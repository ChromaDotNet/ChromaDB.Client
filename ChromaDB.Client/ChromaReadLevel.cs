namespace ChromaDB.Client;

/// <summary>
/// What a read looks at, <c>read_level</c> in Chroma. It matters on Chroma Cloud, where new records are indexed later;
/// a single server indexes them at once and accepts it without a difference.
/// </summary>
public enum ChromaReadLevel
{
	/// <summary>
	/// The index and the records not yet indexed, the default of Chroma.
	/// </summary>
	IndexAndWal,
	/// <summary>
	/// Only the records already indexed: faster, without the latest writes.
	/// </summary>
	IndexOnly,
	/// <summary>
	/// The index and a bounded part of the records not yet indexed.
	/// </summary>
	IndexAndBoundedWal,
}
