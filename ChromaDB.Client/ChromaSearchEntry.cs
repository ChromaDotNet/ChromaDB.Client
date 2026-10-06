namespace ChromaDB.Client.Models;

/// <summary>
/// A record found by a search, with the fields that <c>ChromaSearch.Select</c> asked for; the others are null.
/// </summary>
public class ChromaSearchEntry
{
	/// <summary>
	/// Creates the entry for the record with the given id; it sends no request.
	/// </summary>
	/// <param name="id">The id of the record.</param>
	public ChromaSearchEntry(string id)
	{
		Id = id;
	}

	/// <summary>
	/// The id of the record.
	/// </summary>
	public string Id { get; }

	/// <summary>
	/// The document, when selected.
	/// </summary>
	public string? Document { get; init; }

	/// <summary>
	/// The embedding, when selected.
	/// A value is <c>NaN</c> where Chroma sent <c>null</c>: Chroma 1.x does for a float it cannot write, like an embedding beyond
	/// the range of a float in a collection with the <c>cosine</c> space.
	/// </summary>
	public ReadOnlyMemory<float>? Embedding { get; init; }

	/// <summary>
	/// The metadata: all of it with <c>ChromaSearchKeys.Metadata</c>, or only the selected fields.
	/// </summary>
	public IReadOnlyDictionary<string, object>? Metadata { get; init; }

	/// <summary>
	/// The score of the ranking, when selected: the lower, the better; for a search ranked by <c>ChromaRank.HybridRrf</c>, the fused
	/// score, the higher, the better.
	/// </summary>
	public float? Score { get; init; }
}
