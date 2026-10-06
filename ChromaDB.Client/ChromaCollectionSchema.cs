namespace ChromaDB.Client.Models;

/// <summary>
/// The schema of a new collection: the indexes of its keys, <c>schema</c> in Chroma. Only what is set here is sent; the server fills
/// in the rest. Chroma 1.3.0 and later apply it; 1.0.0 to 1.2.2 create the collection without it, and then the client throws a
/// <c>ChromaException</c>.
/// </summary>
public sealed class ChromaCollectionSchema
{
	// Key, then value type, then index: { "title": { "string": { "string_inverted_index": { "enabled": false, "config": {} } } } }.
	private readonly Dictionary<string, Dictionary<string, Dictionary<string, object>>> _keys;
	// Value type, then index, for the keys not named in _keys.
	private readonly Dictionary<string, Dictionary<string, object>> _defaults;
	private readonly string? _gcpCmek;
	// The keys of the indexes left out on a server without sparse vector indexes.
	private readonly HashSet<string> _keysIfSupported;

	/// <summary>
	/// An empty schema.
	/// </summary>
	public ChromaCollectionSchema()
		: this([], [], null, [])
	{ }

	private ChromaCollectionSchema(Dictionary<string, Dictionary<string, Dictionary<string, object>>> keys, Dictionary<string, Dictionary<string, object>> defaults, string? gcpCmek, HashSet<string> keysIfSupported)
	{
		_keys = keys;
		_defaults = defaults;
		_gcpCmek = gcpCmek;
		_keysIfSupported = keysIfSupported;
	}

	/// <summary>
	/// A copy of the schema with a sparse vector index on the metadata key, which holds the sparse vectors of the records, like
	/// their BM25 vectors. <c>sourceKey</c> is the key of the text they come from, like <c>ChromaSearchKeys.Document</c>; with
	/// <c>bm25</c> the server applies the inverse document frequency of BM25 to them. <c>sourceKey</c> needs <c>embeddingFunction</c>, as
	/// Chroma Cloud rejects one without the other: then it throws an <c>ArgumentException</c>. Only Chroma Cloud has sparse vector indexes:
	/// a single server rejects them.
	/// </summary>
	/// <param name="key">The metadata key that holds the sparse vectors.</param>
	/// <param name="sourceKey">The key of the text the vectors come from, like <c>#document</c>, or null for none.</param>
	/// <param name="bm25">Whether the server applies the inverse document frequency of BM25.</param>
	/// <param name="embeddingFunction">The function that computes the vectors, or null for none; a source key needs one.</param>
	/// <returns>The new schema; this one does not change.</returns>
	public ChromaCollectionSchema WithSparseVectorIndex(string key, string? sourceKey = null, bool bm25 = false, ChromaEmbeddingFunctionReference? embeddingFunction = null)
		=> WithSparseVectorIndex(key, ChromaSparseIndexAlgorithm.Wand, sourceKey, bm25, embeddingFunction);

	/// <summary>
	/// A copy of the schema with a BM25 index on the text of the source key, a metadata key or <c>ChromaSearchKeys.Document</c>, with
	/// the <c>chroma_bm25</c> function, whose vectors the client computes as it writes the records. The index is on the metadata key
	/// named after the source, like <c>title_bm25</c>, or <c>document_bm25</c> for the documents; a write that gives that key another
	/// value than a sparse vector throws an <c>ArgumentException</c>. Only Chroma Cloud has sparse vector indexes: with <c>ifSupported</c>
	/// the client leaves the index out on the other servers, which reject it.
	/// </summary>
	/// <param name="sourceKey">The key of the text: a metadata key, or <c>ChromaSearchKeys.Document</c>.</param>
	/// <param name="ifSupported">Whether the client leaves the index out on a server without sparse vector indexes.</param>
	/// <returns>The new schema; this one does not change.</returns>
	public ChromaCollectionSchema WithBm25Index(string sourceKey, bool ifSupported = false)
	{
		var key = (sourceKey ?? throw new ArgumentNullException(nameof(sourceKey))) == ChromaSearchKeys.Document ? "document_bm25" : sourceKey + "_bm25";
		var schema = WithSparseVectorIndex(key, sourceKey, bm25: true, ChromaEmbeddingFunctionReference.ChromaBm25());
		return ifSupported ? new(schema._keys, schema._defaults, schema._gcpCmek, [.. schema._keysIfSupported, key]) : schema;
	}

