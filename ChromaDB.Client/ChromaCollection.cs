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
	public Dictionary<string, object>? Metadata { get; init; }

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
					indexes.Add(new ChromaSparseVectorIndex(
						key.Name,
						StringProperty(config, "source_key"),
						config.ValueKind == JsonValueKind.Object && config.TryGetProperty("bm25", out var bm25) && bm25.ValueKind == JsonValueKind.True,
						config.ValueKind == JsonValueKind.Object && config.TryGetProperty("embedding_function", out var function) ? StringProperty(function, "name") : null));
				}
			}
			return indexes;
		}
	}

	private static string? StringProperty(JsonElement element, string name)
		=> element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

	/// <summary>
	/// The distance function of the collection.
	/// From the <c>hnsw:space</c> metadata, which every tested Chroma sends back, or from <c>hnsw.space</c> of the configuration,
	/// which Chroma 1.0.6 and later send, or from <c>spann.space</c>, which Chroma Cloud sends with <c>hnsw</c> null.
	/// Null when none is there: Chroma 0.5.4 to 1.0.5 send <c>hnsw_configuration.space</c>,
	/// which says <c>l2</c> also for the collections that use another space.
	/// </summary>
	[JsonIgnore]
	public ChromaSpace? Space
		=> Metadata is not null && Metadata.TryGetValue(ChromaSpaceNames.MetadataKey, out var space) && space is string name
			? ChromaSpaceNames.FromName(name)
			: ConfigurationSpace("hnsw") ?? ConfigurationSpace("spann");

	private ChromaSpace? ConfigurationSpace(string index)
		=> ConfigurationJson is { ValueKind: JsonValueKind.Object } configuration
			&& configuration.TryGetProperty(index, out var settings) && settings.ValueKind == JsonValueKind.Object
			&& settings.TryGetProperty("space", out var space) && space.ValueKind == JsonValueKind.String
			? ChromaSpaceNames.FromName(space.GetString())
			: null;

	/// <summary>
	/// Creates the object for a collection with the given name; it sends no request.
	/// </summary>
	public ChromaCollection(string name)
	{
		Name = name;
	}
}
