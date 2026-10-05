using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChromaDB.Client.Common;

// The embeddings of an add, update or upsert: lists of numbers, or, where the server declares it, base64 strings of
// their float32 values in little-endian order, about half the size. Queries always send numbers.
[JsonConverter(typeof(ChromaEmbeddingsConverter))]
internal sealed class ChromaEmbeddings(IReadOnlyList<ReadOnlyMemory<float>> items, bool base64)
{
	public IReadOnlyList<ReadOnlyMemory<float>> Items { get; } = items;
	public bool Base64 { get; } = base64;
}

internal sealed class ChromaEmbeddingsConverter : JsonConverter<ChromaEmbeddings>
{
	public override ChromaEmbeddings Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotSupportedException();

	public override void Write(Utf8JsonWriter writer, ChromaEmbeddings value, JsonSerializerOptions options)
	{
		writer.WriteStartArray();
		foreach (var embedding in value.Items)
		{
			if (value.Base64)
			{
				writer.WriteBase64StringValue(LittleEndianBytes(embedding.Span));
			}
			else
			{
				writer.WriteStartArray();
				foreach (var number in embedding.Span)
				{
					writer.WriteRawValue(ChromaNumbers.Format(number), skipInputValidation: true);
				}
				writer.WriteEndArray();
			}
		}
		writer.WriteEndArray();
	}

	private static ReadOnlySpan<byte> LittleEndianBytes(ReadOnlySpan<float> embedding)
	{
		if (BitConverter.IsLittleEndian)
		{
			return MemoryMarshal.AsBytes(embedding);
		}
		var bytes = new byte[embedding.Length * sizeof(float)];
		for (var i = 0; i < embedding.Length; i++)
		{
			var number = BitConverter.GetBytes(embedding[i]);
			Array.Reverse(number);
			number.CopyTo(bytes, i * sizeof(float));
		}
		return bytes;
	}
}
