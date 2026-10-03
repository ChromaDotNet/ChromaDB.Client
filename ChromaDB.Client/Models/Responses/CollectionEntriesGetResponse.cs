using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models.Responses;

// Only "ids" is always there: Chroma 0.4.10 to 0.4.15 do not send "uris", and the fields that are not included can be missing.
internal class CollectionEntriesGetResponse
{
	[JsonPropertyName("ids")]
	public required List<string> Ids { get; init; }

	[JsonPropertyName("embeddings")]
	public List<ReadOnlyMemory<float>?>? Embeddings { get; init; }

	[JsonPropertyName("metadatas")]
	public List<Dictionary<string, object>?>? Metadatas { get; init; }

	[JsonPropertyName("documents")]
	public List<string?>? Documents { get; init; }

	[JsonPropertyName("uris")]
	public List<string?>? Uris { get; init; }

	[JsonPropertyName("data")]
	public dynamic? Data { get; init; }
}
