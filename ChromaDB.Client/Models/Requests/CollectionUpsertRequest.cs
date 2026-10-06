using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models.Requests;

internal class CollectionUpsertRequest
{
	[JsonPropertyName("ids")]
	public required IReadOnlyList<string> Ids { get; init; }

	[JsonPropertyName("embeddings")]
	public Common.ChromaEmbeddings? Embeddings { get; init; }

	[JsonPropertyName("metadatas")]
	public IReadOnlyList<IReadOnlyDictionary<string, object>?>? Metadatas { get; init; }

	[JsonPropertyName("documents")]
	public IReadOnlyList<string?>? Documents { get; init; }

	// Left out when null, so that the requests without URIs stay the same for the servers that do not know them.
	[JsonPropertyName("uris")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<string?>? Uris { get; init; }
}
