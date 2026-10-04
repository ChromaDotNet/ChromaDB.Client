namespace ChromaDB.Client.Models;

/// <summary>
/// One search of the Search API of Chroma, which only Chroma Cloud serves. The filters combine with <c>$and</c>; without
/// <c>Rank</c> the records come in the order of the server. A new field of Chroma becomes a new property here.
/// </summary>
public class ChromaSearch
{
	/// <summary>
	/// A filter on the metadata; with the keys of <c>ChromaSearchKeys</c> it also filters ids and documents, like
	/// <c>ChromaWhereOperator.Contains(ChromaSearchKeys.Document, "apple")</c>.
	/// </summary>
	public ChromaWhereOperator? Where { get; init; }

	/// <summary>
	/// A filter on the documents, sent on the <c>#document</c> key.
	/// </summary>
	public ChromaWhereDocumentOperator? WhereDocument { get; init; }

	/// <summary>
	/// Only the records with these ids, sent as <c>$in</c> on the <c>#id</c> key; not empty.
	/// </summary>
	public List<string>? Ids { get; init; }

	/// <summary>
	/// How the records are ranked: the lowest score first.
	/// </summary>
	public ChromaRank? Rank { get; init; }

	/// <summary>
	/// The number of records to return; when null, the server decides. Positive.
	/// </summary>
	public int? Limit { get; init; }

	/// <summary>
	/// The number of records to skip. Not negative.
	/// </summary>
	public int Offset { get; init; }

	/// <summary>
	/// The fields to return besides the id: the keys of <c>ChromaSearchKeys</c>, like <c>ChromaSearchKeys.Document</c> and
	/// <c>ChromaSearchKeys.Score</c>, and metadata fields by name. When null or empty, only the ids.
	/// </summary>
	public List<string>? Select { get; init; }

	/// <summary>
	/// Groups the results by metadata keys and keeps some records in each group.
	/// </summary>
	public ChromaSearchGroupBy? GroupBy { get; init; }
}
