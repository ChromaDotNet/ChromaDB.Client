using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models.Requests;

internal class UpdateTenantRequest
{
	[JsonPropertyName("resource_name")]
	public required string ResourceName { get; init; }
}
