using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models.Requests;

// new_configuration of a modify: the settings of ChromaCollectionConfigurationUpdate, with the embedding function as Chroma writes it.
internal class CollectionConfigurationUpdateRequest
{
	[JsonPropertyName("hnsw")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public ChromaHnswConfigurationUpdate? Hnsw { get; init; }

	[JsonPropertyName("spann")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public ChromaSpannConfigurationUpdate? Spann { get; init; }

	[JsonPropertyName("embedding_function")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public Dictionary<string, object>? EmbeddingFunction { get; init; }
}
