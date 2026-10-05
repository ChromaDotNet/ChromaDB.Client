using System.Text.Json.Serialization;
using ChromaDB.Client.Common;

namespace ChromaDB.Client.Models;

/// <summary>
/// A sparse vector: the values of some dimensions only, like the BM25 or SPLADE embedding of a text. It is a metadata value of a record,
/// written as <c>{"#type": "sparse_vector", "indices": [...], "values": [...]}</c> as the Python client of Chroma writes it, and the query
/// of <c>ChromaRank.SparseKnn</c> on a key with a sparse vector index, which only Chroma Cloud searches.
/// </summary>
[JsonConverter(typeof(ChromaSparseVectorConverter))]
public sealed class ChromaSparseVector
{
	/// <summary>
	/// The vector with the values of the dimensions in <c>indices</c>, which must be in strictly ascending order, as many as the values;
	/// the tokens, when given, too. The indices are unsigned 32-bit integers, as Chroma stores them.
	/// </summary>
	/// <param name="indices">The dimensions that have a value, in strictly ascending order.</param>
	/// <param name="values">The value of each dimension of the indices.</param>
	/// <param name="tokens">The token of each dimension, or null for none.</param>
	public ChromaSparseVector(IReadOnlyList<uint> indices, IReadOnlyList<float> values, IReadOnlyList<string>? tokens = null)
	{
		if (indices.Count != values.Count)
		{
			throw new ArgumentException($"A sparse vector needs as many indices as values: {indices.Count} indices and {values.Count} values.", nameof(values));
		}
		if (tokens is not null && tokens.Count != indices.Count)
		{
			throw new ArgumentException($"A sparse vector needs as many tokens as indices: {tokens.Count} tokens and {indices.Count} indices.", nameof(tokens));
		}
		for (var i = 0; i < indices.Count; i++)
		{
			if (i > 0 && indices[i] <= indices[i - 1])
			{
				throw new ArgumentException($"The indices of a sparse vector must be in strictly ascending order: {indices[i]} after {indices[i - 1]} at position {i}.", nameof(indices));
			}
		}
		// Read-only copies: a cast cannot change them after the checks.
		Indices = Array.AsReadOnly(indices.ToArray());
		Values = Array.AsReadOnly(values.ToArray());
		Tokens = tokens is null ? null : Array.AsReadOnly(tokens.ToArray());
	}

	/// <summary>
	/// The dimensions that have a value, in ascending order.
	/// </summary>
	public IReadOnlyList<uint> Indices { get; }

	/// <summary>
	/// The value of each dimension in <c>Indices</c>.
	/// </summary>
	public IReadOnlyList<float> Values { get; }

	/// <summary>
	/// The token of each dimension, like the words of a BM25 vector, when known; <c>tokens</c> in the JSON.
	/// </summary>
	public IReadOnlyList<string>? Tokens { get; }

	/// <summary>
	/// The JSON of the vector, as the client sends it.
	/// </summary>
	/// <returns>The JSON.</returns>
	public override string ToString()
		=> System.Text.Json.JsonSerializer.Serialize(this, HttpClientHelpers.TypeInfo<ChromaSparseVector>(HttpClientHelpers.PostJsonSerializerOptions));
}
