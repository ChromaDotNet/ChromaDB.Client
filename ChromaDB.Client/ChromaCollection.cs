using System.Text.Json;
using System.Text.Json.Serialization;
using ChromaDB.Client.Common;

namespace ChromaDB.Client.Models;

/// <summary>
/// A collection of Chroma, as the server describes it.
/// </summary>
public class ChromaCollection
{
	/// <summary>
	/// The id of the collection.
	/// </summary>
	[JsonPropertyName("id")]
	public Guid Id { get; init; }

	/// <summary>
	/// The name of the collection.
	/// </summary>
	[JsonPropertyName("name")]
	public string Name { get; }

	/// <summary>
	/// The metadata of the collection.
	/// </summary>
	[JsonPropertyName("metadata")]
	public IReadOnlyDictionary<string, object>? Metadata { get; init; }

	/// <summary>
	/// The tenant of the collection.
	/// </summary>
	[JsonPropertyName("tenant")]
	public string? Tenant { get; init; }

	/// <summary>
	/// The database of the collection.
	/// </summary>
	[JsonPropertyName("database")]
	public string? Database { get; init; }

	/// <summary>
	/// The number of dimensions of the embeddings of the collection, set by the first write: null before it, and from Chroma 0.5.0
	/// and earlier, which do not send it.
	/// </summary>
	[JsonPropertyName("dimension")]
	public int? Dimension { get; init; }

	/// <summary>
	/// The version of the collection, as the server counts it: null from Chroma 0.5.0 and earlier, which do not send it.
	/// </summary>
	[JsonPropertyName("version")]
	public int? Version { get; init; }

	/// <summary>
	/// The position of the collection in the log of the server: null from Chroma 0.5.7 and earlier, which do not send it.
	/// </summary>
	[JsonPropertyName("log_position")]
	public long? LogPosition { get; init; }

	/// <summary>
	/// The configuration as the server sends it: Chroma 0.4.10 to 0.5.3 send none.
	/// </summary>
	[JsonPropertyName("configuration_json")]
	public JsonElement? ConfigurationJson { get; init; }

	/// <summary>
	/// The schema as the server sends it: the indexes of the keys. Chroma 1.2.2 and earlier send none.
	/// </summary>
	[JsonPropertyName("schema")]
	public JsonElement? SchemaJson { get; init; }

	/// <summary>
	/// The sparse vector indexes that the schema enables, like the one for BM25; empty without a schema.
	/// </summary>
	[JsonIgnore]
	public IReadOnlyList<ChromaSparseVectorIndex> SparseVectorIndexes
	{
		get
		{
			if (SchemaJson is not { ValueKind: JsonValueKind.Object } schema
				|| !schema.TryGetProperty("keys", out var keys) || keys.ValueKind != JsonValueKind.Object)
			{
				return [];
			}
			var indexes = new List<ChromaSparseVectorIndex>();
			foreach (var key in keys.EnumerateObject())
			{
				if (key.Value.ValueKind == JsonValueKind.Object
					&& key.Value.TryGetProperty("sparse_vector", out var sparse) && sparse.ValueKind == JsonValueKind.Object
					&& sparse.TryGetProperty("sparse_vector_index", out var index) && index.ValueKind == JsonValueKind.Object
					&& index.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.True)
				{
					var config = index.TryGetProperty("config", out var c) && c.ValueKind == JsonValueKind.Object ? c : default;
					var function = config.ValueKind == JsonValueKind.Object && config.TryGetProperty("embedding_function", out var f) ? f : default;
					indexes.Add(new ChromaSparseVectorIndex(
						key.Name,
						StringProperty(config, "source_key"),
						config.ValueKind == JsonValueKind.Object && config.TryGetProperty("bm25", out var bm25) && bm25.ValueKind == JsonValueKind.True,
						StringProperty(function, "name"),
						function.ValueKind == JsonValueKind.Object && function.TryGetProperty("config", out var settings) && settings.ValueKind != JsonValueKind.Null ? settings.Clone() : null,
						StringProperty(config, "algorithm") == "max_score" ? ChromaSparseIndexAlgorithm.MaxScore : ChromaSparseIndexAlgorithm.Wand));
				}
			}
			return indexes;
		}
	}

	/// <summary>
	/// The sparse vector index of the schema whose vectors the client computes with BM25 (<c>chroma_bm25</c>) from the text of the source
	/// key, like <c>#document</c> or a metadata key: the one a text query of <c>ChromaRank.SparseKnn</c> on that text uses. Null when the
	/// schema has none.
	/// </summary>
	/// <param name="sourceKey">The key of the text: <c>#document</c>, or a metadata key.</param>
	/// <returns>The index, or null.</returns>
	public ChromaSparseVectorIndex? FindBm25Index(string sourceKey)
		=> SparseVectorIndexes.FirstOrDefault(index => index.SourceKey == sourceKey && index.Bm25Function is not null);

	private static string? StringProperty(JsonElement element, string name)
		=> element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

	/// <summary>
	/// The distance function of the collection.
	/// From the <c>hnsw:space</c> metadata, which every tested Chroma sends back, or from <c>hnsw.space</c> of the configuration,
	/// which Chroma 1.0.6 and later send, or from <c>spann.space</c>, which Chroma Cloud sends with <c>hnsw</c> null.
	/// On Chroma 0.x, which keeps the space only in that metadata, a collection without it uses <c>l2</c>, the default of Chroma:
	/// a collection the client reads from a 0.x server has then <c>L2</c>.
	/// Null when none is there on the other servers: Chroma 0.5.4 to 1.0.5 send <c>hnsw_configuration.space</c>,
	/// which says <c>l2</c> also for the collections that use another space.
	/// </summary>
	[JsonIgnore]
	public ChromaSpace? Space
		=> Metadata is not null && Metadata.TryGetValue(ChromaSpaceNames.MetadataKey, out var space) && space is string name
			? ChromaSpaceNames.FromName(name)
			: ConfigurationSpace("hnsw") ?? ConfigurationSpace("spann") ?? DefaultSpace;

	// The space of the server for a collection that does not report one: l2 on Chroma 0.x.
	internal ChromaSpace? DefaultSpace { get; set; }

	private ChromaSpace? ConfigurationSpace(string index)
		=> ConfigurationJson is { ValueKind: JsonValueKind.Object } configuration
			&& configuration.TryGetProperty(index, out var settings) && settings.ValueKind == JsonValueKind.Object
			&& settings.TryGetProperty("space", out var space) && space.ValueKind == JsonValueKind.String
			? ChromaSpaceNames.FromName(space.GetString())
			: null;

	/// <summary>
	/// Creates the object for a collection with the given name; it sends no request.
	/// </summary>
	/// <param name="name">The name of the collection.</param>
	public ChromaCollection(string name)
	{
		Name = name;
	}
}
