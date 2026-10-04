namespace ChromaDB.Client.Models;

/// <summary>
/// The schema of a new collection: the indexes of its keys, <c>schema</c> in Chroma. Only what is set here is sent; the server fills
/// in the rest. Chroma 1.3.0 and later apply it; 1.0.0 to 1.2.2 create the collection without it, and then the client throws a
/// <c>ChromaException</c>.
/// </summary>
public sealed class ChromaCollectionSchema
{
	private readonly Dictionary<string, object> _keys;

	/// <summary>
	/// An empty schema.
	/// </summary>
	public ChromaCollectionSchema()
		: this([])
	{ }

	private ChromaCollectionSchema(Dictionary<string, object> keys)
	{
		_keys = keys;
	}

	/// <summary>
	/// A copy of the schema with a sparse vector index on the metadata key, which holds the sparse vectors of the records, like
	/// their BM25 vectors. <c>sourceKey</c> is the key of the text they come from, like <c>ChromaSearchKeys.Document</c>; with
	/// <c>bm25</c> the server applies the inverse document frequency of BM25 to them; with <c>sourceKey</c>, Chroma Cloud also wants
	/// <c>embeddingFunction</c>. Only Chroma Cloud has sparse vector indexes: a single server rejects them.
	/// </summary>
	public ChromaCollectionSchema WithSparseVectorIndex(string key, string? sourceKey = null, bool bm25 = false, ChromaEmbeddingFunctionReference? embeddingFunction = null)
	{
		var config = new Dictionary<string, object>();
		if (embeddingFunction is not null)
		{
			config["embedding_function"] = embeddingFunction.ToJson();
		}
		if (sourceKey is not null)
		{
			config["source_key"] = sourceKey;
		}
		if (bm25)
		{
			config["bm25"] = true;
		}
		var keys = new Dictionary<string, object>(_keys)
		{
			[key] = new Dictionary<string, object>
			{
				["sparse_vector"] = new Dictionary<string, object>
				{
					["sparse_vector_index"] = new Dictionary<string, object> { ["enabled"] = true, ["config"] = config },
				},
			},
		};
		return new(keys);
	}

	internal Dictionary<string, object> ToSchema()
		=> new() { ["defaults"] = new Dictionary<string, object>(), ["keys"] = _keys };
}
