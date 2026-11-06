using System.Text.Json;
using ChromaDB.Client.Models;
using ChromaDB.Client.Models.Requests;

namespace ChromaDB.Client;

/// <summary>
/// A conditional transaction on the records of a collection, from <c>ChromaCollectionClient.BeginConditionalTransaction</c>: its reads go
/// at once, and its writes wait in the client until <c>CommitAsync</c>, which the server applies only if no record that the transaction
/// read or writes changed after its reads; otherwise it writes none of them and the client throws a
/// <c>ChromaConditionalWriteConflictException</c>. <c>ChromaCollectionClient.RunConditionalTransactionAsync</c> runs a transaction,
/// commits it, and runs it again after a conflict. As in the clients of Chroma, a transaction works on one collection, without queries
/// and without deletes by filters; an add needs a read of the transaction that did not find the id, an update and a delete a read that
/// found it; it takes one write for each id, and no read of an id after its write; a read without ids needs a positive limit, and the
/// commit checks only the records it returned. Its operations go one at a time: one that starts before the previous one ends throws an
/// <c>InvalidOperationException</c>. Distributed Chroma, the one Chroma Cloud runs on, has conditional transactions for the tenants that
/// the configuration of its frontend enables, and so does VecturaDB; Chroma 1.5.9 does not.
/// </summary>
public class ChromaConditionalTransaction
{
	private readonly ChromaCollectionClient _client;
	// The collection of the first read or write that went, which every operation works on.
	private Guid? _collectionId;
	// The read token of the first read, which the later reads and the commit send.
	private long? _readToken;
	// The ids of the records the reads asked for or returned, which the commit checks.
	private readonly HashSet<string> _readIds = new(StringComparer.Ordinal);
	// The ids a read of ids without filters found, or did not find.
	private readonly HashSet<string> _knownPresent = new(StringComparer.Ordinal);
	private readonly HashSet<string> _knownAbsent = new(StringComparer.Ordinal);
	private readonly HashSet<string> _writtenIds = new(StringComparer.Ordinal);
	private readonly List<ConditionalOperation> _operations = [];
	// After a commit, also one that failed: as an ADO.NET transaction, a transaction whose commit failed is not used again.
	private bool _completed;
	// An operation in progress: as a DbContext of EF Core, the transaction rejects a second one that starts before it ends.
	private int _busy;

	internal ChromaConditionalTransaction(ChromaCollectionClient client)
	{
		_client = client;
	}

	/// <summary>
	/// For a mock in tests, as for <c>ChromaCollectionClient</c>: a transaction without a client, whose members are all virtual. It sends
	/// no request: only the members it overrides work.
	/// </summary>
	protected ChromaConditionalTransaction()
	{
		_client = null!;
	}

	// While the operation of RunConditionalTransactionAsync runs: the run commits, not the operation.
	internal bool CommitBlockedByRun { get; set; }

	// The last error of an operation of the transaction after which RunConditionalTransactionAsync runs the transaction again, when the
	// operation of the run throws it on.
	internal ChromaException? RetryableError { get; private set; }

	internal long? ReadToken => _readToken;

	/// <summary>
	/// Gets the record with the id, or null when the server returns none. Without <c>include</c>, the metadata and the document are
	/// included. Without filters, it tells the transaction whether the record exists, for an add, an update or a delete of the id.
	/// </summary>
	/// <param name="id">The id of the record.</param>
	/// <param name="where">The filter on the metadata, or null for none.</param>
	/// <param name="whereDocument">The filter on the documents, or null for none.</param>
	/// <param name="include">What the results include, or null for the default.</param>
	/// <param name="cancellationToken">The token that cancels the operation.</param>
	/// <returns>The record, or null when the server returns none.</returns>
	public virtual async Task<ChromaCollectionEntry?> GetAsync(string id, ChromaWhereOperator? where = null, ChromaWhereDocumentOperator? whereDocument = null, ChromaGetInclude? include = null, CancellationToken cancellationToken = default)
		=> (await GetAsync([id], where: where, whereDocument: whereDocument, include: include, cancellationToken: cancellationToken)).FirstOrDefault();

