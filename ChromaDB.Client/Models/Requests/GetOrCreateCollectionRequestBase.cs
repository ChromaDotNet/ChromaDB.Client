using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models.Requests;

internal abstract class GetOrCreateCollectionRequestBase
{
	[JsonPropertyName("name")]
	public required string Name { get; init; }

	[JsonPropertyName("metadata")]
	public Dictionary<string, object>? Metadata { get; init; }

	// Public, so that the generated serialization sees it.
	[JsonPropertyName("get_or_create")]
	public abstract bool GetOrCreate { get; }
}
