namespace ChromaDB.Client.Models;

public class ChromaCollectionQueryEntry
{
	public string Id { get; }
	// Null when the query did not include ChromaQueryInclude.Distances.
	public float? Distance { get; init; }
	public Dictionary<string, object>? Metadata { get; init; }
	public ReadOnlyMemory<float>? Embeddings { get; init; }
	public string? Document { get; init; }
	// Null when the query did not include the URIs.
	public string? Uri { get; init; }
	[Obsolete("A record has one URI: use Uri.")]
	public List<string?>? Uris { get; init; }
	public dynamic? Data { get; init; }

	public ChromaCollectionQueryEntry(string id)
	{
		Id = id;
	}
}
