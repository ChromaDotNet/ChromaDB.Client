using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models;

// The settings of a collection that can change after it is created, "new_configuration" in Chroma: the HNSW index of a single
// server, the SPANN index of Chroma Cloud. Chroma 1.0.6 and later apply them. The space cannot change.
public class ChromaCollectionConfigurationUpdate
{
	[JsonPropertyName("hnsw")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public ChromaHnswConfigurationUpdate? Hnsw { get; init; }

	[JsonPropertyName("spann")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public ChromaSpannConfigurationUpdate? Spann { get; init; }
}
