using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models.Requests;

internal class AttachFunctionRequest
{
	[JsonPropertyName("name")]
	public required string Name { get; init; }

	// The name of the function, like "statistics".
	[JsonPropertyName("function_id")]
	public required string FunctionId { get; init; }

	[JsonPropertyName("output_collection")]
	public required string OutputCollection { get; init; }

	[JsonPropertyName("params")]
	public Dictionary<string, object>? Params { get; init; }
}
