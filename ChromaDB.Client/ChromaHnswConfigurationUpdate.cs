using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models;

/// <summary>
/// The settings of the HNSW index that can change; the ones left null keep their value.
/// </summary>
public class ChromaHnswConfigurationUpdate
{
	/// <summary>
	/// The <c>ef_search</c> setting of the HNSW index.
	/// </summary>
	[JsonPropertyName("ef_search")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? EfSearch { get; init; }

	/// <summary>
	/// The <c>max_neighbors</c> setting of the HNSW index, at least 2: <c>ModifyConfigurationAsync</c> throws an
	/// <c>ArgumentException</c> for less, as Chroma crashes on the next write with 0 and misses the nearest records with 1.
	/// </summary>
	[JsonPropertyName("max_neighbors")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? MaxNeighbors { get; init; }

	/// <summary>
	/// The <c>num_threads</c> setting of the HNSW index.
	/// </summary>
	[JsonPropertyName("num_threads")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? NumThreads { get; init; }

	/// <summary>
	/// The <c>batch_size</c> setting of the HNSW index.
	/// </summary>
	[JsonPropertyName("batch_size")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? BatchSize { get; init; }

	/// <summary>
	/// The <c>sync_threshold</c> setting of the HNSW index.
	/// </summary>
	[JsonPropertyName("sync_threshold")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? SyncThreshold { get; init; }

	/// <summary>
	/// The <c>resize_factor</c> setting of the HNSW index.
	/// </summary>
	[JsonPropertyName("resize_factor")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public double? ResizeFactor { get; init; }
}
