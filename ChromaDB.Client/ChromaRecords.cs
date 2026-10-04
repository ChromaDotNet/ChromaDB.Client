namespace ChromaDB.Client.Models;

/// <summary>
/// The records to add, update or upsert. A new field of Chroma becomes a new property here, without changing the
/// methods.
/// </summary>
public class ChromaRecords
{
	/// <summary>
	/// The ids of the records.
	/// </summary>
	public List<string> Ids { get; }
	/// <summary>
	/// The embeddings of the records, in the order of the ids.
	/// </summary>
	public List<ReadOnlyMemory<float>>? Embeddings { get; init; }
	/// <summary>
	/// The metadatas of the records, in the order of the ids.
	/// </summary>
	public List<Dictionary<string, object>>? Metadatas { get; init; }
	/// <summary>
	/// The documents of the records, in the order of the ids.
	/// </summary>
	public List<string>? Documents { get; init; }
	/// <summary>
	/// The URIs of the records, in the order of the ids. A record without a URI has null at its position.
	/// </summary>
	public List<string?>? Uris { get; init; }

	/// <summary>
	/// Creates the records with the ids.
	/// </summary>
	public ChromaRecords(List<string> ids)
	{
		Ids = ids;
	}
}
