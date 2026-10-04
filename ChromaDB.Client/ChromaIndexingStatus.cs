using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models;

// How far Chroma Cloud has indexed the writes to a collection.
public class ChromaIndexingStatus
{
	// From 0 to 1.
	[JsonPropertyName("op_indexing_progress")]
	public float OpIndexingProgress { get; init; }

	[JsonPropertyName("num_indexed_ops")]
	public long NumIndexedOps { get; init; }

	[JsonPropertyName("num_unindexed_ops")]
	public long NumUnindexedOps { get; init; }

	[JsonPropertyName("total_ops")]
	public long TotalOps { get; init; }
}
