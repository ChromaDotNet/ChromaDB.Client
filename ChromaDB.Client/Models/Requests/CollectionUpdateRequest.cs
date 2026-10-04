using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models.Requests;

internal class CollectionUpdateRequest
{
	[JsonPropertyName("ids")]
	public required List<string> Ids { get; init; }

	[JsonPropertyName("embeddings")]
	public Common.ChromaEmbeddings? Embeddings { get; init; }

	[JsonPropertyName("metadatas")]
	public List<Dictionary<string, object>>? Metadatas { get; init; }

	[JsonPropertyName("documents")]
	public List<string>? Documents { get; init; }

	// Left out when null, so that the requests without URIs stay the same for the servers that do not know them.
	[JsonPropertyName("uris")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public List<string?>? Uris { get; init; }
}
