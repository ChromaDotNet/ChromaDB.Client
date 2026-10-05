namespace ChromaDB.Client.Models;

/// <summary>
/// The algorithm of a sparse vector index of Chroma Cloud, <c>algorithm</c> in its schema.
/// </summary>
public enum ChromaSparseIndexAlgorithm
{
	/// <summary>
	/// WAND, <c>wand</c>, the default.
	/// </summary>
	Wand,
	/// <summary>
	/// MaxScore, <c>max_score</c>, on Chroma Cloud for the tenants that have it.
	/// </summary>
	MaxScore,
}
