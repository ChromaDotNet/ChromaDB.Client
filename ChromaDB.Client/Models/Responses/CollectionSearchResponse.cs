using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models.Responses;

// One list for each search; a field that was not selected is null for the whole search.
internal class CollectionSearchResponse
{
	[JsonPropertyName("ids")]
	public required List<List<string>> Ids { get; init; }

	[JsonPropertyName("documents")]
	public List<List<string?>?>? Documents { get; init; }

	[JsonPropertyName("embeddings")]
	public List<List<ReadOnlyMemory<float>?>?>? Embeddings { get; init; }

	[JsonPropertyName("metadatas")]
	public List<List<Dictionary<string, object>?>?>? Metadatas { get; init; }

	[JsonPropertyName("scores")]
	public List<List<float?>?>? Scores { get; init; }
}
