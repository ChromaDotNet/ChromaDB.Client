using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using ChromaDB.Client.Backup.Common;
using ChromaDB.Client.Models;

namespace ChromaDB.Client.Backup;

/// <summary>
/// Backup and restore of the collections of a database through the API of Chroma, into a zip file and back: their metadata,
/// configuration and schema as the server sends them, and their records with ids, embeddings, metadata, documents and URIs. A
/// backup reads the collections while the server runs: if the server takes writes meanwhile, the file can hold records of
/// different moments, and the counts of the result tell it. A stopped server, or one that takes no writes, gives a consistent copy.
/// </summary>
public static partial class ChromaBackup
{
	private static readonly ChromaGetInclude Everything = ChromaGetInclude.Embeddings | ChromaGetInclude.Metadatas | ChromaGetInclude.Documents | ChromaGetInclude.Uris;

	/// <summary>
	/// Saves collections of the database of the client into a new zip file. The file is written as <c>path.partial</c> and renamed at
	/// the end, so that a backup that fails leaves no file at the path.
	/// </summary>
	/// <param name="client">The client of the server, with the tenant and the database of the collections.</param>
	/// <param name="path">The file of the backup, which must not exist.</param>
	/// <param name="options">The collections and the size of the gets; null for every collection, 100 records a get.</param>
	/// <param name="progress">The progress, and the requests that wait to go again; null for none.</param>
	/// <param name="cancellationToken">The token that cancels the backup.</param>
	/// <returns>The collections saved, with their counts.</returns>
	public static async Task<ChromaBackupResult> BackupAsync(ChromaClient client, string path, ChromaBackupOptions? options = null, IProgress<ChromaBackupProgress>? progress = null, CancellationToken cancellationToken = default)
	{
		if (client is null)
		{
			throw new ArgumentNullException(nameof(client));
		}
		options ??= new();
		InRange(options.BatchSize, 1, 300, nameof(options.BatchSize));
		InRange(options.Concurrency, 1, 8, nameof(options.Concurrency));
		path = Path.GetFullPath(path);
		if (File.Exists(path))
		{
			throw new IOException($"The file {path} exists: a backup does not overwrite one.");
		}
		var started = DateTimeOffset.UtcNow;
		var waiting = Waiter(progress, "");
		var version = await Waiting.UntilDone(token => client.GetVersionAsync(token), waiting, cancellationToken).ConfigureAwait(false);
		var preFlight = await PreFlight(client, waiting, cancellationToken).ConfigureAwait(false);
		var collections = await Collections(client, options.Collections, waiting, cancellationToken).ConfigureAwait(false);
		var manifest = new Manifest
		{
			CreatedBy = $"ChromaDotNet.Client.Backup {LibraryVersion}",
			StartedAt = started.ToString("o"),
			ServerVersion = version,
			ServerName = preFlight?.Server?.Name,
			ServerOwnVersion = preFlight?.Server?.Version,
			MaxBatchSize = preFlight?.MaxBatchSize,
			Tenant = collections.Select(x => x.Tenant).FirstOrDefault(x => x is not null),
			Database = collections.Select(x => x.Database).FirstOrDefault(x => x is not null),
		};
		var partial = path + ".partial";
		var results = new List<ChromaBackupCollection>();
		try
		{
			using (var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
			using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
			{
				for (var i = 0; i < collections.Count; i++)
				{
					var folder = BackupFile.Folder(i);
					BackupFile.WriteCollection(zip, folder, collections[i]);
					var (result, sha256) = await SaveRecords(client, zip, folder, collections[i], options, progress, cancellationToken).ConfigureAwait(false);
					manifest.Collections.Add((folder, sha256, result));
					results.Add(result);
				}
				manifest.FinishedAt = DateTimeOffset.UtcNow.ToString("o");
				BackupFile.WriteManifest(zip, manifest);
			}
			File.Move(partial, path);
		}
		catch
		{
			try
			{
				File.Delete(partial);
			}
			catch (IOException)
			{
			}
			throw;
		}
		return new(path, results);
	}

	/// <summary>
	/// Compares the collections of a backup with the ones with their names on the server of the client, as the copy command of the CLI
	/// compares a source with its target. It writes nothing.
	/// </summary>
	/// <param name="client">The client of the server, with the tenant and the database of the collections.</param>
	/// <param name="path">The file of the backup.</param>
	/// <param name="collections">The names of the collections of the backup to compare; null for all of them.</param>
	/// <param name="progress">The progress, and the requests that wait to go again; null for none.</param>
	/// <param name="cancellationToken">The token that cancels the comparison.</param>
	/// <returns>Each collection, with what is the same and what is not.</returns>
	public static async Task<ChromaVerifyResult> VerifyAsync(ChromaClient client, string path, IReadOnlyList<string>? collections = null, IProgress<ChromaBackupProgress>? progress = null, CancellationToken cancellationToken = default)
	{
		if (client is null)
		{
			throw new ArgumentNullException(nameof(client));
		}
		using var file = File.OpenRead(path);
		using var zip = new ZipArchive(file, ZipArchiveMode.Read);
		var results = new List<ChromaVerifyCollection>();
		foreach (var entry in Selected(BackupFile.ReadManifest(zip), collections))
		{
			results.Add((await VerifyCollection(client, zip, entry, 100, progress, cancellationToken).ConfigureAwait(false)).Result);
		}
		return new(results);
	}

	internal static string LibraryVersion
		=> typeof(ChromaBackup).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
			?? typeof(ChromaBackup).Assembly.GetName().Version?.ToString()
			?? "";

	private static async Task<(ChromaBackupCollection, string)> SaveRecords(ChromaClient client, ZipArchive zip, string folder, ChromaCollection collection, ChromaBackupOptions options, IProgress<ChromaBackupProgress>? progress, CancellationToken cancellationToken)
	{
		var collectionClient = client.GetCollectionClient(collection);
		var waiting = Waiter(progress, collection.Name);
		var countAtStart = await Waiting.UntilDone(token => collectionClient.CountAsync(token), waiting, cancellationToken).ConfigureAwait(false);
		using var hash = new Sha256();
		var ids = new HashSet<string>(StringComparer.Ordinal);
		long records = 0, duplicates = 0, nullEmbeddings = 0;
		var nullEmbeddingIds = new List<string>();
		using var line = new MemoryStream();
		using (var stream = zip.CreateEntry(BackupFile.RecordsEntry(folder), CompressionLevel.Optimal).Open())
		using (var pages = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
		{
			var pending = new Queue<Task<IReadOnlyList<ChromaCollectionEntry>>>();
			var offset = 0;
			var last = false;
			// The gets go ahead of the writes, up to Concurrency of them, and the records go in the file in the order of their pages.
			void Ask()
			{
				while (!last && pending.Count < options.Concurrency)
				{
					var pageOffset = offset;
					pending.Enqueue(Waiting.UntilDone(token => collectionClient.GetAsync(limit: options.BatchSize, offset: pageOffset, include: Everything, cancellationToken: token), waiting, pages.Token));
					offset += options.BatchSize;
				}
			}
			try
			{
				Ask();
				while (pending.Count > 0)
				{
					var page = await pending.Dequeue().ConfigureAwait(false);
					foreach (var entry in page)
					{
						// An id of a page already read: the pages moved while the server took writes. The file keeps the first.
						if (!ids.Add(entry.Id))
						{
							duplicates++;
							continue;
						}
						line.SetLength(0);
						using (var writer = new Utf8JsonWriter(line, RecordJson.WriterOptions))
						{
							if (!RecordJson.Write(writer, entry))
							{
								nullEmbeddings++;
								if (nullEmbeddingIds.Count < 100)
								{
									nullEmbeddingIds.Add(entry.Id);
								}
							}
						}
						line.WriteByte((byte)'\n');
						hash.Append(line.GetBuffer(), 0, (int)line.Length);
						await stream.WriteAsync(line.GetBuffer(), 0, (int)line.Length, cancellationToken).ConfigureAwait(false);
						records++;
					}
					progress?.Report(new(collection.Name, records, countAtStart, null));
					// Until a page that is not full: the records added during the backup come in too.
					if (page.Count < options.BatchSize)
					{
						last = true;
						break;
					}
					Ask();
				}
			}
			finally
			{
				// The gets asked for beyond the last page, or after an error: they stop, and their results go.
				pages.Cancel();
				while (pending.Count > 0)
				{
					try
					{
						await pending.Dequeue().ConfigureAwait(false);
					}
					catch (Exception)
					{
					}
				}
			}
		}
		var countAtEnd = await Waiting.UntilDone(token => collectionClient.CountAsync(token), waiting, cancellationToken).ConfigureAwait(false);
		return (new(collection.Name, records, countAtStart, countAtEnd, duplicates, nullEmbeddings, nullEmbeddingIds), hash.Hex());
	}

	// The comparison of a collection of the backup with the server, and what a restore needs of it: the ids of the backup, and
	// the ids of its records that are missing or different on the server.
	internal static async Task<Comparison> VerifyCollection(ChromaClient client, ZipArchive zip, ManifestCollection entry, int batchSize, IProgress<ChromaBackupProgress>? progress, CancellationToken cancellationToken)
	{
		var saved = BackupFile.ReadCollection(zip, entry);
		var waiting = Waiter(progress, entry.Name);
		var server = await TryGetCollection(client, entry.Name, waiting, cancellationToken).ConfigureAwait(false);
		var comparison = new Comparison(saved, server);
		if (server is null)
		{
			comparison.Result = new(entry.Name, false, entry.Records, 0, 0, entry.Records, 0, null, false, false);
			return comparison;
		}
		var collectionClient = client.GetCollectionClient(server);
		var onServer = await Waiting.UntilDone(token => collectionClient.CountAsync(token), waiting, cancellationToken).ConfigureAwait(false);
		long same = 0, inBackup = 0;
		string? first = null;
		foreach (var batch in Batches(BackupFile.ReadRecords(zip, entry), batchSize))
		{
			var batchIds = batch.Select(x => x.Id).ToList();
			var found = (await Waiting.UntilDone(token => collectionClient.GetAsync(ids: batchIds, include: Everything, cancellationToken: token), waiting, cancellationToken).ConfigureAwait(false))
				.ToDictionary(x => x.Id, StringComparer.Ordinal);
			foreach (var record in batch)
			{
				comparison.BackupIds.Add(record.Id);
				if (!found.TryGetValue(record.Id, out var onServerRecord))
				{
					comparison.Missing.Add(record.Id);
				}
				else if (Compare.Difference(record, onServerRecord) is { } what)
				{
					comparison.Different.Add(record.Id);
					first ??= $"{record.Id}: {what}";
				}
				else
				{
					same++;
				}
			}
			inBackup += batch.Count;
			progress?.Report(new(entry.Name, inBackup, entry.Records, null));
		}
		var schemaSame = Compare.SameSchema(saved.SchemaJson, server.SchemaJson) && !Compare.OtherDimensions(saved.Dimension, server.Dimension);
		comparison.Result = new(entry.Name, true, inBackup, onServer, same, comparison.Missing.Count, comparison.Different.Count, first, schemaSame, Compare.SameMetadata(saved.Metadata, server.Metadata));
		return comparison;
	}

	internal sealed class Comparison(SavedCollection saved, ChromaCollection? server)
	{
		public SavedCollection Saved { get; } = saved;
		public ChromaCollection? Server { get; } = server;
		public HashSet<string> BackupIds { get; } = new(StringComparer.Ordinal);
		public List<string> Missing { get; } = [];
		public List<string> Different { get; } = [];
		public ChromaVerifyCollection Result { get; set; } = null!;
	}

	private static async Task<ChromaCollection?> TryGetCollection(ChromaClient client, string name, Action<string>? waiting, CancellationToken cancellationToken)
	{
		try
		{
			return await Waiting.UntilDone(token => client.GetCollectionAsync(name, cancellationToken: token), waiting, cancellationToken).ConfigureAwait(false);
		}
		catch (ChromaException ex) when (IsMissing(ex))
		{
			return null;
		}
	}

	// A missing collection, as the client tells it (ChromaException.IsMissingCollection): 404 from Chroma 1.x, and "does not exist".
	private static bool IsMissing(ChromaException ex)
		=> ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest or HttpStatusCode.InternalServerError && ex.Message.Contains("does not exist")
			|| ex.StatusCode == HttpStatusCode.NotFound && ex.ErrorType == "NotFoundError";

	private static async Task<ChromaPreFlightChecks?> PreFlight(ChromaClient client, Action<string>? waiting, CancellationToken cancellationToken)
	{
		try
		{
			return await Waiting.UntilDone(token => client.GetPreFlightChecksAsync(token), waiting, cancellationToken).ConfigureAwait(false);
		}
		catch (ChromaException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
		{
			return null;
		}
	}

	private static async Task<IReadOnlyList<ChromaCollection>> Collections(ChromaClient client, IReadOnlyList<string>? names, Action<string>? waiting, CancellationToken cancellationToken)
	{
		var collections = new List<ChromaCollection>();
		if (names is not null)
		{
			foreach (var name in names.Distinct(StringComparer.Ordinal))
			{
				collections.Add(await Waiting.UntilDone(token => client.GetCollectionAsync(name, cancellationToken: token), waiting, cancellationToken).ConfigureAwait(false));
			}
			return collections;
		}
		const int Page = 100;
		for (var offset = 0; ; offset += Page)
		{
			var pageOffset = offset;
			var page = await Waiting.UntilDone(token => client.ListCollectionsAsync(Page, pageOffset, cancellationToken: token), waiting, cancellationToken).ConfigureAwait(false);
			collections.AddRange(page);
			if (page.Count < Page)
			{
				return collections;
			}
		}
	}

	private static IEnumerable<ManifestCollection> Selected(IReadOnlyList<ManifestCollection> entries, IReadOnlyList<string>? names)
	{
		if (names is null)
		{
			return entries;
		}
		var missing = names.Where(name => entries.All(x => x.Name != name)).ToList();
		if (missing.Count > 0)
		{
			throw new ArgumentException($"The backup has no collection {string.Join(", ", missing)}.", nameof(names));
		}
		return entries.Where(x => names.Contains(x.Name));
	}

	private static IEnumerable<List<BackupRecord>> Batches(IEnumerable<BackupRecord> records, int size)
	{
		var batch = new List<BackupRecord>(size);
		foreach (var record in records)
		{
			batch.Add(record);
			if (batch.Count == size)
			{
				yield return batch;
				batch = new List<BackupRecord>(size);
			}
		}
		if (batch.Count > 0)
		{
			yield return batch;
		}
	}

	private static Action<string>? Waiter(IProgress<ChromaBackupProgress>? progress, string collection)
		=> progress is null ? null : message => progress.Report(new(collection, 0, null, message));

	private static void InRange(int value, int min, int max, string name)
	{
		if (value < min || value > max)
		{
			throw new ArgumentOutOfRangeException(name, value, $"{name} goes from {min} to {max}.");
		}
	}
}
