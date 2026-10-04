using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models.Requests;

internal class DetachFunctionRequest
{
	[JsonPropertyName("delete_output")]
	public bool DeleteOutput { get; init; }
}
