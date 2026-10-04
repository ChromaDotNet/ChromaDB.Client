namespace ChromaDB.Client;

// The distance function of a collection, "hnsw:space" in Chroma.
public enum ChromaSpace
{
	// Squared Euclidean distance, the default of Chroma.
	L2,
	// 1 - cosine similarity.
	Cosine,
	// 1 - inner product.
	InnerProduct,
}
