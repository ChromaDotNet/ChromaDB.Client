namespace ChromaDB.Client.Backup;

/// <summary>
/// What a backup saves, and how it reads the server.
/// </summary>
public sealed class ChromaBackupOptions
{
	/// <summary>
	/// The names of the collections to save; null for every collection of the database of the client.
	/// </summary>
	public IReadOnlyList<string>? Collections { get; init; }

	/// <summary>
	/// The records of one get, from 1 to 300: 300 is the most that Chroma Cloud answers. 100 by default, as the copy command of the CLI.
	/// </summary>
	public int BatchSize { get; init; } = 100;

	/// <summary>
	/// The gets that go at the same time, from 1 to 8; 5 by default. The records go in the file in the order of the collection.
	/// </summary>
	public int Concurrency { get; init; } = 5;
}

/// <summary>
/// The progress of a backup, a restore or a verification: the collection, the records done, and a message while a request waits to
/// go again.
/// </summary>
public sealed class ChromaBackupProgress
{
	internal ChromaBackupProgress(string collection, long records, long? total, string? message)
	{
		Collection = collection;
		Records = records;
		Total = total;
		Message = message;
	}

	/// <summary>
	/// The name of the collection.
	/// </summary>
	public string Collection { get; }

	/// <summary>
	/// The records done so far in the collection.
	/// </summary>
	public long Records { get; }

	/// <summary>
	/// The records of the collection, as counted at the start; null when not known.
	/// </summary>
	public long? Total { get; }

	/// <summary>
	/// Why a request waits before it goes again, like a 429 of the server; null otherwise.
	/// </summary>
	public string? Message { get; }
}

/// <summary>
/// The backup written.
/// </summary>
public sealed class ChromaBackupResult
{
	internal ChromaBackupResult(string path, IReadOnlyList<ChromaBackupCollection> collections)
	{
		Path = path;
		Collections = collections;
	}

	/// <summary>
	/// The file of the backup.
	/// </summary>
	public string Path { get; }

	/// <summary>
	/// The collections saved.
	/// </summary>
	public IReadOnlyList<ChromaBackupCollection> Collections { get; }

	/// <summary>
	/// Whether the server took writes during the backup, in any collection: the file can then hold records of different moments.
	/// </summary>
	public bool ChangedDuringBackup => Collections.Any(x => x.ChangedDuringBackup);
}

/// <summary>
/// A collection saved by a backup.
/// </summary>
public sealed class ChromaBackupCollection
{
	internal ChromaBackupCollection(string name, long records, long countAtStart, long countAtEnd, long duplicateIds, long nullEmbeddings, IReadOnlyList<string> nullEmbeddingIds)
	{
		Name = name;
		Records = records;
		CountAtStart = countAtStart;
		CountAtEnd = countAtEnd;
		DuplicateIds = duplicateIds;
		NullEmbeddings = nullEmbeddings;
		NullEmbeddingIds = nullEmbeddingIds;
	}

	/// <summary>
	/// The name of the collection.
	/// </summary>
	public string Name { get; }

	/// <summary>
	/// The records written in the file.
	/// </summary>
	public long Records { get; }

	/// <summary>
	/// The count of the collection before its first get.
	/// </summary>
	public long CountAtStart { get; }

	/// <summary>
	/// The count of the collection after its last get.
	/// </summary>
	public long CountAtEnd { get; }

	/// <summary>
	/// The ids that a get gave again, from a page already read: the order of the pages changed while the server took writes. The file
	/// holds each id once.
	/// </summary>
	public long DuplicateIds { get; }

	/// <summary>
	/// The records whose embedding the server sent as <c>null</c>, as Chroma 1.x does for a float it cannot write: the file has no
	/// embedding for them, and a restore cannot write them.
	/// </summary>
	public long NullEmbeddings { get; }

	/// <summary>
	/// The ids of the first 100 records whose embedding the server sent as <c>null</c>.
	/// </summary>
	public IReadOnlyList<string> NullEmbeddingIds { get; }

