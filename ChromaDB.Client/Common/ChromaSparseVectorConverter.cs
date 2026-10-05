using System.Text.Json;
using System.Text.Json.Serialization;
using ChromaDB.Client.Models;

namespace ChromaDB.Client.Common;

// {"#type": "sparse_vector", "indices": [...], "values": [...], "tokens": [...]}: the server takes it also without "#type", and always
// sends it with it. Written by hand, so trimming and NativeAOT keep what it needs.
internal sealed class ChromaSparseVectorConverter : JsonConverter<ChromaSparseVector>
{
	public const string TypeKey = "#type";
	public const string TypeValue = "sparse_vector";

	public override ChromaSparseVector Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		=> FromJson(JsonDocument.ParseValue(ref reader).RootElement);

	// Whether the object is a sparse vector, with "#type": "sparse_vector", without reading its values.
	public static bool IsTagged(JsonElement element)
		=> element.ValueKind == JsonValueKind.Object && element.TryGetProperty(TypeKey, out var type) && type.ValueKind == JsonValueKind.String && type.ValueEquals(TypeValue);

	// Null when the object is not a sparse vector.
	public static ChromaSparseVector? FromTaggedJson(JsonElement element)
		=> IsTagged(element) ? FromJson(element) : null;

	static ChromaSparseVector FromJson(JsonElement element)
	{
		if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("indices", out var indices) || !element.TryGetProperty("values", out var values))
		{
			throw new JsonException("A sparse vector needs indices and values.");
		}
		var tokens = element.TryGetProperty("tokens", out var t) && t.ValueKind == JsonValueKind.Array
			? t.EnumerateArray().Select(x => x.GetString()!).ToList()
			: null;
		return new ChromaSparseVector(
			indices.EnumerateArray().Select(x => x.GetUInt32()).ToList(),
			values.EnumerateArray().Select(x => x.GetSingle()).ToList(),
			tokens);
	}

	public override void Write(Utf8JsonWriter writer, ChromaSparseVector value, JsonSerializerOptions options)
	{
		writer.WriteStartObject();
		writer.WriteString(TypeKey, TypeValue);
		writer.WriteStartArray("indices");
		foreach (var index in value.Indices)
		{
			writer.WriteNumberValue(index);
		}
		writer.WriteEndArray();
		writer.WriteStartArray("values");
		foreach (var v in value.Values)
		{
			writer.WriteRawValue(ChromaNumbers.Format(v), skipInputValidation: true);
		}
		writer.WriteEndArray();
		if (value.Tokens is { } tokens)
		{
			writer.WriteStartArray("tokens");
			foreach (var token in tokens)
			{
				writer.WriteStringValue(token);
			}
			writer.WriteEndArray();
		}
		writer.WriteEndObject();
	}
}
