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
	/// <c>bm25</c> the server applies the inverse document frequency of BM25 to them. <c>sourceKey</c> needs <c>embeddingFunction</c>, as
	/// Chroma Cloud rejects one without the other: then it throws an <c>ArgumentException</c>. Only Chroma Cloud has sparse vector indexes:
	/// a single server rejects them.
	/// </summary>
	public ChromaCollectionSchema WithSparseVectorIndex(string key, string? sourceKey = null, bool bm25 = false, ChromaEmbeddingFunctionReference? embeddingFunction = null)
	{
		if (sourceKey is not null && embeddingFunction is null)
		{
			throw new ArgumentException("A source key needs an embedding function: Chroma Cloud rejects one without the other.", nameof(embeddingFunction));
		}
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

	/// <summary>
	/// The JSON of the schema, as the client sends it. With <c>ChromaCollectionConfiguration.Space</c> in the definition of the collection,
	/// the client adds the space to it.
	/// </summary>
	public override string ToString()
		=> System.Text.Json.JsonSerializer.Serialize(ToSchema(), Common.HttpClientHelpers.TypeInfo<Dictionary<string, object>>(Common.HttpClientHelpers.PostJsonSerializerOptions));

	// With a space, the vector index gets it as create_index(VectorIndexConfig(space=...)) of the Python client writes it: in the
	// defaults and on #embedding. Chroma rejects a configuration, like the hnsw:space metadata, together with a schema.
	internal Dictionary<string, object> ToSchema(ChromaSpace? space = null)
	{
		if (space is not { } value)
		{
			return new() { ["defaults"] = new Dictionary<string, object>(), ["keys"] = _keys };
		}
		var name = Common.ChromaSpaceNames.ToName(value);
		Dictionary<string, object> VectorIndex(bool enabled) => new()
		{
			["float_list"] = new Dictionary<string, object>
			{
				["vector_index"] = new Dictionary<string, object> { ["enabled"] = enabled, ["config"] = new Dictionary<string, object> { ["space"] = name } },
			},
		};
		return new() { ["defaults"] = VectorIndex(false), ["keys"] = new Dictionary<string, object>(_keys) { [ChromaSearchKeys.Embedding] = VectorIndex(true) } };
	}
}
