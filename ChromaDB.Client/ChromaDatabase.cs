using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models;

/// <summary>
/// A database of Chroma, in a tenant.
/// </summary>
public class ChromaDatabase
{
	/// <summary>
	/// The id of the database.
	/// </summary>
	[JsonPropertyName("id")]
	public Guid Id { get; init; }

	/// <summary>
	/// The name of the database.
	/// </summary>
	[JsonPropertyName("name")]
	public string Name { get; }

	/// <summary>
	/// The tenant of the database.
	/// </summary>
	[JsonPropertyName("tenant")]
	public string? Tenant { get; init; }

	/// <summary>
	/// Creates the object for a database with the given name; it sends no request.
	/// </summary>
	public ChromaDatabase(string name)
	{
		Name = name;
	}
}
