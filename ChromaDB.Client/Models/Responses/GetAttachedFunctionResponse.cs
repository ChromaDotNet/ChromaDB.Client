using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models.Responses;

internal class GetAttachedFunctionResponse
{
	[JsonPropertyName("attached_function")]
	public ChromaAttachedFunction AttachedFunction { get; init; } = null!;
}
