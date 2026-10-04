using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models.Responses;

internal class AttachFunctionResponse
{
	[JsonPropertyName("attached_function")]
	public ChromaAttachedFunction AttachedFunction { get; init; } = null!;

	// False when a function with the same name was already attached: the request is idempotent. Null when the answer does not
	// say it, which AttachFunction reads as true, like the Python client of Chroma; a default value of an init property would
	// not survive the generated deserialization, which sets every init property.
	[JsonPropertyName("created")]
	public bool? Created { get; init; }
}
