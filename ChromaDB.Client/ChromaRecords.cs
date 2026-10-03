namespace ChromaDB.Client.Models;

// The records to add, update or upsert. A new field of Chroma becomes a new property here, without changing the methods.
public class ChromaRecords
{
	public List<string> Ids { get; }
	public List<ReadOnlyMemory<float>>? Embeddings { get; init; }
	public List<Dictionary<string, object>>? Metadatas { get; init; }
	public List<string>? Documents { get; init; }
	// A record without a URI has null at its position.
	public List<string?>? Uris { get; init; }

	public ChromaRecords(List<string> ids)
	{
		Ids = ids;
	}
}
