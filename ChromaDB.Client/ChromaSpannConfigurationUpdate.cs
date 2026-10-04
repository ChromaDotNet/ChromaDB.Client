using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models;

// The settings of the SPANN index of Chroma Cloud that can change; the ones left null keep their value.
public class ChromaSpannConfigurationUpdate
{
	[JsonPropertyName("ef_search")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? EfSearch { get; init; }

	[JsonPropertyName("search_nprobe")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? SearchNprobe { get; init; }
}