	/// <summary>
	/// The same as the overload without <c>algorithm</c>, with the algorithm of the index: <c>MaxScore</c> is on Chroma Cloud for the
	/// tenants that have it, <c>Wand</c> is the default.
	/// </summary>
	/// <param name="key">The metadata key that holds the sparse vectors.</param>
	/// <param name="algorithm">The algorithm of the index.</param>
	/// <param name="sourceKey">The key of the text the vectors come from, like <c>#document</c>, or null for none.</param>
	/// <param name="bm25">Whether the server applies the inverse document frequency of BM25.</param>
	/// <param name="embeddingFunction">The function that computes the vectors, or null for none; a source key needs one.</param>
	/// <returns>The new schema; this one does not change.</returns>
	public ChromaCollectionSchema WithSparseVectorIndex(string key, ChromaSparseIndexAlgorithm algorithm, string? sourceKey = null, bool bm25 = false, ChromaEmbeddingFunctionReference? embeddingFunction = null)
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
		// As the Python client of Chroma: the default algorithm is left out, which the servers that do not know it read too.
		if (algorithm != ChromaSparseIndexAlgorithm.Wand)
		{
			config["algorithm"] = algorithm switch
			{
				ChromaSparseIndexAlgorithm.MaxScore => "max_score",
				_ => throw new ArgumentOutOfRangeException(nameof(algorithm)),
			};
		}
		return WithKeyIndex(key, "sparse_vector", "sparse_vector_index", true, config);
	}

	/// <summary>
	/// A copy of the schema with the index turned on, as <c>create_index</c> of the Python client of Chroma: on the metadata key, or on
	/// every key without one. The full-text search index is on the documents only, <c>#document</c>: another key throws an
	/// <c>ArgumentException</c>.
	/// </summary>
	/// <param name="index">The index.</param>
	/// <param name="key">The metadata key, or null for every key.</param>
	/// <returns>The new schema; this one does not change.</returns>
	public ChromaCollectionSchema WithIndex(ChromaSchemaIndex index, string? key = null)
		=> WithValueIndex(index, key, true);

	/// <summary>
	/// A copy of the schema with the index turned off, as <c>delete_index</c> of the Python client of Chroma: on the metadata key, or on
	/// every key without one. A filter on a key without its index finds nothing. The full-text search index is on the documents only,
	/// <c>#document</c>: another key throws an <c>ArgumentException</c>.
	/// </summary>
	/// <param name="index">The index.</param>
	/// <param name="key">The metadata key, or null for every key.</param>
	/// <returns>The new schema; this one does not change.</returns>
	public ChromaCollectionSchema WithoutIndex(ChromaSchemaIndex index, string? key = null)
		=> WithValueIndex(index, key, false);

	/// <summary>
	/// A copy of the schema whose collection is encrypted with a customer-managed key of Google Cloud KMS, as <c>set_cmek</c> of the
	/// Python client of Chroma: Chroma Cloud only. A resource that is not
	/// <c>projects/{project}/locations/{location}/keyRings/{key ring}/cryptoKeys/{key}</c> throws an <c>ArgumentException</c>.
	/// </summary>
	/// <param name="resource">The resource name of the key.</param>
	/// <returns>The new schema; this one does not change.</returns>
	public ChromaCollectionSchema WithGcpCmek(string resource)
	{
		if (!GcpKey.IsMatch(resource))
		{
			throw new ArgumentException("A key of Google Cloud KMS is projects/{project}/locations/{location}/keyRings/{key ring}/cryptoKeys/{key}.", nameof(resource));
		}
		return new(_keys, _defaults, resource, _keysIfSupported);
	}

	// The pattern of the Python client, with one segment for each part and nothing after the key, not even a key version.
	private static readonly System.Text.RegularExpressions.Regex GcpKey = new(@"\Aprojects/[^/\s]+/locations/[^/\s]+/keyRings/[^/\s]+/cryptoKeys/[^/\s]+\z");

	/// <summary>
	/// The JSON of the schema, as the client sends it. With the space, the index settings or the embedding function of
	/// <c>ChromaCollectionConfiguration</c> in the definition of the collection, the client adds them to it.
	/// </summary>
	/// <returns>The JSON.</returns>
	public override string ToString()
		=> System.Text.Json.JsonSerializer.Serialize(ToSchema(null), Common.HttpClientHelpers.TypeInfo<Dictionary<string, object>>(Common.HttpClientHelpers.PostJsonSerializerOptions));

	private ChromaCollectionSchema WithValueIndex(ChromaSchemaIndex index, string? key, bool enabled)
	{
		var (valueType, name) = index switch
		{
			ChromaSchemaIndex.FullTextSearch => ("string", "fts_index"),
			ChromaSchemaIndex.StringInverted => ("string", "string_inverted_index"),
			ChromaSchemaIndex.IntInverted => ("int", "int_inverted_index"),
			ChromaSchemaIndex.FloatInverted => ("float", "float_inverted_index"),
			ChromaSchemaIndex.BoolInverted => ("bool", "bool_inverted_index"),
			_ => throw new ArgumentOutOfRangeException(nameof(index)),
		};
		// As the Python client: the full-text search index is on #document, the default of Chroma, and only there.
		if (index == ChromaSchemaIndex.FullTextSearch)
		{
			if (key is not null && key != ChromaSearchKeys.Document)
			{
				throw new ArgumentException($"The full-text search index is on the documents only, {ChromaSearchKeys.Document}.", nameof(key));
			}
			key = ChromaSearchKeys.Document;
		}
		if (key is null)
		{
			var defaults = Copy(_defaults);
			defaults[valueType] = new Dictionary<string, object>(defaults.TryGetValue(valueType, out var indexes) ? indexes : []) { [name] = Index(enabled, []) };
			return new(_keys, defaults, _gcpCmek, _keysIfSupported);
		}
		return WithKeyIndex(key, valueType, name, enabled, []);
	}

	private ChromaCollectionSchema WithKeyIndex(string key, string valueType, string name, bool enabled, Dictionary<string, object> config)
	{
		var keys = _keys.ToDictionary(x => x.Key, x => Copy(x.Value));
		var types = keys.TryGetValue(key, out var existing) ? existing : [];
		types[valueType] = new Dictionary<string, object>(types.TryGetValue(valueType, out var indexes) ? indexes : []) { [name] = Index(enabled, config) };
		keys[key] = types;
		// An index added later on the key keeps the key on every server.
		return new(keys, _defaults, _gcpCmek, [.. _keysIfSupported.Where(x => x != key)]);
	}

	private static Dictionary<string, object> Index(bool enabled, Dictionary<string, object> config)
		=> new() { ["enabled"] = enabled, ["config"] = config };

	private static Dictionary<string, Dictionary<string, object>> Copy(Dictionary<string, Dictionary<string, object>> types)
		=> types.ToDictionary(x => x.Key, x => new Dictionary<string, object>(x.Value));

	// The metadata keys the schema names.
	internal IEnumerable<string> Keys => _keys.Keys;

	// The schema without the indexes added with ifSupported, or null when nothing else is in it.
	internal ChromaCollectionSchema? WithoutIndexesIfSupported()
	{
		if (_keysIfSupported.Count == 0)
		{
			return this;
		}
		var keys = _keys.Where(x => !_keysIfSupported.Contains(x.Key)).ToDictionary(x => x.Key, x => Copy(x.Value));
		return keys.Count == 0 && _defaults.Count == 0 && _gcpCmek is null ? null : new(keys, _defaults, _gcpCmek, []);
	}

	// The settings of the vector index go as create_index(VectorIndexConfig(...)) of the Python client writes them: in the defaults
	// and on #embedding. Chroma rejects a configuration, like the hnsw:space metadata, together with a schema.

	internal Dictionary<string, object> ToSchema(ChromaCollectionConfiguration? configuration)
	{
		var defaults = Copy(_defaults);
		var keys = _keys.ToDictionary(x => x.Key, x => Copy(x.Value));
		var vector = new Dictionary<string, object>();
		if (configuration?.Space is { } space)
		{
			vector["space"] = Common.ChromaSpaceNames.ToName(space);
		}
		if (configuration?.Hnsw is { } hnsw)
		{
			vector["hnsw"] = hnsw.ToJson();
		}
		if (configuration?.Spann is { } spann)
		{
			vector["spann"] = spann.ToJson();
		}
		if (configuration?.EmbeddingFunction is { } embeddingFunction)
		{
			vector["embedding_function"] = embeddingFunction.ToJson();
		}
		if (vector.Count > 0)
		{
			defaults["float_list"] = new() { ["vector_index"] = Index(false, vector) };
			var embedding = keys.TryGetValue(ChromaSearchKeys.Embedding, out var existing) ? existing : [];
			embedding["float_list"] = new() { ["vector_index"] = Index(true, new Dictionary<string, object>(vector)) };
			keys[ChromaSearchKeys.Embedding] = embedding;
		}
		var schema = new Dictionary<string, object>
		{
			["defaults"] = defaults.ToDictionary(x => x.Key, x => (object)x.Value),
			["keys"] = keys.ToDictionary(x => x.Key, x => (object)x.Value.ToDictionary(y => y.Key, y => (object)y.Value)),
		};
		if (_gcpCmek is not null)
		{
			schema["cmek"] = new Dictionary<string, object> { ["gcp"] = _gcpCmek };
		}
		return schema;
	}
}
