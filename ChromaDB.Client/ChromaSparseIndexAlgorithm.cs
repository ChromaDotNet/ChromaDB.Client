namespace ChromaDB.Client.Models;

/// <summary>
/// The algorithm of a sparse vector index, <c>algorithm</c> in the schema of the collection.
/// </summary>
public enum ChromaSparseIndexAlgorithm
{
	/// <summary>
	/// WAND, <c>wand</c>, the default.
	/// </summary>
	Wand,
	/// <summary>
	/// MaxScore, <c>max_score</c>: distributed Chroma, the one Chroma Cloud runs on, and VecturaDB keep it in the schema, Chroma Cloud for
	/// the tenants that have it.
	/// </summary>
	MaxScore,
}
