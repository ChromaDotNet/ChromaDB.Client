using ChromaDB.Client.Common;
using ChromaDB.Client.Models;
using ChromaDB.Client.Models.Requests;
using ChromaDB.Client.Models.Responses;

namespace ChromaDB.Client;

/// <summary>
/// The client for the records of a collection, and for the operations on the collection itself, like <c>CountAsync</c>,
/// <c>ModifyAsync</c> and <c>ForkAsync</c>.
/// </summary>
public class ChromaCollectionClient
{
	private readonly ChromaCollection _collection;
	private readonly ChromaHttpClient _httpClient;
	private readonly string _tenant;
	private readonly string _database;
	private readonly Uri _server;

	/// <summary>
	/// Creates a client for the records of the collection, which sends its requests with the options and the
	/// <c>HttpClient</c>. The requests go to the tenant and database of the collection, or else to those of the
	/// options.
	/// </summary>
	public ChromaCollectionClient(ChromaCollection collection, ChromaConfigurationOptions options, HttpClient httpClient)
		: this(collection, options, new ChromaHttpClient(httpClient, options))
	{ }

	// Shares the ChromaHttpClient of a ChromaClient, with what it keeps, like the version of the server.
	internal ChromaCollectionClient(ChromaCollection collection, ChromaConfigurationOptions options, ChromaHttpClient httpClient)
	{
		_collection = collection;
		_httpClient = httpClient;
		_server = options.Uri;
		// An empty tenant or database means the default, as in ChromaClient.
		_tenant = collection.Tenant is not null and not [] ? collection.Tenant
			: options.Tenant is not null and not [] ? options.Tenant
			: ClientConstants.DefaultTenantName;
		_database = collection.Database is not null and not [] ? collection.Database
			: options.Database is not null and not [] ? options.Database
			: ClientConstants.DefaultDatabaseName;
	}

	/// <summary>
	/// Creates a client without getting the collection first: the requests on a collection need only its id, and the
	/// tenant and database of the options.
	/// </summary>
	public ChromaCollectionClient(Guid collectionId, string collectionName, ChromaConfigurationOptions options, HttpClient httpClient)
		: this(new ChromaCollection(collectionName) { Id = collectionId }, options, httpClient)
	{ }

	// The span and the duration of each operation on the collection.
	private Task<T> Operation<T>(string name, Func<Task<T>> body)
		=> ChromaInstrumentation.Run(name, _collection.Name, $"{_tenant}|{_database}", _server, body);

	private Task Operation(string name, Func<Task> body)
		=> ChromaInstrumentation.Run(name, _collection.Name, $"{_tenant}|{_database}", _server, body);

	/// <summary>
	/// The collection the client works on.
	/// </summary>
	public ChromaCollection Collection => _collection;

	/// <summary>
	/// Gets the record with the id, or null when the server returns none. Without <c>include</c>, the metadata and the
	/// document are included.
	/// </summary>
	public async Task<ChromaCollectionEntry?> GetAsync(string id, ChromaWhereOperator? where = null, ChromaWhereDocumentOperator? whereDocument = null, ChromaGetInclude? include = null, CancellationToken cancellationToken = default)
		=> (await GetAsync([id], where: where, whereDocument: whereDocument, include: include, cancellationToken: cancellationToken)).FirstOrDefault();

	/// <summary>
	/// Gets the records selected by the ids and the filters, a page at a time with <c>limit</c> and <c>offset</c>. Without
	/// <c>include</c>, the metadatas and the documents are included. By default, unless <c>WithBatchSplitting(false)</c>, more
	/// records than the batch size are read in pages, ids beyond it in batches: Chroma Cloud answers at most 300 records, without
	/// an error.
	/// </summary>
	public Task<IReadOnlyList<ChromaCollectionEntry>> GetAsync(IReadOnlyList<string>? ids = null, ChromaWhereOperator? where = null, ChromaWhereDocumentOperator? whereDocument = null, int? limit = null, int? offset = null, ChromaGetInclude? include = null, CancellationToken cancellationToken = default)
		=> Operation<IReadOnlyList<ChromaCollectionEntry>>("get", async () =>
		{
			// With batch splitting, on by default, more records than the batch size come in pages: Chroma Cloud answers at most 300 records, without
			// an error. The ids go in batches, each read whole, and the limit and the offset apply to all of them together; without ids
			// beyond the batch size, pages of the batch size follow the offset until the limit or a page that is not full.
			if (!_httpClient.BatchSplitting || await BatchSize(cancellationToken) is not { } size
				|| ids is { } few && few.Count <= size || ids is null && limit <= size)
			{
				return await GetPage(ids, where, whereDocument, limit, offset, include, cancellationToken);
			}
			var entries = new List<ChromaCollectionEntry>();
			if (ids is not null)
			{
				for (var i = 0; i < ids.Count; i += size)
				{
					entries.AddRange(await GetPage(ids.Skip(i).Take(size).ToList(), where, whereDocument, null, null, include, cancellationToken));
				}
				return entries.Skip(offset ?? 0).Take(limit ?? int.MaxValue).ToList();
			}
			var start = offset ?? 0;
			while (limit is null || entries.Count < limit)
			{
				var take = limit is { } total ? Math.Min(size, total - entries.Count) : size;
				var page = await GetPage(null, where, whereDocument, take, start, include, cancellationToken);
				entries.AddRange(page);
				if (page.Count < take)
				{
					break;
				}
				start += take;
			}
			return entries;
		});

