using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models.Requests;

internal class ForkCollectionRequest
{
	[JsonPropertyName("new_name")]
	public required string NewName { get; init; }
}
