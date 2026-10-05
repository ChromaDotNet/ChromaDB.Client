using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models;

/// <summary>
/// A tenant of Chroma.
/// </summary>
public class ChromaTenant
{
	/// <summary>
	/// The name of the tenant.
	/// </summary>
	[JsonPropertyName("name")]
	public string Name { get; }

	/// <summary>
	/// The name of the tenant in the resource names of Chroma Cloud, like the CRN of a collection; null when it has none.
	/// <c>UpdateTenantAsync</c> sets it.
	/// </summary>
	[JsonPropertyName("resource_name")]
	public string? ResourceName { get; init; }

	/// <summary>
	/// Creates the object for a tenant with the given name; it sends no request.
	/// </summary>
	/// <param name="name">The name of the tenant.</param>
	public ChromaTenant(string name)
	{
		Name = name;
	}
}