	/// <summary>
	/// Gets the records selected by the ids and the filters, in one request, without the pages and the batches of
	/// <c>ChromaCollectionClient.GetAsync</c>. Without <c>include</c>, the metadatas and the documents are included. A later read answers
	/// the records as they were at the first read of the transaction, or the client throws a <c>ChromaStaleReadException</c> when they
	/// changed since. A read of ids without filters, without an offset and without a limit below the number of the ids tells the
	/// transaction which of them exist; a read without ids needs a positive <c>limit</c>.
	/// </summary>
	/// <param name="ids">The ids of the records, or null for the records the filters select.</param>
	/// <param name="where">The filter on the metadata, or null for none.</param>
	/// <param name="whereDocument">The filter on the documents, or null for none.</param>
	/// <param name="limit">The most records to return, or null for all; a read without ids needs one above 0.</param>
	/// <param name="offset">How many records to skip first, or null for none.</param>
	/// <param name="include">What the results include, or null for the default.</param>
	/// <param name="cancellationToken">The token that cancels the operation.</param>
	/// <returns>The records.</returns>
	public virtual Task<IReadOnlyList<ChromaCollectionEntry>> GetAsync(IReadOnlyList<string>? ids = null, ChromaWhereOperator? where = null, ChromaWhereDocumentOperator? whereDocument = null, int? limit = null, int? offset = null, ChromaGetInclude? include = null, CancellationToken cancellationToken = default)
	{
		EnsureOpen();
		return Retryable(() => _client.ConditionalGet(this, ids, where, whereDocument, limit, offset, include, cancellationToken));
	}

	/// <summary>
	/// Adds the records with the commit: each id needs a read of the transaction that did not find it. The client prepares them as
	/// <c>ChromaCollectionClient.AddAsync</c> does, with the embeddings of a generator from <c>WithEmbeddingGenerator</c>.
	/// </summary>
	/// <param name="ids">The ids of the records.</param>
	/// <param name="embeddings">The embeddings, one for each id, or null for none.</param>
	/// <param name="metadatas">The metadata, one for each id, or null for none.</param>
	/// <param name="documents">The documents, one for each id, or null for none.</param>
	/// <param name="cancellationToken">The token that cancels the operation.</param>
	public virtual Task AddAsync(IReadOnlyList<string> ids, IReadOnlyList<ReadOnlyMemory<float>>? embeddings = null, IReadOnlyList<IReadOnlyDictionary<string, object>>? metadatas = null, IReadOnlyList<string>? documents = null, CancellationToken cancellationToken = default)
		=> AddAsync(new ChromaRecords(ids) { Embeddings = embeddings, Metadatas = metadatas, Documents = documents }, cancellationToken);

	/// <summary>
	/// Adds the records with the commit: each id needs a read of the transaction that did not find it. The client prepares them as
	/// <c>ChromaCollectionClient.AddAsync</c> does, with the embeddings of a generator from <c>WithEmbeddingGenerator</c>; at most the
	/// batch size of the server, as they go in one request.
	/// </summary>
	/// <param name="records">The records: ids, and embeddings, metadatas, documents and URIs when given.</param>
	/// <param name="cancellationToken">The token that cancels the operation.</param>
	public virtual Task AddAsync(ChromaRecords records, CancellationToken cancellationToken = default)
		=> Write("add", records, cancellationToken);

	/// <summary>
	/// Updates the records with the commit: each id needs a read of the transaction that found it. The client prepares them as
	/// <c>ChromaCollectionClient.UpdateAsync</c> does.
	/// </summary>
	/// <param name="ids">The ids of the records.</param>
	/// <param name="embeddings">The embeddings, one for each id, or null for none.</param>
	/// <param name="metadatas">The metadata, one for each id, or null for none.</param>
	/// <param name="documents">The documents, one for each id, or null for none.</param>
	/// <param name="cancellationToken">The token that cancels the operation.</param>
	public virtual Task UpdateAsync(IReadOnlyList<string> ids, IReadOnlyList<ReadOnlyMemory<float>>? embeddings = null, IReadOnlyList<IReadOnlyDictionary<string, object>>? metadatas = null, IReadOnlyList<string>? documents = null, CancellationToken cancellationToken = default)
		=> UpdateAsync(new ChromaRecords(ids) { Embeddings = embeddings, Metadatas = metadatas, Documents = documents }, cancellationToken);

	/// <summary>
	/// Updates the records with the commit: each id needs a read of the transaction that found it. The client prepares them as
	/// <c>ChromaCollectionClient.UpdateAsync</c> does, without reading the records first, and without <c>UpsertStrategy</c>; at most the
	/// batch size of the server, as they go in one request.
	/// </summary>
	/// <param name="records">The records: ids, and embeddings, metadatas, documents and URIs when given.</param>
	/// <param name="cancellationToken">The token that cancels the operation.</param>
	public virtual Task UpdateAsync(ChromaRecords records, CancellationToken cancellationToken = default)
		=> Write("update", records, cancellationToken);