	private async Task<List<ChromaCollectionEntry>> GetPage(IReadOnlyList<string>? ids, ChromaWhereOperator? where, ChromaWhereDocumentOperator? whereDocument, int? limit, int? offset, ChromaGetInclude? include, CancellationToken cancellationToken)
	{
		var requestParams = new RequestQueryParams()
			.Insert("{tenant}", _tenant)
			.Insert("{database}", _database)
			.Insert("{collection_id}", _collection.Id);
		var request = new CollectionGetRequest()
		{
			Ids = ids,
			Where = where?.ToWhere(),
			WhereDocument = whereDocument?.ToWhereDocument(),
			Limit = limit,
			Offset = offset,
			Include = (include ?? ChromaGetInclude.Metadatas | ChromaGetInclude.Documents).ToInclude(),
		};
		var response = await _httpClient.Post<CollectionGetRequest, CollectionEntriesGetResponse>(_httpClient.Routes.Collection + "/get", request, requestParams, cancellationToken);
		return response.Map() ?? [];
	}

	/// <summary>
	/// Searches the <c>nResults</c> records nearest to the query embedding. Without <c>include</c>, the metadatas, the
	/// documents and the distances are included.
	/// </summary>
	public async Task<IReadOnlyList<ChromaCollectionQueryEntry>> QueryAsync(ReadOnlyMemory<float> queryEmbeddings, int nResults = 10, ChromaWhereOperator? where = null, ChromaWhereDocumentOperator? whereDocument = null, ChromaQueryInclude? include = null, CancellationToken cancellationToken = default)
		=> (await QueryAsync([queryEmbeddings], nResults: nResults, where: where, whereDocument: whereDocument, include: include, cancellationToken: cancellationToken)).FirstOrDefault() ?? [];

	/// <summary>
	/// Searches the <c>nResults</c> records nearest to each query embedding, and returns one list of results per
	/// embedding. Without <c>include</c>, the metadatas, the documents and the distances are included.
	/// </summary>
	public Task<IReadOnlyList<IReadOnlyList<ChromaCollectionQueryEntry>>> QueryAsync(IReadOnlyList<ReadOnlyMemory<float>> queryEmbeddings, int nResults = 10, ChromaWhereOperator? where = null, ChromaWhereDocumentOperator? whereDocument = null, ChromaQueryInclude? include = null, CancellationToken cancellationToken = default)
		=> QueryAsync(new ChromaQuery(queryEmbeddings) { NResults = nResults, Where = where, WhereDocument = whereDocument, Include = include }, cancellationToken);

	/// <summary>
	/// Runs the query and returns one list of results per query embedding. When the query has ids and the server
	/// searches outside them, as Chroma 0.x does, it throws a <c>ChromaException</c> instead of returning the results.
	/// </summary>
	public Task<IReadOnlyList<IReadOnlyList<ChromaCollectionQueryEntry>>> QueryAsync(ChromaQuery query, CancellationToken cancellationToken = default)
		=> Operation<IReadOnlyList<IReadOnlyList<ChromaCollectionQueryEntry>>>("query", async () =>
		{
			var requestParams = new RequestQueryParams()
				.Insert("{tenant}", _tenant)
				.Insert("{database}", _database)
				.Insert("{collection_id}", _collection.Id);
			var request = new CollectionQueryRequest()
			{
				QueryEmbeddings = query.QueryEmbeddings,
				NResults = query.NResults,
				Where = query.Where?.ToWhere(),
				WhereDocument = query.WhereDocument?.ToWhereDocument(),
				Include = (query.Include ?? ChromaQueryInclude.Metadatas | ChromaQueryInclude.Documents | ChromaQueryInclude.Distances).ToInclude(),
				Ids = query.Ids,
			};
			var response = await _httpClient.Post<CollectionQueryRequest, CollectionEntriesQueryResponse>(_httpClient.Routes.Collection + "/query", request, requestParams, cancellationToken);
			var result = response.Map() ?? [];
			// Chroma 0.x ignores the ids and searches all the records: a result outside the ids shows it. When all the results
			// are among the ids, they are also the nearest among them, so the answer is right on those servers too.
			if (query.Ids is not null)
			{
				var ids = new HashSet<string>(query.Ids);
				if (result.Any(entries => entries.Any(entry => !ids.Contains(entry.Id))))
				{
					throw new ChromaException("The server searched outside the ids of the query: it does not support them. Chroma 1.0.0 and later do.");
				}
			}
			return result;
		});

	/// <summary>
	/// Adds the records with the ids, embeddings, metadatas and documents. Since Chroma 1.0.16 the server requires the
	/// embeddings: the client does not compute them.
	/// </summary>
	public Task AddAsync(IReadOnlyList<string> ids, IReadOnlyList<ReadOnlyMemory<float>>? embeddings = null, IReadOnlyList<IReadOnlyDictionary<string, object>>? metadatas = null, IReadOnlyList<string>? documents = null, CancellationToken cancellationToken = default)
		=> AddAsync(new ChromaRecords(ids) { Embeddings = embeddings, Metadatas = metadatas, Documents = documents }, cancellationToken);

