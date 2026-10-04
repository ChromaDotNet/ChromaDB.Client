using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models.Responses;

internal class DetachFunctionResponse
{
	[JsonPropertyName("success")]
	public bool Success { get; init; }
}
