using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models.Responses;

internal class ForkCountResponse
{
	[JsonPropertyName("count")]
	public int Count { get; init; }
}
