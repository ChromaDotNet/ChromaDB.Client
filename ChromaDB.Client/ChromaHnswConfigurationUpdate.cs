using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models;

// The settings of the HNSW index that can change; the ones left null keep their value.
public class ChromaHnswConfigurationUpdate
{
	[JsonPropertyName("ef_search")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? EfSearch { get; init; }

	[JsonPropertyName("max_neighbors")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? MaxNeighbors { get; init; }

	[JsonPropertyName("num_threads")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? NumThreads { get; init; }

	[JsonPropertyName("batch_size")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? BatchSize { get; init; }

	[JsonPropertyName("sync_threshold")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? SyncThreshold { get; init; }

	[JsonPropertyName("resize_factor")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public double? ResizeFactor { get; init; }
}