	/// <summary>
	/// Adds the records with the commit, or updates the ones that exist. The client prepares them as <c>ChromaCollectionClient.UpsertAsync</c>
	/// does.
	/// </summary>
	/// <param name="ids">The ids of the records.</param>
	/// <param name="embeddings">The embeddings, one for each id, or null for none.</param>
	/// <param name="metadatas">The metadata, one for each id, or null for none.</param>
	/// <param name="documents">The documents, one for each id, or null for none.</param>
	/// <param name="cancellationToken">The token that cancels the operation.</param>
	public virtual Task UpsertAsync(IReadOnlyList<string> ids, IReadOnlyList<ReadOnlyMemory<float>>? embeddings = null, IReadOnlyList<IReadOnlyDictionary<string, object>>? metadatas = null, IReadOnlyList<string>? documents = null, CancellationToken cancellationToken = default)
		=> UpsertAsync(new ChromaRecords(ids) { Embeddings = embeddings, Metadatas = metadatas, Documents = documents }, cancellationToken);

	/// <summary>
	/// Adds the records with the commit, or updates the ones that exist. The client prepares them as <c>ChromaCollectionClient.UpsertAsync</c>
	/// does, without reading the records first, and without <c>UpsertStrategy</c>; at most the batch size of the server, as they go in one
	/// request.
	/// </summary>
	/// <param name="records">The records: ids, and embeddings, metadatas, documents and URIs when given.</param>
	/// <param name="cancellationToken">The token that cancels the operation.</param>
	public virtual Task UpsertAsync(ChromaRecords records, CancellationToken cancellationToken = default)
		=> Write("upsert", records, cancellationToken);

