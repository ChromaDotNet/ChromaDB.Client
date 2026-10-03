using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models.Requests;

internal class CreateTenantRequest
{
	[JsonPropertyName("name")]
	public required string Name { get; init; }
}
