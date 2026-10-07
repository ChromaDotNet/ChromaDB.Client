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
	/// The settings of the collection: its space, the settings of its vector index and its embedding function. The space and the HNSW
	/// settings go as the <c>hnsw:</c> metadata, which every tested Chroma applies; the SPANN settings and the embedding function in the
	/// configuration of the request; with a schema, all of them in the schema.
	/// </summary>
	public ChromaCollectionConfiguration? Configuration { get; init; }

	/// <summary>
	/// The indexes of the keys of the collection, like a sparse vector index for BM25. Chroma 1.3.0 and later apply a schema, but
	/// sparse vector indexes only on Chroma Cloud: a single server rejects them, and does not get the ones added with <c>ifSupported</c>.
	/// </summary>
	public ChromaCollectionSchema? Schema { get; init; }

	/// <summary>
	/// Creates the definition of a collection with the name.
	/// </summary>
	/// <param name="name">The name of the collection.</param>
	public ChromaCollectionDefinition(string name)
	{
		Name = name;
	}

	// Where the settings go. With a schema, or with SPANN settings that only a schema takes, they all go in the schema: Chroma rejects
	// a configuration together with a schema ("Cannot set both collection config and schema simultaneously"). Otherwise the space and the
	// HNSW settings go in the "hnsw:" metadata, which every tested Chroma applies, while the configuration field of the request is ignored
	// by 0.4.10 to 0.5.3 and fails on 0.5.4 to 0.6.3; the SPANN settings and the embedding function, which only the configuration takes
	// besides a schema, go in the configuration, with the space in the SPANN settings: Chroma ignores the "hnsw:space" metadata of a
	// request with SPANN settings.
	internal bool SettingsInSchema => Schema is not null || Configuration?.Spann?.HasSchemaOnlySettings == true;

	internal bool SettingsInConfiguration => !SettingsInSchema && (Configuration?.Spann is not null || Configuration?.EmbeddingFunction is not null);

	// Chroma rejects the two indexes together: "Multiple vector index configurations provided".
	internal void Validate()
	{
		if (Configuration is { Hnsw: not null, Spann: not null })
		{
			throw new ArgumentException("A collection has one vector index: set Hnsw for a single Chroma server or Spann for Chroma Cloud, not both, as Chroma rejects them together.", nameof(Configuration));
		}
		ChromaHnswConfiguration.CheckMaxNeighbors(Configuration?.Hnsw?.MaxNeighbors, nameof(Configuration));
		ChromaHnswConfiguration.CheckMaxNeighbors(IntegerSettings()?.TryGetValue("hnsw:M", out var m) == true ? m : null, nameof(Metadata));
		// Chroma ignores the hnsw:space metadata next to SPANN settings, but ChromaCollection.Space would read it back.
		if (Configuration?.Spann is not null && !SettingsInSchema && Metadata is not null && Metadata.ContainsKey(ChromaSpaceNames.MetadataKey))
		{
			throw new ArgumentException($"Chroma ignores the {ChromaSpaceNames.MetadataKey} metadata next to SPANN settings: set the space in Configuration.Space instead.", nameof(Metadata));
		}
	}

	// The definition as the server gets it, without the indexes added with ifSupported that it does not take: all of them on a server
	// other than Chroma Cloud, and on Chroma Cloud the ones whose key is beyond its quota of bytes.
	internal ChromaCollectionDefinition ForServer(bool chromaCloud)
	{
		var schema = Schema?.WithoutIndexesIfSupported(key => !chromaCloud || System.Text.Encoding.UTF8.GetByteCount(key) > ChromaCloudQuotas.MaxMetadataKeyBytes);
		return schema == Schema ? this : new(Name) { Metadata = Metadata, Configuration = Configuration, Schema = schema };
	}

	internal Dictionary<string, object>? ToRequestSchema()
		=> SettingsInSchema ? (Schema ?? new ChromaCollectionSchema()).ToSchema(Configuration) : null;

	internal Dictionary<string, object>? ToRequestConfiguration()
	{
		if (!SettingsInConfiguration)
		{
			return null;
		}
		var configuration = new Dictionary<string, object>();
		if (Configuration!.EmbeddingFunction is { } embeddingFunction)
		{
			configuration["embedding_function"] = embeddingFunction.ToJson();
		}
		if (Configuration.Spann is { } spann)
		{
			var settings = spann.ToJson();
			if (Configuration.Space is { } space)
			{
				settings["space"] = ChromaSpaceNames.ToName(space);
			}
			configuration["spann"] = settings;
		}
		return configuration;
	}

	internal IReadOnlyDictionary<string, object>? ToRequestMetadata()
	{
		Common.ChromaRequestChecks.NoLists(Metadata, nameof(Metadata));
		var integerSettings = IntegerSettings();
		if (SettingsInSchema)
		{
			return integerSettings;
		}
		var settings = new List<KeyValuePair<string, object>>();
		// Works around KD-44 (docs/COMPATIBILITY.md)
		if (Configuration?.Space is { } space && Configuration.Spann is null)
		{
			settings.Add(new(ChromaSpaceNames.MetadataKey, ChromaSpaceNames.ToName(space)));
		}
		if (Configuration?.Hnsw is { } hnsw)
		{
			settings.AddRange(hnsw.ToMetadata());
		}
		if (settings.Count == 0)
		{
			return integerSettings;
		}
		var metadata = integerSettings?.ToDictionary(x => x.Key, x => x.Value) ?? [];
		foreach (var setting in settings)
		{
			if (metadata.TryGetValue(setting.Key, out var existing) && !SameValue(existing, setting.Value))
			{
				throw new ArgumentException($"The metadata sets {setting.Key} to '{existing}', the configuration to '{setting.Value}'.", nameof(Metadata));
			}
			metadata[setting.Key] = setting.Value;
		}
		return metadata;
	}

	// The metadata with the "hnsw:" settings that Chroma takes only as integers written as integers.
	private IReadOnlyDictionary<string, object>? IntegerSettings()
		=> Metadata is not null && ChromaHnswConfiguration.IntegerMetadataKeys.Any(Metadata.ContainsKey)
			? Metadata.ToDictionary(x => x.Key, x => ChromaHnswConfiguration.IntegerMetadataKeys.Contains(x.Key) ? ChromaHnswConfiguration.IntegerMetadata(x.Key, x.Value, nameof(Metadata)) : x.Value)
			: Metadata;

	// 2 and 2L, or 1.5f and 1.5, are the same setting, also in a JsonElement.
	private static bool SameValue(object existing, object value)
	{
		var scalar = ChromaRequestChecks.Scalar(existing);
		return Equals(scalar, value)
			|| scalar is IConvertible && value is IConvertible && scalar is not string && value is not string
				&& Convert.ToDouble(scalar, System.Globalization.CultureInfo.InvariantCulture) == Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
	}
}
