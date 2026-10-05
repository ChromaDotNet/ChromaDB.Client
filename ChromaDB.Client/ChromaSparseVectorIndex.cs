using System.Text.Json;

namespace ChromaDB.Client.Models;

/// <summary>
/// A sparse vector index of a collection, read from its schema.
/// </summary>
public sealed class ChromaSparseVectorIndex
{
	internal ChromaSparseVectorIndex(string key, string? sourceKey, bool bm25, string? embeddingFunction, JsonElement? embeddingFunctionConfig, ChromaSparseIndexAlgorithm algorithm = ChromaSparseIndexAlgorithm.Wand)
	{
		Algorithm = algorithm;
		Key = key;
		SourceKey = sourceKey;
		Bm25 = bm25;
		EmbeddingFunction = embeddingFunction;
		EmbeddingFunctionConfig = embeddingFunctionConfig;
		Bm25Function = embeddingFunction == ChromaBm25.Name ? ChromaBm25.FromConfig(embeddingFunctionConfig) : null;
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
	/// The algorithm of the index: <c>Wand</c>, the default, when the schema names none.
	/// </summary>
	public ChromaSparseIndexAlgorithm Algorithm { get; }

	/// <summary>
	/// The name of the embedding function the schema declares, like <c>chroma_bm25</c>, when it declares a known one.
	/// </summary>
	public string? EmbeddingFunction { get; }

	/// <summary>
	/// The settings of the embedding function, <c>config</c> in the schema, as the server sends them; null when the schema has none.
	/// </summary>
	public JsonElement? EmbeddingFunctionConfig { get; }

	/// <summary>
	/// The BM25 function with the settings of the schema, as the Python client of Chroma builds it, when the function is
	/// <c>chroma_bm25</c>: the client computes the vectors of the index with it. Null for the other functions, and for settings
	/// of the wrong type.
	/// </summary>
	public ChromaBm25? Bm25Function { get; }
}
