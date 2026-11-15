using System.IO.Compression;
using System.Net;
using ChromaDB.Client.Backup.Common;
using ChromaDB.Client.Models;

namespace ChromaDB.Client.Backup;

public static partial class ChromaBackup
{
	/// <summary>
	/// Writes the collections of a backup into the database of the client, as the copy command of the CLI writes a source into its
	/// target. Every collection that exists on the server is compared first, and nothing is written before: a collection that is
	/// the same stays as it is; one that is not follows <c>Existing</c>, and with <c>Fail</c> the restore throws before any write.
	/// A collection that does not exist is created with the schema and the metadata of the backup, and gets every record.
	/// </summary>
	/// <param name="client">The client of the server, with the tenant and the database to restore into.</param>
	/// <param name="path">The file of the backup.</param>
	/// <param name="options">The collections, what happens to the ones that exist, and the size of the writes.</param>
	/// <param name="progress">The progress, and the requests that wait to go again; null for none.</param>
	/// <param name="cancellationToken">The token that cancels the restore.</param>
	/// <returns>Each collection, with what was written.</returns>
	public static async Task<ChromaRestoreResult> RestoreAsync(ChromaClient client, string path, ChromaRestoreOptions? options = null, IProgress<ChromaBackupProgress>? progress = null, CancellationToken cancellationToken = default)
	{
		if (client is null)
		{
			throw new ArgumentNullException(nameof(client));
		}
		options ??= new();
		InRange(options.BatchSize, 1, 300, nameof(options.BatchSize));
		using var file = File.OpenRead(path);
		using var zip = new ZipArchive(file, ZipArchiveMode.Read);
		var plans = new List<(ManifestCollection Entry, Comparison Comparison, ChromaRestoreExisting? Existing)>();
		var refused = new List<string>();
		foreach (var entry in Selected(BackupFile.ReadManifest(zip), options.Collections))
		{
			var comparison = await VerifyCollection(client, zip, entry, options.BatchSize, progress, cancellationToken).ConfigureAwait(false);
			ChromaRestoreExisting? existing = null;
			if (comparison.Server is not null && !comparison.Result.Same)
			{
				existing = options.ExistingByCollection is { } byCollection && byCollection.TryGetValue(entry.Name, out var chosen) ? chosen : options.Existing;
				if (existing == ChromaRestoreExisting.Fail)
				{
					refused.Add(Describe(comparison.Result));
				}
			}
			plans.Add((entry, comparison, existing));
		}
		if (refused.Count > 0)
		{
			throw new InvalidOperationException($"Collections of the backup exist on the server and are not the same, so nothing was written: {string.Join("; ", refused)}. Restore them with Resume or Force.");
		}
		var results = new List<ChromaRestoreCollection>();
		foreach (var (entry, comparison, existing) in plans)
		{
			results.Add(await RestoreCollection(client, zip, entry, comparison, existing, options.BatchSize, progress, cancellationToken).ConfigureAwait(false));
		}
		return new(results);
	}

