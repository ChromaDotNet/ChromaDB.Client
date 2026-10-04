using ChromaDB.Client.Common;

namespace ChromaDB.Client.Models;

// A collection to create: its name, metadata and configuration.
public class ChromaCollectionDefinition
{
	public string Name { get; }
	public Dictionary<string, object>? Metadata { get; init; }
	public ChromaCollectionConfiguration? Configuration { get; init; }

	public ChromaCollectionDefinition(string name)
	{
		Name = name;
	}

	// The space goes in the "hnsw:space" metadata: every tested Chroma applies it from there, while the configuration
	// field of the request is ignored by 0.4.10 to 0.5.3 and fails on 0.5.4 to 0.6.3.
	internal Dictionary<string, object>? ToRequestMetadata()
	{
		if (Configuration?.Space is not { } space)
		{
			return Metadata;
		}
		var value = ChromaSpaceNames.ToName(space);
		if (Metadata is not null && Metadata.TryGetValue(ChromaSpaceNames.MetadataKey, out var existing) && !Equals(existing, value))
		{
			throw new ArgumentException($"The metadata sets {ChromaSpaceNames.MetadataKey} to '{existing}', the configuration to '{value}'.", nameof(Metadata));
		}
		return new Dictionary<string, object>(Metadata ?? []) { [ChromaSpaceNames.MetadataKey] = value };
	}
}
