using System.Text.Json;
using System.Text.Json.Serialization;
using ChromaDB.Client.Common;

namespace ChromaDB.Client.Models;

public class ChromaCollection
{
	[JsonPropertyName("id")]
	public Guid Id { get; init; }

	[JsonPropertyName("name")]
	public string Name { get; }

	[JsonPropertyName("metadata")]
	public Dictionary<string, object>? Metadata { get; init; }

	[JsonPropertyName("tenant")]
	public string? Tenant { get; init; }

	[JsonPropertyName("database")]
	public string? Database { get; init; }

	// The configuration as the server sends it: Chroma 0.4.10 to 0.5.3 send none.
	[JsonPropertyName("configuration_json")]
	public JsonElement? ConfigurationJson { get; init; }

	// From the "hnsw:space" metadata, which every tested Chroma sends back, or from "hnsw.space" of the configuration,
	// which Chroma 1.0.6 and later send. Null when neither is there: Chroma 0.5.4 to 1.0.5 send "hnsw_configuration.space",
	// which says "l2" also for the collections that use another space.
	[JsonIgnore]
	public ChromaSpace? Space
		=> Metadata is not null && Metadata.TryGetValue(ChromaSpaceNames.MetadataKey, out var space) && space is string name
			? ChromaSpaceNames.FromName(name)
			: ConfigurationJson is { ValueKind: JsonValueKind.Object } configuration
				&& configuration.TryGetProperty("hnsw", out var hnsw) && hnsw.ValueKind == JsonValueKind.Object
				&& hnsw.TryGetProperty("space", out var hnswSpace) && hnswSpace.ValueKind == JsonValueKind.String
				? ChromaSpaceNames.FromName(hnswSpace.GetString())
				: null;

	public ChromaCollection(string name)
	{
		Name = name;
	}
}
