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
