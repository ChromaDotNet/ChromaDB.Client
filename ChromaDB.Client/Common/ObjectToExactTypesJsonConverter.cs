using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChromaDB.Client.Common;

// Reads the values of metadata as they are: strings stay strings, and lists become lists of the same types as the single values.
internal class ObjectToExactTypesJsonConverter : JsonConverter<object>
{
	public override object Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		=> ReadValue(ref reader)!;

	static object? ReadValue(ref Utf8JsonReader reader)
	{
		switch (reader.TokenType)
		{
			case JsonTokenType.True:
				return true;
			case JsonTokenType.False:
				return false;
			case JsonTokenType.Number when reader.TryGetInt64(out var l):
				return l;
			case JsonTokenType.Number:
				return reader.GetDouble();
			case JsonTokenType.String:
				return reader.GetString()!;
			case JsonTokenType.Null:
				return null;
			case JsonTokenType.StartArray:
				var list = new List<object>();
				while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
				{
					list.Add(ReadValue(ref reader)!);
				}
				return list;
			default:
				return JsonDocument.ParseValue(ref reader).RootElement.Clone();
		}
	}

	public override void Write(Utf8JsonWriter writer, object objectToWrite, JsonSerializerOptions options) => throw new NotSupportedException();
}