	/// <summary>
	/// Adds the records; by default, unless <c>WithBatchSplitting(false)</c>, they go in batches of the <c>max_batch_size</c> of
	/// the server. Since Chroma 1.0.16 the server requires the embeddings: the client does not compute them. It computes the sparse
	/// vectors of the <c>chroma_bm25</c> indexes of the schema that have a source key, as the Python client of Chroma does.
	/// </summary>
	public Task AddAsync(ChromaRecords records, CancellationToken cancellationToken = default)
		=> Operation("add", async () =>
		{
			records = WithSparseVectors(records);
			await CheckListsInMetadata(records, cancellationToken);
			var base64 = records.Embeddings is not null && await _httpClient.SupportsBase64Embeddings(cancellationToken);
			var requestParams = new RequestQueryParams()
				.Insert("{tenant}", _tenant)
				.Insert("{database}", _database)
				.Insert("{collection_id}", _collection.Id);
			await InBatches(records, async batch =>
			{
				var request = new CollectionAddRequest()
				{
					Ids = batch.Ids,
					Embeddings = batch.Embeddings is { } embeddings ? new ChromaEmbeddings(embeddings, base64) : null,
					Metadatas = batch.Metadatas,
					Documents = batch.Documents,
					Uris = batch.Uris,
				};
				await _httpClient.Post(_httpClient.Routes.Collection + "/add", request, requestParams, cancellationToken);
			}, cancellationToken);
		});

	/// <summary>
	/// Updates the embeddings, metadatas and documents of the records with the ids.
	/// </summary>
	public Task UpdateAsync(IReadOnlyList<string> ids, IReadOnlyList<ReadOnlyMemory<float>>? embeddings = null, IReadOnlyList<IReadOnlyDictionary<string, object>>? metadatas = null, IReadOnlyList<string>? documents = null, CancellationToken cancellationToken = default)
		=> UpdateAsync(new ChromaRecords(ids) { Embeddings = embeddings, Metadatas = metadatas, Documents = documents }, cancellationToken);

	/// <summary>
	/// Updates the records with the ids; by default, unless <c>WithBatchSplitting(false)</c>, they go in batches of the
	/// <c>max_batch_size</c> of the server. The client computes the sparse vectors of the <c>chroma_bm25</c> indexes of the schema
	/// that have a source key, as the Python client of Chroma does.
	/// </summary>
	public Task UpdateAsync(ChromaRecords records, CancellationToken cancellationToken = default)
		=> Operation("update", async () =>
		{
			records = WithSparseVectors(records);
			await CheckListsInMetadata(records, cancellationToken);
			var base64 = records.Embeddings is not null && await _httpClient.SupportsBase64Embeddings(cancellationToken);
			var requestParams = new RequestQueryParams()
				.Insert("{tenant}", _tenant)
				.Insert("{database}", _database)
				.Insert("{collection_id}", _collection.Id);
			await InBatches(records, async batch =>
			{
				var request = new CollectionUpdateRequest()
				{
					Ids = batch.Ids,
					Embeddings = batch.Embeddings is { } embeddings ? new ChromaEmbeddings(embeddings, base64) : null,
					Metadatas = batch.Metadatas,
					Documents = batch.Documents,
					Uris = batch.Uris,
				};
				await _httpClient.Post(_httpClient.Routes.Collection + "/update", request, requestParams, cancellationToken);
			}, cancellationToken);
		});

	/// <summary>
	/// Adds the records with the ids, or updates the ones that already exist. Since Chroma 1.0.16 the server requires
	/// the embeddings: the client does not compute them.
	/// </summary>
	public Task UpsertAsync(IReadOnlyList<string> ids, IReadOnlyList<ReadOnlyMemory<float>>? embeddings = null, IReadOnlyList<IReadOnlyDictionary<string, object>>? metadatas = null, IReadOnlyList<string>? documents = null, CancellationToken cancellationToken = default)
		=> UpsertAsync(new ChromaRecords(ids) { Embeddings = embeddings, Metadatas = metadatas, Documents = documents }, cancellationToken);

	/// <summary>
	/// Adds the records, or updates the ones that already exist; by default, unless <c>WithBatchSplitting(false)</c>, they go in
	/// batches of the <c>max_batch_size</c> of the server. Since Chroma 1.0.16 the server requires the embeddings: the client does
	/// not compute them. It computes the sparse vectors of the <c>chroma_bm25</c> indexes of the schema that have a source key, as
	/// the Python client of Chroma does.
	/// </summary>
	public Task UpsertAsync(ChromaRecords records, CancellationToken cancellationToken = default)
		=> Operation("upsert", async () =>
		{
			records = WithSparseVectors(records);
			await CheckListsInMetadata(records, cancellationToken);
			var base64 = records.Embeddings is not null && await _httpClient.SupportsBase64Embeddings(cancellationToken);
			var requestParams = new RequestQueryParams()
				.Insert("{tenant}", _tenant)
				.Insert("{database}", _database)
				.Insert("{collection_id}", _collection.Id);
			await InBatches(records, async batch =>
			{
				var request = new CollectionUpsertRequest()
				{
					Ids = batch.Ids,
					Embeddings = batch.Embeddings is { } embeddings ? new ChromaEmbeddings(embeddings, base64) : null,
					Metadatas = batch.Metadatas,
					Documents = batch.Documents,
					Uris = batch.Uris,
				};
				await _httpClient.Post(_httpClient.Routes.Collection + "/upsert", request, requestParams, cancellationToken);
			}, cancellationToken);
		});

