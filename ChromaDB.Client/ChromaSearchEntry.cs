namespace ChromaDB.Client.Models;

/// <summary>
/// A record found by a search, with the fields that <c>ChromaSearch.Select</c> asked for; the others are null.
/// </summary>
public class ChromaSearchEntry
{
	/// <summary>
	/// Creates the entry for the record with the given id; it sends no request.
	/// </summary>
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
	/// </summary>
	public ReadOnlyMemory<float>? Embedding { get; init; }

	/// <summary>
	/// The metadata: all of it with <c>ChromaSearchKeys.Metadata</c>, or only the selected fields.
	/// </summary>
	public Dictionary<string, object>? Metadata { get; init; }

	/// <summary>
	/// The score of the ranking, when selected: the lower, the better.
	/// </summary>
	public float? Score { get; init; }
}
