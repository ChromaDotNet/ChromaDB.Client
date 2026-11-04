using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models.Requests;

internal class AddFunctionInputRequest
{
	[JsonPropertyName("input_collection_id")]
	public required Guid InputCollectionId { get; init; }
}