	/// <summary>
	/// Deletes the records with the ids with the commit: each id needs a read of the transaction that found it.
	/// </summary>
	/// <param name="ids">The ids of the records.</param>
	/// <param name="cancellationToken">The token that cancels the operation.</param>
	public virtual Task DeleteAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken = default)
	{
		if (ids is null)
		{
			throw new ArgumentNullException(nameof(ids));
		}
		if (ids.Count == 0)
		{
			throw new ArgumentException("A delete of a conditional transaction needs ids: it has no delete by filters.", nameof(ids));
		}
		EnsureOpen();
		CheckWrite("delete", ids, nameof(ids));
		return Retryable(() => _client.ConditionalDelete(this, ids, cancellationToken));
	}

	/// <summary>
	/// Sends the writes of the transaction, in one request, and the server applies them only if no record that the transaction read or
	/// writes changed after its reads; otherwise it writes none of them, and the client throws a
	/// <c>ChromaConditionalWriteConflictException</c>. A transaction without writes sends no request. After a commit, also one that
	/// failed, the transaction takes no other operation. In the operation of <c>RunConditionalTransactionAsync</c>, which commits, it
	/// throws an <c>InvalidOperationException</c>.
	/// </summary>
	/// <param name="cancellationToken">The token that cancels the operation.</param>
	/// <returns>What the commit wrote.</returns>
	public virtual Task<ChromaConditionalCommitResult> CommitAsync(CancellationToken cancellationToken = default)
	{
		if (CommitBlockedByRun)
		{
			throw new InvalidOperationException("RunConditionalTransactionAsync commits the transaction after its operation: the operation cannot commit it.");
		}
		EnsureOpen();
		return Commit(cancellationToken);
	}

	private async Task<ChromaConditionalCommitResult> Commit(CancellationToken cancellationToken)
	{
		if (_operations.Count == 0)
		{
			_completed = true;
			return new ChromaConditionalCommitResult();
		}
		var request = new ConditionalCommitRequest()
		{
			ReadToken = _readToken,
			ReadIds = _readIds.OrderBy(id => id, StringComparer.Ordinal).ToList(),
			Operations = _operations.ToList(),
		};
		// A commit that failed may have gone, as after a timeout, and one that the server refused would be refused again.
		return await Retryable(async () =>
		{
			try
			{
				return await _client.ConditionalCommit(this, request, cancellationToken);
			}
			finally
			{
				_completed = true;
			}
		});
	}

	private Task Write(string operation, ChromaRecords records, CancellationToken cancellationToken)
	{
		EnsureOpen();
		return Retryable(() => _client.ConditionalWrite(this, operation, records, cancellationToken));
	}

	// The errors after which RunConditionalTransactionAsync runs the transaction again, as the clients of Chroma: a conflict, a stale read,
	// and the 429 Backoff of a server that refuses writes for a while.
	internal static bool IsRetryable(ChromaException exception)
		=> exception is ChromaConditionalWriteConflictException or ChromaStaleReadException
			|| (int?)exception.StatusCode == 429 && exception.ErrorType == "Backoff";

	private async Task<T> Retryable<T>(Func<Task<T>> operation)
	{
		if (Interlocked.Exchange(ref _busy, 1) == 1)
		{
			throw new InvalidOperationException("A second operation started on the conditional transaction before the previous one ended: its operations go one at a time.");
		}
		try
		{
			return await operation();
		}
		catch (ChromaException ex) when (IsRetryable(ex))
		{
			RetryableError = ex;
			throw;
		}
		finally
		{
			Volatile.Write(ref _busy, 0);
		}
	}

	private Task Retryable(Func<Task> operation)
		=> Retryable(async () =>
		{
			await operation();
			return true;
		});

	private void EnsureOpen()
	{
		if (_completed)
		{
			throw new InvalidOperationException("The conditional transaction committed, or its commit failed: begin another one.");
		}
	}

	// Every operation works on the collection of the first read or write that went: a client made by name that reads another collection
	// under the name, as after a delete and a create, cannot go on with the transaction.
	internal void Scope(Guid collectionId)
	{
		if (_collectionId is { } first && first != collectionId)
		{
			throw new InvalidOperationException($"A conditional transaction works on one collection: it began on {first}, and the collection of the client is now {collectionId}.");
		}
	}

	// Before a read: no read of an id after its write, and a read without ids needs a positive limit.
	internal void CheckRead(IReadOnlyList<string>? ids, int? limit)
	{
		if (ids is not null)
		{
			foreach (var id in ids)
			{
				if (_writtenIds.Contains(id))
				{
					throw ReadAfterWrite(id);
				}
			}
			return;
		}
		if (limit is not > 0)
		{
			throw new ArgumentException("A read of a conditional transaction without ids needs a positive limit.", nameof(limit));
		}
	}

	// After a read: every read has the read token of the first one. The ids it asked for and the ones it returned are read, and a read of
	// ids whose answer has every record of them, without filters, without an offset and without a limit below their number, tells which
	// of them exist. The clients of Chroma take a read with a limit or an offset for such a read, and an id it cut out of the answer for
	// one that does not exist.
	internal void RecordRead(Guid collectionId, IReadOnlyList<string>? ids, bool complete, IReadOnlyList<string> returned, ulong? readToken)
	{
		if (readToken is not { } token)
		{
			throw new ChromaException("The answer to a read of the conditional transaction has no read token.");
		}
		if (token > long.MaxValue)
		{
			throw new ChromaException($"The read token {token} of the answer is beyond the 64-bit signed numbers of Chroma.");
		}
		if (_readToken is { } first && first != (long)token)
		{
			throw new ChromaException($"The read token of the conditional transaction changed from {first} to {token}.");
		}
		foreach (var id in returned)
		{
			if (_writtenIds.Contains(id))
			{
				throw ReadAfterWrite(id);
			}
		}
		if (ids is not null)
		{
			_readIds.UnionWith(ids);
		}
		foreach (var id in returned)
		{
			_readIds.Add(id);
			_knownPresent.Add(id);
			_knownAbsent.Remove(id);
		}
		if (ids is not null && complete)
		{
			foreach (var id in ids.Except(returned, StringComparer.Ordinal))
			{
				_knownAbsent.Add(id);
				_knownPresent.Remove(id);
			}
		}
		_readToken ??= (long)token;
		_collectionId ??= collectionId;
	}

	private static InvalidOperationException ReadAfterWrite(string id)
		=> new($"The conditional transaction cannot read the record \"{id}\" after its write.");

	// Before a write, and again when it waits for the commit: one write for each id, an add of an id that a read did not find, an update
	// and a delete of one that a read found.
	internal void CheckWrite(string operation, IReadOnlyList<string> ids, string paramName)
	{
		var given = new HashSet<string>(StringComparer.Ordinal);
		foreach (var id in ids)
		{
			if (!given.Add(id))
			{
				throw new ArgumentException($"The {operation} gives the id \"{id}\" twice: a conditional transaction takes one write for each id.", paramName);
			}
			if (_writtenIds.Contains(id))
			{
				throw new InvalidOperationException($"The conditional transaction has a write of \"{id}\" already: it takes one write for each id.");
			}
			if (operation == "add" && !_knownAbsent.Contains(id))
			{
				throw new InvalidOperationException($"The add of \"{id}\" needs a read of the conditional transaction that did not find it: a read of its id without filters.");
			}
			if (operation is "update" or "delete" && !_knownPresent.Contains(id))
			{
				throw new InvalidOperationException($"The {operation} of \"{id}\" needs a read of the conditional transaction that found it.");
			}
		}
	}

	// The write, prepared as outside a transaction, waits for the commit.
	internal void Buffer(Guid collectionId, string operation, IReadOnlyList<string> ids, JsonElement payload, string paramName)
	{
		EnsureOpen();
		CheckWrite(operation, ids, paramName);
		_writtenIds.UnionWith(ids);
		_operations.Add(new ConditionalOperation() { Operation = operation, Payload = payload });
		_collectionId ??= collectionId;
	}
}
