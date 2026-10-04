namespace ChromaDB.Client.Models;

/// <summary>
/// A sparse vector index of a collection, read from its schema.
/// </summary>
public sealed class ChromaSparseVectorIndex
{
	internal ChromaSparseVectorIndex(string key, string? sourceKey, bool bm25, string? embeddingFunction)
	{
		Key = key;
		SourceKey = sourceKey;
		Bm25 = bm25;
		EmbeddingFunction = embeddingFunction;
	}

	/// <summary>
	/// The metadata key that holds the sparse vectors.
	/// </summary>
	public string Key { get; }

	/// <summary>
	/// The key of the text the sparse vectors come from, like <c>#document</c>, when the schema names one.
	/// </summary>
	public string? SourceKey { get; }

	/// <summary>
	/// Whether the server applies the inverse document frequency of BM25 to the vectors.
	/// </summary>
	public bool Bm25 { get; }

	/// <summary>
	/// The name of the embedding function the schema declares, like <c>chroma_bm25</c>, when it declares a known one.
	/// </summary>
	public string? EmbeddingFunction { get; }
}
