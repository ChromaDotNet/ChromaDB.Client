using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models.Responses;

// Only "ids" is always there: Chroma 0.4.10 to 0.4.15 do not send "uris", and the fields that are not included can be missing.
internal class CollectionEntriesQueryResponse
{
	[JsonPropertyName("ids")]
	public required List<List<string>> Ids { get; init; }

	[JsonPropertyName("distances")]
	public List<List<float>>? Distances { get; init; }

	[JsonPropertyName("metadatas")]
	public List<List<Dictionary<string, object>>>? Metadatas { get; init; }

	[JsonPropertyName("embeddings")]
	public List<List<ReadOnlyMemory<float>>>? Embeddings { get; init; }

	[JsonPropertyName("documents")]
	public List<List<string?>>? Documents { get; init; }

	[JsonPropertyName("uris")]
	public List<List<string?>>? Uris { get; init; }

	[JsonPropertyName("data")]
	public object? Data { get; init; }
}
