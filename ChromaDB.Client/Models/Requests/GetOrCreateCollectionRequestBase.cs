using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models.Requests;

internal abstract class GetOrCreateCollectionRequestBase
{
	[JsonPropertyName("name")]
	public required string Name { get; init; }

	[JsonPropertyName("metadata")]
	public IReadOnlyDictionary<string, object>? Metadata { get; init; }

	// Sent only when set: the request stays as before for the servers that do not know it.
	[JsonPropertyName("configuration")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public Dictionary<string, object>? Configuration { get; init; }

	// Sent only when set: the request stays as before for the servers that do not know it.
	[JsonPropertyName("schema")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public Dictionary<string, object>? Schema { get; init; }

	// Public, so that the generated serialization sees it.
	[JsonPropertyName("get_or_create")]
	public abstract bool GetOrCreate { get; }
}
