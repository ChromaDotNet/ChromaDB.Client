using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models;

/// <summary>
/// The settings of a collection that can change after it is created, <c>new_configuration</c> in Chroma: the HNSW index
/// of a single server, the SPANN index of Chroma Cloud, and the embedding function the collection declares. Chroma 1.0.6 and later
/// apply them. The space cannot change.
/// </summary>
public class ChromaCollectionConfigurationUpdate
{
	/// <summary>
	/// The settings of the HNSW index, for a collection on a single Chroma server.
	/// </summary>
	[JsonPropertyName("hnsw")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public ChromaHnswConfigurationUpdate? Hnsw { get; init; }

	/// <summary>
	/// The settings of the SPANN index, for a collection on Chroma Cloud.
	/// </summary>
	[JsonPropertyName("spann")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public ChromaSpannConfigurationUpdate? Spann { get; init; }

	/// <summary>
	/// The embedding function the collection declares, so that the clients of Chroma that know it compute the embeddings. The client
	/// only declares it.
	/// </summary>
	[JsonIgnore]
	public ChromaEmbeddingFunctionReference? EmbeddingFunction { get; init; }
}
