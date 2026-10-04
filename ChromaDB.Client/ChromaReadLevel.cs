namespace ChromaDB.Client;

// What a read looks at, "read_level" in Chroma. It matters on Chroma Cloud, where new records are indexed later;
// a single server indexes them at once and accepts it without a difference.
public enum ChromaReadLevel
{
	// The index and the records not yet indexed, the default of Chroma.
	IndexAndWal,
	// Only the records already indexed: faster, without the latest writes.
	IndexOnly,
	// The index and a bounded part of the records not yet indexed.
	IndexAndBoundedWal,
}
