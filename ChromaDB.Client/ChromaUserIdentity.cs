using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models;

/// <summary>
/// The user the server sees for the credentials of the client, with its tenant and databases.
/// </summary>
public class ChromaUserIdentity
{
	/// <summary>
	/// The id of the user.
	/// </summary>
	[JsonPropertyName("user_id")]
	public string? UserId { get; init; }

	/// <summary>
	/// The tenant of the user.
	/// </summary>
	[JsonPropertyName("tenant")]
	public string? Tenant { get; init; }

	/// <summary>
	/// The databases of the user.
	/// </summary>
	[JsonPropertyName("databases")]
	public List<string>? Databases { get; init; }
}
