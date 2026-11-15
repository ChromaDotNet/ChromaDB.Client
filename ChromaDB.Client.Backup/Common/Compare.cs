using System.Collections;
using System.Text.Json;
using ChromaDB.Client.Models;

namespace ChromaDB.Client.Backup.Common;

// The comparisons of the copy command of the CLI of VecturaDB (rust/cli/src/commands/copy.rs): of a record (difference,
// same_embedding) and of a schema (chosen_settings, has_spann, other_dimensions).
internal static class Compare
{
	// The tolerances of np.allclose, with which the tests of Chroma take an embedding read back as the one written: HNSW with the
	// cosine space keeps every embedding normalized, and gives back their product, whose last bit can change.
	private const double RelativeTolerance = 1e-5;
	private const double AbsoluteTolerance = 1e-8;

	// What of the record of the backup is not the same on the server: null when nothing.
	public static string? Difference(BackupRecord backup, ChromaCollectionEntry server)
	{
		if (backup.Document != server.Document)
		{
			return "document";
		}
		if (!SameEmbedding(backup.Embedding, server.Embedding))
		{
			return "embedding";
		}
		if (!SameMetadata(backup.Metadata, server.Metadata))
		{
			return "metadata";
		}
		if (backup.Uri != server.Uri)
		{
			return "uri";
		}
		return null;
	}

	public static bool SameEmbedding(float[]? backup, ReadOnlyMemory<float>? server)
	{
		if (backup is null || server is null)
		{
			return backup is null && (server is null || HasNaN(server.Value.Span));
		}
		var read = server.Value.Span;
		if (backup.Length != read.Length)
		{
			return false;
		}
		for (var i = 0; i < backup.Length; i++)
		{
			double written = backup[i], back = read[i];
			if (!(Math.Abs(back - written) <= AbsoluteTolerance + RelativeTolerance * Math.Abs(written)))
			{
				return false;
			}
		}
		return true;
	}

