using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models.Requests;

internal class CollectionModifyRequest
{
	[JsonPropertyName("new_name")]
	public string? Name { get; init; }

	[JsonPropertyName("new_metadata")]
	public Dictionary<string, object>? Metadata { get; init; }
}
