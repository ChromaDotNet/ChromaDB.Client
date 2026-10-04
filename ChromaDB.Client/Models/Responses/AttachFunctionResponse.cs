using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models.Responses;

internal class AttachFunctionResponse
{
	[JsonPropertyName("attached_function")]
	public ChromaAttachedFunction AttachedFunction { get; init; } = null!;

	// False when a function with the same name was already attached: the request is idempotent.
	[JsonPropertyName("created")]
	public bool Created { get; init; }
}
