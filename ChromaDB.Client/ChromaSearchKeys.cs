namespace ChromaDB.Client;

/// <summary>
/// The special keys of the Search API of Chroma, for <c>ChromaSearch.Select</c>, filters and <c>ChromaRank.Knn</c>; any other key is a
/// metadata field.
/// </summary>
public static class ChromaSearchKeys
{
	/// <summary>
	/// The id of the record, <c>#id</c>.
	/// </summary>
	public const string Id = "#id";

	/// <summary>
	/// The document of the record, <c>#document</c>.
	/// </summary>
	public const string Document = "#document";

	/// <summary>
	/// The embedding of the record, <c>#embedding</c>.
	/// </summary>
	public const string Embedding = "#embedding";

	/// <summary>
	/// All the metadata of the record, <c>#metadata</c>.
	/// </summary>
	public const string Metadata = "#metadata";

	/// <summary>
	/// The score of the record in the ranking, <c>#score</c>.
	/// </summary>
	public const string Score = "#score";
}
