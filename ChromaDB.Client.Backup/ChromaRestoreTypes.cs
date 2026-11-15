namespace ChromaDB.Client.Backup;

/// <summary>
/// What a restore writes, and what it does with a collection that already exists on the server.
/// </summary>
public sealed class ChromaRestoreOptions
{
	/// <summary>
	/// The names of the collections of the backup to restore; null for all of them.
	/// </summary>
	public IReadOnlyList<string>? Collections { get; init; }

	/// <summary>
	/// What happens to a collection of the backup that exists on the server and is not the same: <c>Fail</c> by default, which writes
	/// nothing to any collection.
	/// </summary>
	public ChromaRestoreExisting Existing { get; init; } = ChromaRestoreExisting.Fail;

	/// <summary>
	/// What happens to each named collection that exists, in place of <c>Existing</c>.
	/// </summary>
	public IReadOnlyDictionary<string, ChromaRestoreExisting>? ExistingByCollection { get; init; }

	/// <summary>
	/// The records of one write, from 1 to 300; 100 by default.
	/// </summary>
	public int BatchSize { get; init; } = 100;
}

/// <summary>
/// What a restore does with a collection of the backup that exists on the server and is not the same, as the copy command of the CLI.
/// </summary>
public enum ChromaRestoreExisting
{
	/// <summary>
	/// The restore writes nothing, to any collection, and throws.
	/// </summary>
	Fail,

	/// <summary>
	/// Adds the records that the collection does not have. The records that are different, the records that only the server has, the
	/// metadata and the schema of the collection stay as they are.
	/// </summary>
	Resume,

	/// <summary>
	/// Makes the collection the same as the backup: with another schema or another dimension it is deleted and created again; otherwise
	/// the records that only the server has are deleted, the different ones deleted and added again, the missing ones added, and the
	/// metadata of the collection set. A different record is deleted and added, not upserted: on Chroma 1.0.21 to 1.5.9 an upsert of a
	/// record that exists, with its embedding, can lose a record from the index (KD-49 in docs/COMPATIBILITY.md).
	/// </summary>
	Force,
}

/// <summary>
/// What a restore wrote.
/// </summary>
public sealed class ChromaRestoreResult
{
	internal ChromaRestoreResult(IReadOnlyList<ChromaRestoreCollection> collections)
	{
		Collections = collections;
	}

	/// <summary>
	/// The collections of the backup restored.
	/// </summary>
	public IReadOnlyList<ChromaRestoreCollection> Collections { get; }
}

/// <summary>
/// A collection restored.
/// </summary>
public sealed class ChromaRestoreCollection
{
	internal ChromaRestoreCollection(string name, bool created, bool alreadySame, long added, long rewritten, long deleted, long withoutEmbedding)
	{
		Name = name;
		Created = created;
		AlreadySame = alreadySame;
		Added = added;
		Rewritten = rewritten;
		Deleted = deleted;
		WithoutEmbedding = withoutEmbedding;
	}

	/// <summary>
	/// The name of the collection.
	/// </summary>
	public string Name { get; }

	/// <summary>
	/// Whether the restore created the collection, new or again with <c>Force</c>.
	/// </summary>
	public bool Created { get; }

	/// <summary>
	/// Whether the collection was already the same on the server, and the restore wrote nothing to it.
	/// </summary>
	public bool AlreadySame { get; }

	/// <summary>
	/// The records added that the collection did not have.
	/// </summary>
	public long Added { get; }

	/// <summary>
	/// The records deleted and added again, as they are in the backup.
	/// </summary>
	public long Rewritten { get; }

	/// <summary>
	/// The records that only the server had, deleted.
	/// </summary>
	public long Deleted { get; }

	/// <summary>
	/// The records of the backup without an embedding, which the server sent as <c>null</c>: not written, as Chroma 1.0.16 and later
	/// take a record only with its embedding.
	/// </summary>
	public long WithoutEmbedding { get; }
}
