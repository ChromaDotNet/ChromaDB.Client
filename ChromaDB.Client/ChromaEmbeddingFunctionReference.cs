namespace ChromaDB.Client.Models;

/// <summary>
/// The embedding function that a collection declares for a key, <c>embedding_function</c> in its schema: the clients of Chroma that
/// know the function compute the embeddings with it. The client only declares it.
/// </summary>
public sealed class ChromaEmbeddingFunctionReference
{
	private readonly string _name;
	private readonly Dictionary<string, object> _config;

	private ChromaEmbeddingFunctionReference(string name, Dictionary<string, object> config)
	{
		_name = name;
		_config = config;
	}

	/// <summary>
	/// A function known to the clients of Chroma by its name, with its settings.
	/// </summary>
	public static ChromaEmbeddingFunctionReference Known(string name, Dictionary<string, object>? config = null)
		=> new(name, config is null ? [] : new Dictionary<string, object>(config)); // a copy: later changes to config do not reach the schemas

	/// <summary>
	/// The BM25 function of Chroma, <c>chroma_bm25</c>, with the settings the Python client of Chroma 1.5.9 uses by default:
	/// <c>k</c> 1.2, <c>b</c> 0.75, <c>avg_doc_length</c> 256, <c>token_max_length</c> 40, <c>include_tokens</c> false.
	/// </summary>
	public static ChromaEmbeddingFunctionReference ChromaBm25(double k = 1.2, double b = 0.75, double avgDocLength = 256, int tokenMaxLength = 40, bool includeTokens = false)
		=> new("chroma_bm25", new()
		{
			["k"] = k,
			["b"] = b,
			["avg_doc_length"] = avgDocLength,
			["token_max_length"] = tokenMaxLength,
			["include_tokens"] = includeTokens,
		});

	internal Dictionary<string, object> ToJson()
		=> new() { ["type"] = "known", ["name"] = _name, ["config"] = _config };
}
