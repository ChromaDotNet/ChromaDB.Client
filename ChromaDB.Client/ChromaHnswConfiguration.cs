namespace ChromaDB.Client.Models;

/// <summary>
/// The settings of the HNSW index of a new collection, the index of a single Chroma server; the ones left null take the default of
/// the server. Chroma Cloud uses a SPANN index and ignores them: then <c>CreateCollectionAsync</c> throws a <c>ChromaException</c>.
/// </summary>
public class ChromaHnswConfiguration
{
	/// <summary>
	/// The <c>ef_construction</c> setting: how many neighbors are looked at when a record is added.
	/// </summary>
	public int? EfConstruction { get; init; }

	/// <summary>
	/// The <c>ef_search</c> setting: how many neighbors are looked at in a query.
	/// </summary>
	public int? EfSearch { get; init; }

	/// <summary>
	/// The <c>max_neighbors</c> setting, <c>M</c> of HNSW: how many neighbors each record links to, at least 2. Chroma crashes on the
	/// first write with 0 and misses the nearest records with 1: the client throws an <c>ArgumentException</c> for them.
	/// </summary>
	public int? MaxNeighbors { get; init; }

	/// <summary>
	/// The <c>resize_factor</c> setting: how much the index grows when it is full.
	/// </summary>
	public double? ResizeFactor { get; init; }

	/// <summary>
	/// The <c>sync_threshold</c> setting: how many records are added before the index is written to disk.
	/// </summary>
	public int? SyncThreshold { get; init; }

	/// <summary>
	/// The <c>batch_size</c> setting: how many records are indexed together.
	/// </summary>
	public int? BatchSize { get; init; }

	/// <summary>
	/// The <c>num_threads</c> setting: how many threads build the index.
	/// </summary>
	public int? NumThreads { get; init; }

	// Chroma 1.5.9 crashes on the first write to an HNSW index with no neighbors, and an index with one neighbor misses the nearest
	// records: the client rejects them before the request, wherever they come from.
	internal static void CheckMaxNeighbors(object? maxNeighbors, string paramName)
	{
		if (maxNeighbors is IConvertible and not string and not bool && System.Convert.ToDouble(maxNeighbors, System.Globalization.CultureInfo.InvariantCulture) < 2)
		{
			throw new ArgumentException($"The HNSW index needs at least 2 neighbors, not {maxNeighbors}: Chroma crashes on the first write with 0 and misses the nearest records with 1.", paramName);
		}
	}

	// The "hnsw:" metadata of the settings, which every tested Chroma applies, as the space.
	internal IEnumerable<KeyValuePair<string, object>> ToMetadata()
	{
		if (EfConstruction is { } efConstruction) yield return new("hnsw:construction_ef", efConstruction);
		if (EfSearch is { } efSearch) yield return new("hnsw:search_ef", efSearch);
		if (MaxNeighbors is { } maxNeighbors) yield return new("hnsw:M", maxNeighbors);
		if (ResizeFactor is { } resizeFactor) yield return new("hnsw:resize_factor", resizeFactor);
		if (SyncThreshold is { } syncThreshold) yield return new("hnsw:sync_threshold", syncThreshold);
		if (BatchSize is { } batchSize) yield return new("hnsw:batch_size", batchSize);
		if (NumThreads is { } numThreads) yield return new("hnsw:num_threads", numThreads);
	}

	// The settings in a vector index of a schema.
	internal Dictionary<string, object> ToJson()
	{
		var json = new Dictionary<string, object>();
		if (EfConstruction is { } efConstruction) json["ef_construction"] = efConstruction;
		if (EfSearch is { } efSearch) json["ef_search"] = efSearch;
		if (MaxNeighbors is { } maxNeighbors) json["max_neighbors"] = maxNeighbors;
		if (ResizeFactor is { } resizeFactor) json["resize_factor"] = resizeFactor;
		if (SyncThreshold is { } syncThreshold) json["sync_threshold"] = syncThreshold;
		if (BatchSize is { } batchSize) json["batch_size"] = batchSize;
		if (NumThreads is { } numThreads) json["num_threads"] = numThreads;
		return json;
	}
}
