using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models;

// The user the server sees for the credentials of the client, with its tenant and databases.
public class ChromaUserIdentity
{
	[JsonPropertyName("user_id")]
	public string? UserId { get; init; }

	[JsonPropertyName("tenant")]
	public string? Tenant { get; init; }

	[JsonPropertyName("databases")]
	public List<string>? Databases { get; init; }
}