	private static async Task<ChromaRestoreCollection> RestoreCollection(ChromaClient client, ZipArchive zip, ManifestCollection entry, Comparison comparison, ChromaRestoreExisting? existing, int batchSize, IProgress<ChromaBackupProgress>? progress, CancellationToken cancellationToken)
	{
		var saved = comparison.Saved;
		var waiting = Waiter(progress, entry.Name);
		if (comparison.Server is not null && comparison.Result.Same)
		{
			return new(entry.Name, false, true, 0, 0, 0, 0);
		}
		// A new collection, or one with another schema or another dimension under Force: created, with every record.
		if (comparison.Server is null || existing == ChromaRestoreExisting.Force && !comparison.Result.SchemaSame)
		{
			if (comparison.Server is not null)
			{
				await Waiting.UntilDone(token => client.DeleteCollectionAsync(entry.Name, cancellationToken: token), waiting, cancellationToken).ConfigureAwait(false);
			}
			var created = await Create(client, saved, waiting, cancellationToken).ConfigureAwait(false);
			var collectionClient = client.GetCollectionClient(created);
			var (added, withoutEmbedding) = await Write(collectionClient, zip, entry, null, batchSize, progress, cancellationToken, deleteIfNothingWent: () => client.DeleteCollectionAsync(entry.Name)).ConfigureAwait(false);
			await SetMetadata(client, collectionClient, saved, waiting, cancellationToken).ConfigureAwait(false);
			await CheckCount(collectionClient, entry.Name, added, waiting, cancellationToken).ConfigureAwait(false);
			return new(entry.Name, true, false, added, 0, 0, withoutEmbedding);
		}
		var target = client.GetCollectionClient(comparison.Server);
		var countBefore = comparison.Result.OnServer;
		if (existing == ChromaRestoreExisting.Resume)
		{
			var missing = new HashSet<string>(comparison.Missing, StringComparer.Ordinal);
			var (added, withoutEmbedding) = missing.Count == 0 ? (0L, 0L) : await Write(target, zip, entry, missing, batchSize, progress, cancellationToken, deleteIfNothingWent: null).ConfigureAwait(false);
			await CheckCount(target, entry.Name, countBefore + added, waiting, cancellationToken).ConfigureAwait(false);
			return new(entry.Name, false, false, added, 0, 0, withoutEmbedding);
		}
		// Force, with the same schema: the records that only the server has go, the different ones are deleted and added again, as
		// an upsert of a record that exists with its embedding can lose a record from the index of Chroma 1.0.21 to 1.5.9 (KD-49),
		// and the missing ones are added.
		// A different record without an embedding in the backup stays as the server has it: deleted, it could not be added again.
		var onlyOnServer = await OnlyOnServer(target, comparison.BackupIds, batchSize, waiting, cancellationToken).ConfigureAwait(false);
		var different = comparison.Different.Where(id => !comparison.WithoutEmbedding.Contains(id)).ToList();
		await Delete(target, onlyOnServer, batchSize, waiting, cancellationToken).ConfigureAwait(false);
		await Delete(target, different, batchSize, waiting, cancellationToken).ConfigureAwait(false);
		var rewrite = new HashSet<string>(different.Concat(comparison.Missing), StringComparer.Ordinal);
		var (written, skipped) = rewrite.Count == 0 ? (0L, 0L) : await Write(target, zip, entry, rewrite, batchSize, progress, cancellationToken, deleteIfNothingWent: null).ConfigureAwait(false);
		await SetMetadata(client, target, saved, waiting, cancellationToken).ConfigureAwait(false);
		await CheckCount(target, entry.Name, countBefore - onlyOnServer.Count - different.Count + written, waiting, cancellationToken).ConfigureAwait(false);
		return new(entry.Name, false, false, written - different.Count, different.Count, onlyOnServer.Count, skipped + comparison.Different.Count - different.Count);
	}

