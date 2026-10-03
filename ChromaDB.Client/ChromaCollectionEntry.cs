namespace ChromaDB.Client.Models;

public class ChromaCollectionEntry
{
	public string Id { get; }
	public ReadOnlyMemory<float>? Embeddings { get; init; }
	public Dictionary<string, object>? Metadata { get; init; }
	public string? Document { get; init; }
	// Null when the get did not include the URIs.
	public string? Uri { get; init; }
	[Obsolete("A record has one URI: use Uri.")]
	public List<string?>? Uris { get; init; }
	public dynamic? Data { get; init; }

	public ChromaCollectionEntry(string id)
	{
		Id = id;
	}
}
