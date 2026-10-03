using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models.Responses;

internal class GeneralError
{
	[JsonPropertyName("error")]
	public string? Error { get; init; }

	[JsonPropertyName("message")]
	public string? Message { get; init; }

	[JsonPropertyName("detail")]
	public string? Detail { get; init; }
}