	// As create_on_target of the copy command: Chroma refuses a schema together with the old settings hnsw:* in the metadata of a new
	// collection, so they come at the end, with the metadata; a creation whose answer was lost, or that someone else just made,
	// answers 409, and the collection is the one to write into.
	private static async Task<ChromaCollection> Create(ChromaClient client, SavedCollection saved, Action<string>? waiting, CancellationToken cancellationToken)
	{
		var definition = new ChromaCollectionDefinition(saved.Name)
		{
			SchemaJson = saved.SchemaJson,
			Metadata = saved.SchemaJson is not null && OldSettings(saved.Metadata) ? null : saved.Metadata,
		};
		try
		{
			return await Waiting.UntilDone(token => client.CreateCollectionAsync(definition, cancellationToken: token), waiting, cancellationToken).ConfigureAwait(false);
		}
		catch (ChromaException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
		{
			return await Waiting.UntilDone(token => client.GetCollectionAsync(saved.Name, cancellationToken: token), waiting, cancellationToken).ConfigureAwait(false);
		}
		// Chroma 1.5.9, a single server, refuses a schema with sparse vector indexes: "Sparse vector indexing is not enabled in local".
		catch (ChromaException ex) when (ex.StatusCode is not null && SparseIndexKeys(saved.SchemaJson) is { Count: > 0 } keys && ex.Message.IndexOf("sparse", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			throw new ChromaException($"The server does not take the sparse vector indexes of the keys {string.Join(", ", keys)} of the collection {saved.Name}: {ex.Message}", ex) { StatusCode = ex.StatusCode, ErrorType = ex.ErrorType };
		}
	}

	// The keys of the schema with a sparse vector index turned on.
	private static List<string> SparseIndexKeys(System.Text.Json.JsonElement? schema)
	{
		var keys = new List<string>();
		if (schema is { ValueKind: System.Text.Json.JsonValueKind.Object } json && json.TryGetProperty("keys", out var all) && all.ValueKind == System.Text.Json.JsonValueKind.Object)
		{
			foreach (var key in all.EnumerateObject())
			{
				if (key.Value.ValueKind == System.Text.Json.JsonValueKind.Object && key.Value.TryGetProperty("sparse_vector", out var sparse) && sparse.ValueKind == System.Text.Json.JsonValueKind.Object
					&& sparse.TryGetProperty("sparse_vector_index", out var index) && index.ValueKind == System.Text.Json.JsonValueKind.Object
					&& index.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == System.Text.Json.JsonValueKind.True)
				{
					keys.Add(key.Name);
				}
			}
		}
		return keys;
	}

	// The metadata of the backup on the collection, when it does not have it: the old settings left out at its creation, or the
	// metadata of a collection that Force makes the same.
	private static async Task SetMetadata(ChromaClient client, ChromaCollectionClient collectionClient, SavedCollection saved, Action<string>? waiting, CancellationToken cancellationToken)
	{
		var now = await Waiting.UntilDone(token => client.GetCollectionAsync(saved.Name, cancellationToken: token), waiting, cancellationToken).ConfigureAwait(false);
		if (!Compare.SameMetadata(saved.Metadata, now.Metadata))
		{
			var metadata = saved.Metadata ?? new Dictionary<string, object>();
			await Waiting.UntilDone(token => collectionClient.ModifyAsync(metadata: metadata, cancellationToken: token), waiting, cancellationToken).ConfigureAwait(false);
		}
	}

	private static bool OldSettings(IReadOnlyDictionary<string, object>? metadata)
		=> metadata?.Keys.Any(key => key.StartsWith("hnsw:", StringComparison.Ordinal)) == true;

	// Adds the records of the backup with the given ids, or all of them, in batches in the order of the file. A record without an
	// embedding, which the server sent as null, cannot go: Chroma 1.0.16 and later take a record only with its embedding.
	private static async Task<(long Added, long WithoutEmbedding)> Write(ChromaCollectionClient collectionClient, ZipArchive zip, ManifestCollection entry, HashSet<string>? ids, int batchSize, IProgress<ChromaBackupProgress>? progress, CancellationToken cancellationToken, Func<Task>? deleteIfNothingWent)
	{
		var waiting = Waiter(progress, entry.Name);
		long added = 0, withoutEmbedding = 0;
		var records = BackupFile.ReadRecords(zip, entry).Where(x => ids is null || ids.Contains(x.Id)).Where(x =>
		{
			if (x.Embedding is null)
			{
				withoutEmbedding++;
				return false;
			}
			return true;
		});
		try
		{
			foreach (var batch in Batches(records, batchSize))
			{
				var write = new ChromaRecords(batch.Select(x => x.Id).ToList())
				{
					Embeddings = batch.Select(x => new ReadOnlyMemory<float>(x.Embedding!)).ToList(),
					Metadatas = batch.Select(x => (IReadOnlyDictionary<string, object>?)(x.Metadata is { Count: > 0 } metadata ? metadata : null)).ToList(),
					Documents = batch.Select(x => x.Document).ToList(),
					Uris = batch.Any(x => x.Uri is not null) ? batch.Select(x => x.Uri).ToList() : null,
				};
				try
				{
					await Waiting.UntilDone(token => collectionClient.AddAsync(write, token), waiting, cancellationToken).ConfigureAwait(false);
				}
				// Chroma 1.5.9, a single server, answers 501 to sparse vectors.
				catch (ChromaException ex) when (ex.StatusCode == HttpStatusCode.NotImplemented && SparseKeys(batch) is { Count: > 0 } keys)
				{
					throw new ChromaException($"The server does not take the sparse vectors of the keys {string.Join(", ", keys)} of the collection {entry.Name}: {ex.Message}", ex) { StatusCode = ex.StatusCode, ErrorType = ex.ErrorType };
				}
				added += batch.Count;
				progress?.Report(new(entry.Name, added, entry.Records, null));
			}
		}
		// As the copy command: a collection just created that got no record goes, so that the restore can be run again as new.
		catch when (added == 0 && deleteIfNothingWent is not null)
		{
			try
			{
				await deleteIfNothingWent().ConfigureAwait(false);
			}
			catch (ChromaException)
			{
			}
			throw;
		}
		return (added, withoutEmbedding);
	}

	private static List<string> SparseKeys(IEnumerable<BackupRecord> batch)
		=> batch.SelectMany(x => x.Metadata ?? []).Where(x => x.Value is ChromaSparseVector).Select(x => x.Key).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();

	// The ids on the server that the backup does not have, read in pages of ids only before any of them goes.
	private static async Task<List<string>> OnlyOnServer(ChromaCollectionClient collectionClient, HashSet<string> backupIds, int batchSize, Action<string>? waiting, CancellationToken cancellationToken)
	{
		var only = new List<string>();
		for (var offset = 0; ; offset += batchSize)
		{
			var pageOffset = offset;
			var page = await Waiting.UntilDone(token => collectionClient.GetAsync(limit: batchSize, offset: pageOffset, include: (ChromaGetInclude)0, cancellationToken: token), waiting, cancellationToken).ConfigureAwait(false);
			only.AddRange(page.Select(x => x.Id).Where(id => !backupIds.Contains(id)));
			if (page.Count < batchSize)
			{
				return only;
			}
		}
	}

	private static async Task Delete(ChromaCollectionClient collectionClient, IReadOnlyList<string> ids, int batchSize, Action<string>? waiting, CancellationToken cancellationToken)
	{
		for (var i = 0; i < ids.Count; i += batchSize)
		{
			var batch = ids.Skip(i).Take(batchSize).ToList();
			await Waiting.UntilDone(token => collectionClient.DeleteAsync(batch, cancellationToken: token), waiting, cancellationToken).ConfigureAwait(false);
		}
	}

	// As the copy command at its end: the count of the collection must be the one the restore wrote, or someone else wrote meanwhile.
	private static async Task CheckCount(ChromaCollectionClient collectionClient, string name, long expected, Action<string>? waiting, CancellationToken cancellationToken)
	{
		var count = await Waiting.UntilDone(token => collectionClient.CountAsync(token), waiting, cancellationToken).ConfigureAwait(false);
		if (count != expected)
		{
			throw new ChromaException($"The collection {name} has {count} records after the restore, not {expected}: the server took other writes meanwhile.");
		}
	}

	private static string Describe(ChromaVerifyCollection collection)
		=> $"{collection.Name} ({collection.SameRecords} records the same, {collection.Missing} missing, {collection.Different} different, {collection.OnlyOnServer} only on the server"
			+ (collection.SchemaSame ? "" : ", another schema or dimension")
			+ (collection.MetadataSame ? "" : ", other metadata")
			+ (collection.FirstDifference is { } first ? $", first difference {first}" : "")
			+ ")";
}
