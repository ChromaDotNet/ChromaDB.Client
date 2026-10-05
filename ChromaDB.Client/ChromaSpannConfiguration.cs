namespace ChromaDB.Client.Models;

/// <summary>
/// The settings of the SPANN index of a new collection, the index of Chroma Cloud; the ones left null take the default of the server.
/// A single Chroma server uses an HNSW index and ignores them: then <c>CreateCollectionAsync</c> throws a <c>ChromaException</c>.
/// </summary>
public class ChromaSpannConfiguration
{
	/// <summary>
	/// The <c>search_nprobe</c> setting: how many clusters a query looks into.
	/// </summary>
	public int? SearchNprobe { get; init; }

	/// <summary>
	/// The <c>write_nprobe</c> setting: how many clusters a write looks into.
	/// </summary>
	public int? WriteNprobe { get; init; }

	/// <summary>
	/// The <c>ef_construction</c> setting of the graph of the cluster centers.
	/// </summary>
	public int? EfConstruction { get; init; }

	/// <summary>
	/// The <c>ef_search</c> setting of the graph of the cluster centers.
	/// </summary>
	public int? EfSearch { get; init; }

	/// <summary>
	/// The <c>max_neighbors</c> setting of the graph of the cluster centers.
	/// </summary>
	public int? MaxNeighbors { get; init; }

	/// <summary>
	/// The <c>split_threshold</c> setting: the size above which a cluster is split.
	/// </summary>
	public int? SplitThreshold { get; init; }

	/// <summary>
	/// The <c>merge_threshold</c> setting: the size below which a cluster is merged.
	/// </summary>
	public int? MergeThreshold { get; init; }

	/// <summary>
	/// The <c>reassign_neighbor_count</c> setting: how many neighboring clusters are checked when a cluster is split.
	/// </summary>
	public int? ReassignNeighborCount { get; init; }

	/// <summary>
	/// The <c>search_rng_epsilon</c> setting. It goes in the schema of the collection, the only place Chroma takes it: Chroma Cloud,
	/// or Chroma 1.3.0 and later.
	/// </summary>
	public double? SearchRngEpsilon { get; init; }

	/// <summary>
	/// The <c>write_rng_epsilon</c> setting, from 5 to 10 on Chroma Cloud. It goes in the schema of the collection, as
	/// <c>SearchRngEpsilon</c>.
	/// </summary>
	public double? WriteRngEpsilon { get; init; }

	/// <summary>
	/// The <c>nreplica_count</c> setting: in how many clusters a record is written. It goes in the schema of the collection, as
	/// <c>SearchRngEpsilon</c>.
	/// </summary>
	public int? NreplicaCount { get; init; }

	/// <summary>
	/// The <c>num_samples_kmeans</c> setting: how many records the clustering samples. It goes in the schema of the collection, as
	/// <c>SearchRngEpsilon</c>.
	/// </summary>
	public int? NumSamplesKmeans { get; init; }

	/// <summary>
	/// The <c>num_centers_to_merge_to</c> setting. It goes in the schema of the collection, as <c>SearchRngEpsilon</c>.
	/// </summary>
	public int? NumCentersToMergeTo { get; init; }

	/// <summary>
	/// The <c>center_drift_threshold</c> setting. It goes in the schema of the collection, as <c>SearchRngEpsilon</c>.
	/// </summary>
	public double? CenterDriftThreshold { get; init; }

	// The settings that Chroma takes only in a schema, not in the configuration of a collection.
	internal bool HasSchemaOnlySettings
		=> SearchRngEpsilon is not null || WriteRngEpsilon is not null || NreplicaCount is not null || NumSamplesKmeans is not null
			|| NumCentersToMergeTo is not null || CenterDriftThreshold is not null;

	internal Dictionary<string, object> ToJson()
	{
		var json = new Dictionary<string, object>();
		if (SearchNprobe is { } searchNprobe) json["search_nprobe"] = searchNprobe;
		if (WriteNprobe is { } writeNprobe) json["write_nprobe"] = writeNprobe;
		if (EfConstruction is { } efConstruction) json["ef_construction"] = efConstruction;
		if (EfSearch is { } efSearch) json["ef_search"] = efSearch;
		if (MaxNeighbors is { } maxNeighbors) json["max_neighbors"] = maxNeighbors;
		if (SplitThreshold is { } splitThreshold) json["split_threshold"] = splitThreshold;
		if (MergeThreshold is { } mergeThreshold) json["merge_threshold"] = mergeThreshold;
		if (ReassignNeighborCount is { } reassignNeighborCount) json["reassign_neighbor_count"] = reassignNeighborCount;
		if (SearchRngEpsilon is { } searchRngEpsilon) json["search_rng_epsilon"] = searchRngEpsilon;
		if (WriteRngEpsilon is { } writeRngEpsilon) json["write_rng_epsilon"] = writeRngEpsilon;
		if (NreplicaCount is { } nreplicaCount) json["nreplica_count"] = nreplicaCount;
		if (NumSamplesKmeans is { } numSamplesKmeans) json["num_samples_kmeans"] = numSamplesKmeans;
		if (NumCentersToMergeTo is { } numCentersToMergeTo) json["num_centers_to_merge_to"] = numCentersToMergeTo;
		if (CenterDriftThreshold is { } centerDriftThreshold) json["center_drift_threshold"] = centerDriftThreshold;
		return json;
	}
}
