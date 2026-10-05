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

// A ulong above long.MaxValue: Chroma stores integers as 64-bit signed numbers, and would store it as a float.
internal sealed class ChromaUInt64Converter : JsonConverter<ulong>
{
	public override ulong Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetUInt64();

	public override void Write(Utf8JsonWriter writer, ulong value, JsonSerializerOptions options)
	{
		if (value > long.MaxValue)
		{
			throw new ArgumentOutOfRangeException(nameof(value), value, "Chroma stores integers as 64-bit signed numbers: a ulong above long.MaxValue would become a float.");
		}
		writer.WriteNumberValue(value);
	}
}

// The strings of the requests, values and keys of metadata, without a lone surrogate (ChromaRequestChecks).
internal sealed class ChromaStringConverter : JsonConverter<string>
{
	public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetString();

	public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
	{
		ChromaRequestChecks.NoLoneSurrogates(value, "A text of the request");
		writer.WriteStringValue(value);
	}

	public override string ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetString()!;

	public override void WriteAsPropertyName(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
	{
		ChromaRequestChecks.NoLoneSurrogates(value, "A key of the request");
		writer.WritePropertyName(value);
	}
}
