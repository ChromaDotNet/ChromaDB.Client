using ChromaDB.Client.Common;

namespace ChromaDB.Client.Models;

/// <summary>
/// A collection to create: its name, metadata and configuration.
/// </summary>
public class ChromaCollectionDefinition
{
	/// <summary>
	/// The name of the collection.
	/// </summary>
	public string Name { get; }
	/// <summary>
	/// The metadata of the collection.
	/// </summary>
	public IReadOnlyDictionary<string, object>? Metadata { get; init; }
	/// <summary>
	/// The settings of the collection, like its space. The client sends the space as the <c>hnsw:space</c> metadata,
	/// which every tested Chroma applies.
	/// </summary>
	public ChromaCollectionConfiguration? Configuration { get; init; }

	/// <summary>
	/// The indexes of the keys of the collection, like a sparse vector index for BM25. Chroma 1.3.0 and later apply a schema, but
	/// sparse vector indexes only on Chroma Cloud: a single server rejects them.
	/// </summary>
	public ChromaCollectionSchema? Schema { get; init; }

	/// <summary>
	/// Creates the definition of a collection with the name.
	/// </summary>
	public ChromaCollectionDefinition(string name)
	{
		Name = name;
	}

	// The space goes in the "hnsw:space" metadata: every tested Chroma applies it from there, while the configuration
	// field of the request is ignored by 0.4.10 to 0.5.3 and fails on 0.5.4 to 0.6.3. With a schema it goes in the schema:
	// Chroma rejects the two together ("Cannot set both collection config and schema simultaneously").
	internal Dictionary<string, object>? ToRequestSchema()
		=> Schema?.ToSchema(Configuration?.Space);

	internal IReadOnlyDictionary<string, object>? ToRequestMetadata()
	{
		if (Configuration?.Space is not { } space || Schema is not null)
		{
			return Metadata;
		}
		var value = ChromaSpaceNames.ToName(space);
		if (Metadata is not null && Metadata.TryGetValue(ChromaSpaceNames.MetadataKey, out var existing) && !Equals(existing, value))
		{
			throw new ArgumentException($"The metadata sets {ChromaSpaceNames.MetadataKey} to '{existing}', the configuration to '{value}'.", nameof(Metadata));
		}
		var metadata = Metadata?.ToDictionary(x => x.Key, x => x.Value) ?? [];
		metadata[ChromaSpaceNames.MetadataKey] = value;
		return metadata;
	}
}
