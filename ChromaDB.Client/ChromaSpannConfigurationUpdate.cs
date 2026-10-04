using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models;

/// <summary>
/// The settings of the SPANN index of Chroma Cloud that can change; the ones left null keep their value.
/// </summary>
public class ChromaSpannConfigurationUpdate
{
	/// <summary>
	/// The <c>ef_search</c> setting of the SPANN index.
	/// </summary>
	[JsonPropertyName("ef_search")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? EfSearch { get; init; }

	/// <summary>
	/// The <c>search_nprobe</c> setting of the SPANN index.
	/// </summary>
	[JsonPropertyName("search_nprobe")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? SearchNprobe { get; init; }
}
