using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models;

/// <summary>
/// How far Chroma Cloud has indexed the writes to a collection.
/// </summary>
public class ChromaIndexingStatus
{
	/// <summary>
	/// The indexing progress of the writes, from 0 to 1.
	/// </summary>
	[JsonPropertyName("op_indexing_progress")]
	public float OpIndexingProgress { get; init; }

	/// <summary>
	/// How many writes are indexed.
	/// </summary>
	[JsonPropertyName("num_indexed_ops")]
	public long NumIndexedOps { get; init; }

	/// <summary>
	/// How many writes are not indexed yet.
	/// </summary>
	[JsonPropertyName("num_unindexed_ops")]
	public long NumUnindexedOps { get; init; }

	/// <summary>
	/// The total number of writes.
	/// </summary>
	[JsonPropertyName("total_ops")]
	public long TotalOps { get; init; }
}
