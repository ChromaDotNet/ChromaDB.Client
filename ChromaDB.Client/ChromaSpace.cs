namespace ChromaDB.Client;

/// <summary>
/// The distance function of a collection, <c>hnsw:space</c> in Chroma.
/// </summary>
public enum ChromaSpace
{
	/// <summary>
	/// Squared Euclidean distance, the default of Chroma.
	/// </summary>
	L2,
	/// <summary>
	/// 1 - cosine similarity.
	/// </summary>
	Cosine,
	/// <summary>
	/// 1 - inner product.
	/// </summary>
	InnerProduct,
}
