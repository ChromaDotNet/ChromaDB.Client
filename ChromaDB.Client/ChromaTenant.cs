using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models;

public class ChromaTenant
{
	[JsonPropertyName("name")]
	public string Name { get; }

	// The name of the tenant in the resource names of Chroma Cloud, like the CRN of a collection; null when it has none.
	[JsonPropertyName("resource_name")]
	public string? ResourceName { get; init; }

	public ChromaTenant(string name)
	{
		Name = name;
	}
}