	// As the Python client of Chroma: for each sparse vector index of the schema with a source key and an embedding function, the vector
	// of each record whose metadata does not have the key, from its document (#document) or from the text in its metadata. The records
	// and the metadata of the caller stay as they are: the ones that change are copies.
	private ChromaRecords WithSparseVectors(ChromaRecords records)
	{
		if (records.Metadatas is null && records.Documents is null)
		{
			return records;
		}
		var indexes = _collection.SparseVectorIndexes.Where(index => index.SourceKey is not null && index.EmbeddingFunction is not null).ToList();
		if (indexes.Count == 0)
		{
			return records;
		}
		var metadatas = records.Metadatas?.ToList() ?? records.Ids.Select(_ => (IReadOnlyDictionary<string, object>)null!).ToList();
		var copied = new bool[metadatas.Count];
		foreach (var index in indexes)
		{
			for (var i = 0; i < metadatas.Count; i++)
			{
				var metadata = metadatas[i];
				if (metadata?.ContainsKey(index.Key) == true)
				{
					continue;
				}
				var text = index.SourceKey == ChromaSearchKeys.Document
					? records.Documents is { } documents && i < documents.Count ? documents[i] : null
					: metadata is not null && metadata.TryGetValue(index.SourceKey!, out var value) ? value as string : null;
				if (text is null)
				{
					continue;
				}
				var function = index.Bm25Function ?? throw CannotEmbed(index);
				var copy = copied[i] ? (Dictionary<string, object>)metadata! : metadata?.ToDictionary(x => x.Key, x => x.Value) ?? [];
				copy[index.Key] = function.Embed(text);
				metadatas[i] = copy;
				copied[i] = true;
			}
		}
		return !copied.Contains(true) ? records : new ChromaRecords(records.Ids)
		{
			Embeddings = records.Embeddings,
			Metadatas = metadatas,
			Documents = records.Documents,
			Uris = records.Uris,
		};
	}

	// The vector of a text query of SparseKnn, with the function of the sparse vector index of the key, as the Python client of Chroma does.
	private ChromaSparseVector EmbedText(string key, string text)
	{
		var index = _collection.SparseVectorIndexes.FirstOrDefault(index => index.Key == key)
			?? throw new ChromaException($"A text query on \"{key}\" needs a sparse vector index on that key in the schema of the collection, as GetCollection returns it; or give the sparse vector.");
		return (index.Bm25Function ?? throw CannotEmbed(index)).Embed(text);
	}

	private static ChromaException CannotEmbed(ChromaSparseVectorIndex index)
		=> new($"The sparse vectors of \"{index.Key}\" come from the embedding function \"{index.EmbeddingFunction}\" of the schema, which the client cannot compute: it computes chroma_bm25. Give the sparse vectors yourself.");

	// The records in batches of the batch size, one request after the other, with batch splitting, on by default. Chroma Cloud
	// rejects a batch beyond its quota of records, "current usage of 301 exceeds limit of 300", before it writes any of it: the
	// client keeps that limit for the server, and sends that batch and the rest in batches of it.
	private async Task InBatches(ChromaRecords records, Func<ChromaRecords, Task> send, CancellationToken cancellationToken)
	{
		var total = records.Ids.Count;
		var offset = 0;
		do
		{
			var size = _httpClient.BatchSplitting ? await BatchSize(cancellationToken) : null;
			var count = size is { } limit ? Math.Min(limit, total - offset) : total - offset;
			var batch = offset == 0 && count == total ? records : Slice(records, offset, count);
			try
			{
				await send(batch);
				offset += count;
			}
			catch (ChromaException ex) when (_httpClient.BatchSplitting && RecordsQuota(ex) is { } quota && quota < count)
			{
				_httpClient.LearnRecordsLimit(quota);
			}
		}
		while (offset < total);
	}

	private static ChromaRecords Slice(ChromaRecords records, int offset, int count)
		=> new(records.Ids.Skip(offset).Take(count).ToList())
		{
			Embeddings = records.Embeddings?.Skip(offset).Take(count).ToList(),
			Metadatas = records.Metadatas?.Skip(offset).Take(count).ToList(),
			Documents = records.Documents?.Skip(offset).Take(count).ToList(),
			Uris = records.Uris?.Skip(offset).Take(count).ToList(),
		};

	// "Quota exceeded: 'Number of records' exceeded quota limit for action 'Add': current usage of 301 exceeds limit of 300".
	private static readonly System.Text.RegularExpressions.Regex RecordsQuotaMessage = new("'Number of records' exceeded quota limit .* exceeds limit of ([0-9]+)");