	// The metadata exactly the same; none and empty are the same.
	public static bool SameMetadata(IReadOnlyDictionary<string, object>? backup, IReadOnlyDictionary<string, object>? server)
	{
		var a = backup is { Count: > 0 } ? backup : null;
		var b = server is { Count: > 0 } ? server : null;
		if (a is null || b is null)
		{
			return a is null && b is null;
		}
		return a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out var other) && SameValue(pair.Value, other));
	}

	public static bool SameValue(object? a, object? b)
		=> (a, b) switch
		{
			(null, null) => true,
			(null, _) or (_, null) => false,
			(bool x, bool y) => x == y,
			(string x, string y) => x == y,
			(long x, long y) => x == y,
			(double x, double y) => x.Equals(y),
			(ChromaSparseVector x, ChromaSparseVector y) => x.Indices.SequenceEqual(y.Indices) && x.Values.SequenceEqual(y.Values)
				&& (x.Tokens is null ? y.Tokens is null : y.Tokens is not null && x.Tokens.SequenceEqual(y.Tokens)),
			(JsonElement x, JsonElement y) => SameJson(x, y),
			(string, _) or (_, string) => false,
			(IEnumerable x, IEnumerable y) => SameList(x, y),
			_ => Equals(a, b),
		};

	// The same JSON: objects whatever the order of their properties, numbers by their value.
	public static bool SameJson(JsonElement a, JsonElement b)
	{
		if (a.ValueKind != b.ValueKind)
		{
			return false;
		}
		switch (a.ValueKind)
		{
			case JsonValueKind.Object:
				var properties = a.EnumerateObject().ToList();
				if (properties.Count != b.EnumerateObject().Count())
				{
					return false;
				}
				return properties.All(property => b.TryGetProperty(property.Name, out var other) && SameJson(property.Value, other));
			case JsonValueKind.Array:
				if (a.GetArrayLength() != b.GetArrayLength())
				{
					return false;
				}
				return a.EnumerateArray().Zip(b.EnumerateArray(), SameJson).All(x => x);
			case JsonValueKind.Number:
				return a.TryGetDecimal(out var x) && b.TryGetDecimal(out var y) ? x == y : a.GetDouble().Equals(b.GetDouble());
			case JsonValueKind.String:
				return a.GetString() == b.GetString();
			default:
				return true;
		}
	}

	// Whether the schemas say the same, as chosen_settings of the copy command: a key that the records brought in, with the settings of the
	// defaults, does not count, nor do the settings of HNSW and SPANN when the two sides have different vector indexes. The copy command
	// leaves them out when the target does not hold SPANN, as Chroma 1.5.9, and compares no schema on Chroma Cloud; the client of a
	// restore does not know which server it has, so the settings go out whenever one side has SPANN and the other does not.
	public static bool SameSchema(JsonElement? backup, JsonElement? server)
	{
		if (backup is not { ValueKind: JsonValueKind.Object } a || server is not { ValueKind: JsonValueKind.Object } b)
		{
			return true;
		}
		var withIndexSettings = HasSpann(a) == HasSpann(b);
		return SameJson(Chosen(a, withIndexSettings), Chosen(b, withIndexSettings));
	}

	public static bool OtherDimensions(int? backup, int? server) => backup is { } a && server is { } b && a != b;

	private static bool HasSpann(JsonElement schema)
		=> ValueTypes(schema).Any(types => VectorIndexConfig(types) is { } config && config.TryGetProperty("spann", out var spann) && spann.ValueKind == JsonValueKind.Object);

	private static IEnumerable<JsonElement> ValueTypes(JsonElement schema)
	{
		if (schema.TryGetProperty("defaults", out var defaults) && defaults.ValueKind == JsonValueKind.Object)
		{
			yield return defaults;
		}
		if (schema.TryGetProperty("keys", out var keys) && keys.ValueKind == JsonValueKind.Object)
		{
			foreach (var key in keys.EnumerateObject())
			{
				yield return key.Value;
			}
		}
	}

	private static JsonElement? VectorIndexConfig(JsonElement types)
		=> types.ValueKind == JsonValueKind.Object && types.TryGetProperty("float_list", out var list) && list.ValueKind == JsonValueKind.Object
			&& list.TryGetProperty("vector_index", out var index) && index.ValueKind == JsonValueKind.Object
			&& index.TryGetProperty("config", out var config) && config.ValueKind == JsonValueKind.Object
				? config
				: null;

	// The schema with what someone chose: without the value types of a key that are the same as the defaults, without the keys left
	// empty, and, without withIndexSettings, without the settings of HNSW and SPANN.
	private static JsonElement Chosen(JsonElement schema, bool withIndexSettings)
	{
		using var stream = new MemoryStream();
		using (var writer = new Utf8JsonWriter(stream))
		{
			var defaults = schema.TryGetProperty("defaults", out var d) && d.ValueKind == JsonValueKind.Object ? d : (JsonElement?)null;
			writer.WriteStartObject();
			foreach (var property in schema.EnumerateObject())
			{
				writer.WritePropertyName(property.Name);
				if (property.Name == "defaults" && property.Value.ValueKind == JsonValueKind.Object)
				{
					WriteTypes(writer, property.Value, null, withIndexSettings);
				}
				else if (property.Name == "keys" && property.Value.ValueKind == JsonValueKind.Object)
				{
					writer.WriteStartObject();
					foreach (var key in property.Value.EnumerateObject())
					{
						if (key.Value.ValueKind != JsonValueKind.Object || key.Value.EnumerateObject().Any(type => !IsDefault(type, defaults)))
						{
							writer.WritePropertyName(key.Name);
							WriteTypes(writer, key.Value, defaults, withIndexSettings);
						}
					}
					writer.WriteEndObject();
				}
				else
				{
					property.Value.WriteTo(writer);
				}
			}
			writer.WriteEndObject();
		}
		return JsonDocument.Parse(stream.ToArray()).RootElement.Clone();
	}

	private static bool IsDefault(JsonProperty type, JsonElement? defaults)
		=> type.Value.ValueKind == JsonValueKind.Null
			|| defaults is { } d && d.TryGetProperty(type.Name, out var value) && SameJson(type.Value, value);

	private static void WriteTypes(Utf8JsonWriter writer, JsonElement types, JsonElement? defaults, bool withIndexSettings)
	{
		if (types.ValueKind != JsonValueKind.Object)
		{
			types.WriteTo(writer);
			return;
		}
		writer.WriteStartObject();
		foreach (var type in types.EnumerateObject())
		{
			if (defaults is not null && IsDefault(type, defaults))
			{
				continue;
			}
			writer.WritePropertyName(type.Name);
			if (!withIndexSettings && type.Name == "float_list" && VectorIndexConfig(types) is not null)
			{
				WriteWithout(writer, type.Value, ["vector_index", "config"], ["hnsw", "spann"]);
			}
			else
			{
				type.Value.WriteTo(writer);
			}
		}
		writer.WriteEndObject();
	}

	// The element without the given properties of the object at the path.
	private static void WriteWithout(Utf8JsonWriter writer, JsonElement element, string[] path, string[] names)
	{
		writer.WriteStartObject();
		foreach (var property in element.EnumerateObject())
		{
			if (path.Length == 0 && names.Contains(property.Name))
			{
				continue;
			}
			writer.WritePropertyName(property.Name);
			if (path.Length > 0 && property.Name == path[0] && property.Value.ValueKind == JsonValueKind.Object)
			{
				WriteWithout(writer, property.Value, path.Skip(1).ToArray(), names);
			}
			else
			{
				property.Value.WriteTo(writer);
			}
		}
		writer.WriteEndObject();
	}

	private static bool SameList(IEnumerable a, IEnumerable b)
	{
		var x = a.Cast<object?>().ToList();
		var y = b.Cast<object?>().ToList();
		return x.Count == y.Count && x.Zip(y, SameValue).All(same => same);
	}

	private static bool HasNaN(ReadOnlySpan<float> values)
	{
		foreach (var value in values)
		{
			if (float.IsNaN(value))
			{
				return true;
			}
		}
		return false;
	}
}