	/// <summary>
	/// Whether the counts tell that the server took writes during the backup of the collection: the count changed, a get gave an id
	/// again, or the file holds another number of records than the count at the end.
	/// </summary>
	public bool ChangedDuringBackup => CountAtStart != CountAtEnd || DuplicateIds > 0 || Records != CountAtEnd;
}

/// <summary>
/// The comparison of a backup with the server.
/// </summary>
public sealed class ChromaVerifyResult
{
	internal ChromaVerifyResult(IReadOnlyList<ChromaVerifyCollection> collections)
	{
		Collections = collections;
	}

	/// <summary>
	/// The collections of the backup, compared with the ones of the server.
	/// </summary>
	public IReadOnlyList<ChromaVerifyCollection> Collections { get; }

	/// <summary>
	/// Whether every collection of the backup is the same on the server.
	/// </summary>
	public bool Same => Collections.All(x => x.Same);
}

/// <summary>
/// A collection of a backup compared with the one of the server with its name, as the copy command of the CLI compares them: the
/// documents and the URIs the same, the embeddings within the tolerances of <c>np.allclose</c> (relative 1e-5, absolute 1e-8), with
/// which the tests of Chroma take an embedding read back, and the metadata exactly the same.
/// </summary>
public sealed class ChromaVerifyCollection
{
	internal ChromaVerifyCollection(string name, bool exists, long inBackup, long onServer, long same, long missing, long different, string? firstDifference, bool schemaSame, bool metadataSame)
	{
		Name = name;
		Exists = exists;
		InBackup = inBackup;
		OnServer = onServer;
		SameRecords = same;
		Missing = missing;
		Different = different;
		FirstDifference = firstDifference;
		SchemaSame = schemaSame;
		MetadataSame = metadataSame;
	}

	/// <summary>
	/// The name of the collection.
	/// </summary>
	public string Name { get; }

	/// <summary>
	/// Whether the server has a collection with the name.
	/// </summary>
	public bool Exists { get; }

	/// <summary>
	/// The records of the collection in the backup.
	/// </summary>
	public long InBackup { get; }

	/// <summary>
	/// The count of the collection on the server.
	/// </summary>
	public long OnServer { get; }

	/// <summary>
	/// The records of the backup that are the same on the server.
	/// </summary>
	public long SameRecords { get; }

	/// <summary>
	/// The records of the backup that the server does not have.
	/// </summary>
	public long Missing { get; }

	/// <summary>
	/// The records of the backup that the server has with another document, embedding, metadata or URI.
	/// </summary>
	public long Different { get; }

	/// <summary>
	/// The records that only the server has.
	/// </summary>
	public long OnlyOnServer => Math.Max(0, OnServer - SameRecords - Different);

	/// <summary>
	/// The id of the first different record and what differs, like <c>"a: embedding"</c>; null when none differs.
	/// </summary>
	public string? FirstDifference { get; }

	/// <summary>
	/// Whether the schema and the dimension of the collection are the same. A key that only the records brought in, with the settings
	/// of the defaults, does not count, nor the settings of the vector index when one side has HNSW and the other SPANN: a single
	/// Chroma server, like Chroma 1.5.9, creates with HNSW a collection whose schema has SPANN.
	/// </summary>
	public bool SchemaSame { get; }

	/// <summary>
	/// Whether the metadata of the collection is the same.
	/// </summary>
	public bool MetadataSame { get; }

	/// <summary>
	/// Whether the collection is the same on the server: every record, none more, the schema and the metadata.
	/// </summary>
	public bool Same => Exists && Missing == 0 && Different == 0 && OnlyOnServer == 0 && SchemaSame && MetadataSame;
}

/// <summary>
/// A backup that cannot be read: not a backup of ChromaDotNet, a later version of the format, or a file whose records do not match
/// the digest of its manifest.
/// </summary>
public sealed class ChromaBackupFormatException : Exception
{
	/// <summary>
	/// The exception with its message.
	/// </summary>
	/// <param name="message">What is wrong with the file.</param>
	public ChromaBackupFormatException(string message) : base(message) { }
}
