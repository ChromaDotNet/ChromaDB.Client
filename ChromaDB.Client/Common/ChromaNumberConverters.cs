using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChromaDB.Client.Common;

// The numbers of the requests, in the text of ChromaNumbers: the same on every build, and floats stay floats; and the
// numbers of the answers, -0.0 with its sign on .NET Framework too.
internal sealed class ChromaDoubleConverter : JsonConverter<double>
{
	public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => ChromaNumbers.ReadDouble(ref reader);

	public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
		=> writer.WriteRawValue(ChromaNumbers.Format(value), skipInputValidation: true);
}

internal sealed class ChromaFloatConverter : JsonConverter<float>
{
	public override float Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => ChromaNumbers.ReadSingle(ref reader);

	public override void Write(Utf8JsonWriter writer, float value, JsonSerializerOptions options)
		=> writer.WriteRawValue(ChromaNumbers.Format(value), skipInputValidation: true);
}

// A decimal as its own digits, which are exact on every runtime, with ".0" when it is whole: Chroma keeps it a float.
internal sealed class ChromaDecimalConverter : JsonConverter<decimal>
{
	public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetDecimal();

	public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
	{
		var text = value.ToString(CultureInfo.InvariantCulture);
		writer.WriteRawValue(text.IndexOf('.') < 0 ? text + ".0" : text, skipInputValidation: true);
	}
}
