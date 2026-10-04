using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models.Responses;

internal class GeneralError
{
	[JsonPropertyName("error")]
	public string? Error { get; init; }

	[JsonPropertyName("message")]
	public string? Message { get; init; }

	// A string, or on the 0.x servers the list of the validation errors of FastAPI: [{"loc": ["body", "n_results"], "msg": "..."}].
	[JsonPropertyName("detail")]
	public JsonElement? Detail { get; init; }
}
