using System.Collections;
using System.Text.Encodings.Web;
using System.Text.Json;
using ChromaDB.Client.Models;

namespace ChromaDB.Client.Backup.Common;

// A record of a backup, as a line of records.jsonl reads back.
internal sealed record BackupRecord(string Id, float[]? Embedding, Dictionary<string, object>? Metadata, string? Document, string? Uri);

// The JSON of the backup: a record as a line of records.jsonl, with the names of the lines of Chroma Data Pipes (id, metadata,
// embedding, text_chunk) and the uri; the values of the metadata in the JSON of Chroma, so that an integer stays 1 and a float 1.0,
// a list a list, and a sparse vector {"#type":"sparse_vector",...}; the embeddings as the numbers that read back as the same floats.
internal static class RecordJson
{
	public static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, SkipValidation = false };

	public static readonly JsonDocumentOptions DocumentOptions = new() { MaxDepth = 64 };

	// Writes the record; false when the server sent an embedding that has no float, NaN where Chroma sends null, which goes as null.
	public static bool Write(Utf8JsonWriter writer, ChromaCollectionEntry entry)
	{
		var embeddingWritten = true;
		writer.WriteStartObject();
		writer.WriteString("id", entry.Id);
		writer.WritePropertyName("metadata");
		WriteMetadata(writer, entry.Metadata is { Count: > 0 } metadata ? metadata : null);
		writer.WritePropertyName("embedding");
		if (entry.Embedding is { } embedding && AllFinite(embedding.Span))
		{
			WriteFloats(writer, embedding.Span);
		}
		else
		{
			embeddingWritten = entry.Embedding is null;
			writer.WriteNullValue();
		}
		writer.WriteString("text_chunk", entry.Document);
		writer.WriteString("uri", entry.Uri);
		writer.WriteEndObject();
		return embeddingWritten;
	}

	public static BackupRecord Read(JsonElement line)
	{
		var id = line.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String
			? idElement.GetString()!
			: throw new InvalidDataException("A record of the backup has no id.");
		float[]? embedding = null;
		if (line.TryGetProperty("embedding", out var embeddingElement) && embeddingElement.ValueKind == JsonValueKind.Array)
		{
			embedding = new float[embeddingElement.GetArrayLength()];
			var i = 0;
			foreach (var value in embeddingElement.EnumerateArray())
			{
				embedding[i++] = value.GetSingle();
			}
		}
		var metadata = line.TryGetProperty("metadata", out var metadataElement) && metadataElement.ValueKind == JsonValueKind.Object
			? ReadMetadata(metadataElement)
			: null;
		return new(id, embedding, metadata, String(line, "text_chunk"), String(line, "uri"));
	}

	public static void WriteMetadata(Utf8JsonWriter writer, IReadOnlyDictionary<string, object>? metadata)
	{
		if (metadata is null)
		{
			writer.WriteNullValue();
			return;
		}
		writer.WriteStartObject();
		foreach (var pair in metadata)
		{
			writer.WritePropertyName(pair.Key);
			WriteValue(writer, pair.Value);
		}
		writer.WriteEndObject();
	}

	public static Dictionary<string, object> ReadMetadata(JsonElement metadata)
	{
		var values = new Dictionary<string, object>();
		foreach (var property in metadata.EnumerateObject())
		{
			if (ReadValue(property.Value) is { } value)
			{
				values[property.Name] = value;
			}
		}
		return values;
	}

	// The values that the Exact metadata values of the client read: bool, long, double, string, lists of them, sparse vectors, and
	// JsonElement for any other object; the other numbers of .NET, for metadata written by hand.
	private static void WriteValue(Utf8JsonWriter writer, object? value)
	{
		switch (value)
		{
			case null:
				writer.WriteNullValue();
				break;
			case bool flag:
				writer.WriteBooleanValue(flag);
				break;
			case string text:
				writer.WriteStringValue(text);
				break;
			case long or int or short or sbyte or byte or ushort or uint:
				writer.WriteRawValue(JsonNumbers.Integer(Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture)));
				break;
			case double number:
				WriteDouble(writer, number);
				break;
			case float number:
				WriteDouble(writer, number);
				break;
			case ChromaSparseVector sparse:
				writer.WriteStartObject();
				writer.WriteString("#type", "sparse_vector");
				writer.WriteStartArray("indices");
				foreach (var index in sparse.Indices)
				{
					writer.WriteNumberValue(index);
				}
				writer.WriteEndArray();
				writer.WritePropertyName("values");
				WriteFloats(writer, sparse.Values);
				if (sparse.Tokens is { } tokens)
				{
					writer.WriteStartArray("tokens");
					foreach (var token in tokens)
					{
						writer.WriteStringValue(token);
					}
					writer.WriteEndArray();
				}
				writer.WriteEndObject();
				break;
			case JsonElement element:
				element.WriteTo(writer);
				break;
			case IEnumerable list:
				writer.WriteStartArray();
				foreach (var item in list)
				{
					WriteValue(writer, item);
				}
				writer.WriteEndArray();
				break;
			default:
				throw new NotSupportedException($"A metadata value of type {value.GetType()} has no JSON in a backup.");
		}
	}

	private static void WriteDouble(Utf8JsonWriter writer, double number)
	{
		if (!JsonNumbers.IsFinite(number))
		{
			throw new NotSupportedException($"The metadata value {number} has no JSON.");
		}
		writer.WriteRawValue(JsonNumbers.Double(number));
	}

	private static object? ReadValue(JsonElement value)
	{
		switch (value.ValueKind)
		{
			case JsonValueKind.True:
				return true;
			case JsonValueKind.False:
				return false;
			case JsonValueKind.String:
				return value.GetString();
			case JsonValueKind.Number:
				// As the Exact metadata values of the client: an integer without a decimal point or an exponent is a long, the rest a double.
				var raw = value.GetRawText();
				return raw.IndexOfAny(['.', 'e', 'E']) < 0 && value.TryGetInt64(out var integer) ? (object)integer : value.GetDouble();
			case JsonValueKind.Array:
				var items = new List<object>();
				foreach (var item in value.EnumerateArray())
				{
					if (ReadValue(item) is { } read)
					{
						items.Add(read);
					}
				}
				return items;
			case JsonValueKind.Object when value.TryGetProperty("#type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "sparse_vector":
				var indices = value.GetProperty("indices").EnumerateArray().Select(x => x.GetUInt32()).ToList();
				var values = value.GetProperty("values").EnumerateArray().Select(x => x.GetSingle()).ToList();
				var tokens = value.TryGetProperty("tokens", out var tokensElement) && tokensElement.ValueKind == JsonValueKind.Array
					? tokensElement.EnumerateArray().Select(x => x.GetString()!).ToList()
					: null;
				return new ChromaSparseVector(indices, values, tokens);
			case JsonValueKind.Object:
				return value.Clone();
			default:
				return null;
		}
	}

	private static void WriteFloats(Utf8JsonWriter writer, IReadOnlyList<float> values)
	{
		writer.WriteStartArray();
		foreach (var value in values)
		{
			writer.WriteRawValue(JsonNumbers.Float(value));
		}
		writer.WriteEndArray();
	}

	private static void WriteFloats(Utf8JsonWriter writer, ReadOnlySpan<float> values)
	{
		writer.WriteStartArray();
		foreach (var value in values)
		{
			writer.WriteRawValue(JsonNumbers.Float(value));
		}
		writer.WriteEndArray();
	}

	private static bool AllFinite(ReadOnlySpan<float> values)
	{
		foreach (var value in values)
		{
			if (!JsonNumbers.IsFinite(value))
			{
				return false;
			}
		}
		return true;
	}

	private static string? String(JsonElement line, string name)
		=> line.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String ? element.GetString() : null;
}