	private static int? RecordsQuota(ChromaException exception)
		=> RecordsQuotaMessage.Match(exception.Message) is { Success: true } match
			&& int.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var limit) && limit > 0
			? limit
			: null;

	// The smallest of the limit of the caller, 300 on Chroma Cloud when not given, the max_batch_size the server declares, and the
	// quota of records the server enforced.
	private async Task<int?> BatchSize(CancellationToken cancellationToken)
		=> new[] { _httpClient.MaxBatchSize, await _httpClient.GetMaxBatchSize(cancellationToken), _httpClient.RecordsLimit }.Min();

	// The 0.x servers accept lists in metadata but drop them without an error; Chroma 1.0 to 1.4 reject them, 1.5.0 stores them.
	// Sparse vectors too: Chroma 0.6.3 accepts them and stores the metadata as null.
	private async Task CheckListsInMetadata(ChromaRecords records, CancellationToken cancellationToken)
	{
		if (records.Metadatas?.Any(metadata => metadata?.Values.Any(IsList) == true) == true
			&& await _httpClient.IsChroma0(cancellationToken))
		{
			throw new ChromaException("Chroma 0.x drops the lists in metadata without an error: they need Chroma 1.5.0 or later.");
		}
		if (records.Metadatas?.Any(metadata => metadata?.Values.Any(IsSparseVector) == true) == true
			&& await _httpClient.IsChroma0(cancellationToken))
		{
			throw new ChromaException("Chroma 0.x drops the sparse vectors in metadata without an error: only Chroma Cloud stores them.");
		}
	}

	// A tagged JsonElement object is what the client returns for a sparse vector read with ChromaMetadataValues.Inferred.
	private static bool IsSparseVector(object? value)
		=> value is ChromaSparseVector
			|| value is System.Text.Json.JsonElement element && ChromaSparseVectorConverter.IsTagged(element);

	// A JsonElement array is what the client returns for a list read with ChromaMetadataValues.Inferred.
	private static bool IsList(object? value)
		=> value is System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.Array }
			or System.Collections.IEnumerable and not string and not System.Collections.IDictionary;

	/// <summary>
	/// Deletes the records with the ids, sending the filters with them when given; by default, unless
	/// <c>WithBatchSplitting(false)</c>, the ids go in batches of the <c>max_batch_size</c> of the server.
	/// </summary>
	public Task DeleteAsync(IReadOnlyList<string> ids, ChromaWhereOperator? where = null, ChromaWhereDocumentOperator? whereDocument = null, CancellationToken cancellationToken = default)
		=> Operation("delete", async () =>
		{
			var requestParams = new RequestQueryParams()
				.Insert("{tenant}", _tenant)
				.Insert("{database}", _database)
				.Insert("{collection_id}", _collection.Id);
			await InBatches(new ChromaRecords(ids), async batch =>
			{
				var request = new CollectionDeleteRequest()
				{
					Ids = batch.Ids,
					Where = where?.ToWhere(),
					WhereDocument = whereDocument?.ToWhereDocument(),
				};
				await _httpClient.Post(_httpClient.Routes.Collection + "/delete", request, requestParams, cancellationToken);
			}, cancellationToken);
		});

	/// <summary>
	/// Deletes the records with the ids, the ones the filters match, or both, at most <c>delete.Limit</c> of them.
	/// Returns how many records were deleted when the server says it, from Chroma 1.5.3; null otherwise.
	/// </summary>
	public Task<int?> DeleteAsync(ChromaDelete delete, CancellationToken cancellationToken = default)
		=> Operation("delete", async () =>
		{
			// The rules of Chroma and of its Python client, checked before any request.
			if (delete.Ids is null && delete.Where is null && delete.WhereDocument is null)
			{
				throw new ArgumentException("A delete needs ids, a where filter or a where document filter: without them it would select every record.", nameof(delete));
			}
			if (delete.Ids is [])
			{
				throw new ArgumentException("The ids of a delete cannot be empty: leave them null to delete by the filters only.", nameof(delete));
			}
			if (delete.Limit is < 0)
			{
				throw new ArgumentOutOfRangeException(nameof(delete), "The limit of a delete cannot be negative.");
			}
			if (delete.Limit is not null && delete.Where is null && delete.WhereDocument is null)
			{
				throw new ArgumentException("The limit of a delete needs a where or where document filter: Chroma rejects it with the ids alone.", nameof(delete));
			}
			if (delete.Limit is not null && !await _httpClient.SupportsDeleteLimit(cancellationToken))
			{
				throw new ChromaException("The server ignores the limit of a delete and would delete every matching record: Chroma 1.5.3 and later apply it.");
			}
			var requestParams = new RequestQueryParams()
				.Insert("{tenant}", _tenant)
				.Insert("{database}", _database)
				.Insert("{collection_id}", _collection.Id);
			int? deleted = null;
			var remaining = delete.Limit;
			var sent = false;
			async Task Send(IReadOnlyList<string>? ids)
			{
				// The limit is used up: the next batches would delete nothing. The first request goes anyway, also with a limit of 0.
				if (sent && remaining is <= 0)
				{
					return;
				}
				sent = true;
				var request = new CollectionDeleteRequest()
				{
					Ids = ids,
					Where = delete.Where?.ToWhere(),
					WhereDocument = delete.WhereDocument?.ToWhereDocument(),
					Limit = remaining,
				};
				// {"deleted": n} from Chroma 1.5.3; {} or null before, and a list of ids from some 0.x servers.
				var response = await _httpClient.Post<CollectionDeleteRequest, System.Text.Json.JsonElement>(_httpClient.Routes.Collection + "/delete", request, requestParams, cancellationToken);
				if (response.ValueKind == System.Text.Json.JsonValueKind.Object
					&& response.TryGetProperty("deleted", out var count) && count.ValueKind == System.Text.Json.JsonValueKind.Number)
				{
					deleted = (deleted ?? 0) + count.GetInt32();
					remaining -= count.GetInt32();
				}
			}
			if (delete.Ids is null)
			{
				await Send(null);
			}
			else
			{
				await InBatches(new ChromaRecords(delete.Ids), batch => Send(batch.Ids), cancellationToken);
			}
			return deleted;
		});

	/// <summary>
	/// Counts the records of the collection.
	/// </summary>
	public Task<int> CountAsync(CancellationToken cancellationToken = default)
		=> Operation("count", async () =>
		{
			var requestParams = new RequestQueryParams()
				.Insert("{tenant}", _tenant)
				.Insert("{database}", _database)
				.Insert("{collection_id}", _collection.Id);
			return await _httpClient.Get<int>(_httpClient.Routes.Collection + "/count", requestParams, cancellationToken);
		});

	/// <summary>
	/// The count at a read level: on Chroma Cloud, <c>ChromaReadLevel.IndexOnly</c> leaves out the records not indexed
	/// yet.
	/// </summary>
	public Task<int> CountAsync(ChromaReadLevel readLevel, CancellationToken cancellationToken = default)
		=> Operation("count", async () =>
		{
			var requestParams = new RequestQueryParams()
				.Insert("{tenant}", _tenant)
				.Insert("{database}", _database)
				.Insert("{collection_id}", _collection.Id)
				.Insert("{read_level}", ReadLevelName(readLevel));
			return await _httpClient.Get<int>(_httpClient.Routes.Collection + "/count?read_level={read_level}", requestParams, cancellationToken);
		});

	/// <summary>
	/// Runs one search with the Search API of Chroma, which only Chroma Cloud serves: a single server answers <c>501</c>.
	/// </summary>
	public async Task<IReadOnlyList<ChromaSearchEntry>> SearchAsync(ChromaSearch search, ChromaReadLevel? readLevel = null, CancellationToken cancellationToken = default)
		=> (await SearchAsync([search], readLevel, cancellationToken)).Single();

	/// <summary>
	/// Runs several searches in one request with the Search API of Chroma, which only Chroma Cloud serves: a single server answers
	/// <c>501</c>. The results come in the order of the searches. With <c>ChromaReadLevel.IndexOnly</c> the records not indexed yet are
	/// left out.
	/// </summary>
	public Task<IReadOnlyList<IReadOnlyList<ChromaSearchEntry>>> SearchAsync(IReadOnlyList<ChromaSearch> searches, ChromaReadLevel? readLevel = null, CancellationToken cancellationToken = default)
		=> Operation<IReadOnlyList<IReadOnlyList<ChromaSearchEntry>>>("search", async () =>
		{
			if (searches is not { Count: > 0 })
			{
				throw new ArgumentException("At least one search is needed.", nameof(searches));
			}
			foreach (var search in searches)
			{
				if (search.Offset < 0 || search.Limit is <= 0)
				{
					throw new ArgumentOutOfRangeException(nameof(searches), "The offset of a search cannot be negative, and its limit must be positive.");
				}
				if (search.Ids is [])
				{
					throw new ArgumentException("The ids of a search cannot be empty: leave them null to search all the records.", nameof(searches));
				}
			}
			var requestParams = new RequestQueryParams()
				.Insert("{tenant}", _tenant)
				.Insert("{database}", _database)
				.Insert("{collection_id}", _collection.Id);
			var request = new CollectionSearchRequest()
			{
				Searches = searches.Select(search => new CollectionSearchPayload()
				{
					Filter = SearchFilter(search),
					Rank = search.Rank?.ToRank(EmbedText),
					GroupBy = search.GroupBy?.ToGroupBy() ?? [],
					Limit = new CollectionSearchLimit() { Offset = search.Offset, Limit = search.Limit },
					Select = new CollectionSearchSelect() { Keys = search.Select?.Distinct().ToList() ?? [] },
				}).ToList(),
				ReadLevel = readLevel is { } level ? ReadLevelName(level) : null,
			};
			var response = await _httpClient.Post<CollectionSearchRequest, CollectionSearchResponse>(_httpClient.Routes.Collection + "/search", request, requestParams, cancellationToken);
			return response.Ids
				.Select((ids, i) => ids
					.Select((id, j) => new ChromaSearchEntry(id)
					{
						Document = response.Documents?[i]?[j],
						Embedding = response.Embeddings?[i]?[j],
						Metadata = response.Metadatas?[i]?[j],
						Score = response.Scores?[i]?[j],
					})
					.ToList())
				.ToList();
		});

	// The where clause of the Search API holds the metadata, the documents (#document) and the ids (#id); several filters go in $and.
	private static Dictionary<string, object>? SearchFilter(ChromaSearch search)
	{
		var filters = new List<Dictionary<string, object>>();
		if (search.Where is { } where)
		{
			filters.Add(where.ToWhere());
		}
		if (search.WhereDocument is { } whereDocument)
		{
			filters.Add(whereDocument.ToSearchWhere());
		}
		if (search.Ids is { } ids)
		{
			filters.Add(ChromaWhereOperator.In(ChromaSearchKeys.Id, ids.ToArray<object>()).ToWhere());
		}
		return filters switch
		{
			[] => null,
			[var single] => single,
			_ => new() { ["$and"] = filters.Cast<object>().ToArray() },
		};
	}

	private static string ReadLevelName(ChromaReadLevel readLevel) => readLevel switch
	{
		ChromaReadLevel.IndexAndWal => "index_and_wal",
		ChromaReadLevel.IndexOnly => "index_only",
		ChromaReadLevel.IndexAndBoundedWal => "index_and_bounded_wal",
		_ => throw new ArgumentOutOfRangeException(nameof(readLevel)),
	};

	/// <summary>
	/// A copy of the collection under a new name, with the same records: Chroma Cloud only, a single server answers
	/// 501.
	/// </summary>
	public Task<ChromaCollection> ForkAsync(string newName, CancellationToken cancellationToken = default)
		=> Operation("fork", async () =>
		{
			var requestParams = new RequestQueryParams()
				.Insert("{tenant}", _tenant)
				.Insert("{database}", _database)
				.Insert("{collection_id}", _collection.Id);
			var request = new ForkCollectionRequest()
			{
				NewName = newName,
			};
			return await _httpClient.Post<ForkCollectionRequest, ChromaCollection>(_httpClient.Routes.Collection + "/fork", request, requestParams, cancellationToken);
		});

	/// <summary>
	/// How many forks the collection has: Chroma Cloud only.
	/// </summary>
	public Task<int> ForkCountAsync(CancellationToken cancellationToken = default)
		=> Operation("fork_count", async () =>
		{
			var requestParams = new RequestQueryParams()
				.Insert("{tenant}", _tenant)
				.Insert("{database}", _database)
				.Insert("{collection_id}", _collection.Id);
			return (await _httpClient.Get<ForkCountResponse>(_httpClient.Routes.Collection + "/fork_count", requestParams, cancellationToken)).Count;
		});

	/// <summary>
	/// How far the writes to the collection are indexed: Chroma Cloud only.
	/// </summary>
	public Task<ChromaIndexingStatus> GetIndexingStatusAsync(CancellationToken cancellationToken = default)
		=> Operation("get_indexing_status", async () =>
		{
			var requestParams = new RequestQueryParams()
				.Insert("{tenant}", _tenant)
				.Insert("{database}", _database)
				.Insert("{collection_id}", _collection.Id);
			return await _httpClient.Get<ChromaIndexingStatus>(_httpClient.Routes.Collection + "/indexing_status", requestParams, cancellationToken);
		});

	/// <summary>
	/// Attaches a function of Chroma Cloud, like <c>ChromaFunctions.Statistics</c>, under a name of its own; its
	/// results go to the output collection. <c>Created</c> is false when a function with that name was already
	/// attached.
	/// </summary>
	public Task<(ChromaAttachedFunction AttachedFunction, bool Created)> AttachFunctionAsync(string function, string name, string outputCollection, IReadOnlyDictionary<string, object>? parameters = null, CancellationToken cancellationToken = default)
		=> Operation("attach_function", async () =>
		{
			var requestParams = new RequestQueryParams()
				.Insert("{tenant}", _tenant)
				.Insert("{database}", _database)
				.Insert("{collection_id}", _collection.Id);
			var request = new AttachFunctionRequest()
			{
				Name = name,
				FunctionId = function,
				OutputCollection = outputCollection,
				Params = parameters,
			};
			var response = await _httpClient.Post<AttachFunctionRequest, AttachFunctionResponse>(_httpClient.Routes.Collection + "/functions/attach", request, requestParams, cancellationToken);
			return (response.AttachedFunction, response.Created ?? true);
		});

	/// <summary>
	/// Gets the function attached to the collection under the name: Chroma Cloud only.
	/// </summary>
	public Task<ChromaAttachedFunction> GetAttachedFunctionAsync(string name, CancellationToken cancellationToken = default)
		=> Operation("get_attached_function", async () =>
		{
			var requestParams = new RequestQueryParams()
				.Insert("{tenant}", _tenant)
				.Insert("{database}", _database)
				.Insert("{collection_id}", _collection.Id)
				.Insert("{function_name}", name);
			return (await _httpClient.Get<GetAttachedFunctionResponse>(_httpClient.Routes.Collection + "/functions/{function_name}", requestParams, cancellationToken)).AttachedFunction;
		});

	/// <summary>
	/// Detaches the function attached under the name, and deletes its output collection when
	/// <c>deleteOutputCollection</c> is true: Chroma Cloud only. Returns the <c>success</c> of the answer of the
	/// server.
	/// </summary>
	public Task<bool> DetachFunctionAsync(string name, bool deleteOutputCollection = false, CancellationToken cancellationToken = default)
		=> Operation("detach_function", async () =>
		{
			var requestParams = new RequestQueryParams()
				.Insert("{tenant}", _tenant)
				.Insert("{database}", _database)
				.Insert("{collection_id}", _collection.Id)
				.Insert("{function_name}", name);
			var request = new DetachFunctionRequest()
			{
				DeleteOutput = deleteOutputCollection,
			};
			return (await _httpClient.Post<DetachFunctionRequest, DetachFunctionResponse>(_httpClient.Routes.Collection + "/attached_functions/{function_name}/detach", request, requestParams, cancellationToken)).Success;
		});

	/// <summary>
	/// Gets up to <c>limit</c> records of the collection, with a get that sets only the limit.
	/// </summary>
	public Task<IReadOnlyList<ChromaCollectionEntry>> PeekAsync(int limit = 10, CancellationToken cancellationToken = default)
		=> Operation<IReadOnlyList<ChromaCollectionEntry>>("peek", async () =>
		{
			var requestParams = new RequestQueryParams()
				.Insert("{tenant}", _tenant)
				.Insert("{database}", _database)
				.Insert("{collection_id}", _collection.Id);
			var request = new CollectionPeekRequest()
			{
				Limit = limit,
			};
			var response = await _httpClient.Post<CollectionPeekRequest, CollectionEntriesGetResponse>(_httpClient.Routes.Collection + "/get", request, requestParams, cancellationToken);
			return response.Map() ?? [];
		});

	/// <summary>
	/// Changes the name or the metadata of the collection.
	/// </summary>
	public Task ModifyAsync(string? name = null, IReadOnlyDictionary<string, object>? metadata = null, CancellationToken cancellationToken = default)
		=> Operation("modify", async () =>
		{
			var requestParams = new RequestQueryParams()
				.Insert("{tenant}", _tenant)
				.Insert("{database}", _database)
				.Insert("{collection_id}", _collection.Id);
			var request = new CollectionModifyRequest()
			{
				Name = name,
				Metadata = metadata,
			};
			await _httpClient.Put(_httpClient.Routes.Collection, request, requestParams, cancellationToken);
		});

	/// <summary>
	/// Changes the settings of the index, like <c>ef_search</c>. Chroma 1.0.6 and later apply them; the earlier
	/// versions answer without applying them, so on them the client throws a <c>ChromaException</c> before sending the
	/// request. The settings must be those of the index of the collection: HNSW on a single server, SPANN on Chroma
	/// Cloud. Chroma Cloud answers 500 to HNSW settings, and a single server answers without applying SPANN settings:
	/// the client throws before both.
	/// </summary>
	public Task ModifyConfigurationAsync(ChromaCollectionConfigurationUpdate configuration, CancellationToken cancellationToken = default)
		=> Operation("modify", async () =>
		{
			var current = await CurrentConfiguration(cancellationToken);
			if (current is not { ValueKind: System.Text.Json.JsonValueKind.Object } value
				|| !value.TryGetProperty("hnsw", out var hnsw) && !value.TryGetProperty("spann", out _))
			{
				throw new ChromaException("The server answers without applying a new configuration: Chroma 1.0.6 and later apply it.");
			}
			var hasHnsw = hnsw.ValueKind == System.Text.Json.JsonValueKind.Object;
			var hasSpann = value.TryGetProperty("spann", out var spann) && spann.ValueKind == System.Text.Json.JsonValueKind.Object;
			if (configuration.Hnsw is not null && !hasHnsw && hasSpann)
			{
				throw new ChromaException("The collection has a SPANN index, as on Chroma Cloud, which rejects HNSW settings: set Spann instead.");
			}
			if (configuration.Spann is not null && !hasSpann && hasHnsw)
			{
				throw new ChromaException("The collection has an HNSW index, as on a single Chroma server, which answers without applying SPANN settings: set Hnsw instead.");
			}
			var requestParams = new RequestQueryParams()
				.Insert("{tenant}", _tenant)
				.Insert("{database}", _database)
				.Insert("{collection_id}", _collection.Id);
			var request = new CollectionModifyRequest()
			{
				Configuration = configuration,
			};
			await _httpClient.Put(_httpClient.Routes.Collection, request, requestParams, cancellationToken);
		});

	// Chroma 1.0.6 and later, Chroma Cloud too, send the configuration with "hnsw" and "spann", one of them null; 0.5.4 to 1.0.5
	// with "hnsw_configuration", and 0.4.10 to 0.5.3 send none. Without the configuration at hand, the collection is read by its name.
	private async Task<System.Text.Json.JsonElement?> CurrentConfiguration(CancellationToken cancellationToken)
	{
		var configuration = _collection.ConfigurationJson;
		if (configuration is null)
		{
			var requestParams = new RequestQueryParams()
				.Insert("{collectionName}", _collection.Name)
				.Insert("{tenant}", _tenant)
				.Insert("{database}", _database);
			configuration = (await _httpClient.Get<ChromaCollection>(_httpClient.Routes.CollectionByName, requestParams, cancellationToken)).ConfigurationJson;
		}
		return configuration;
	}
}
