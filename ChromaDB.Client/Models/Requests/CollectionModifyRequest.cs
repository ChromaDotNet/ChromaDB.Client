using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models.Requests;

internal class CollectionModifyRequest
{
	[JsonPropertyName("new_name")]
	public string? Name { get; init; }

	[JsonPropertyName("new_metadata")]
	public Dictionary<string, object>? Metadata { get; init; }

	// Sent only when set: the request stays as before for the servers that do not know it.
	[JsonPropertyName("new_configuration")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public ChromaCollectionConfigurationUpdate? Configuration { get; init; }
}
