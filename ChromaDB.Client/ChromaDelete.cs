namespace ChromaDB.Client.Models;

/// <summary>
/// The records to delete: the ones with the ids, the ones the filters match, or both. A new field of Chroma becomes a
/// new property here.
/// </summary>
public class ChromaDelete
{
	/// <summary>
	/// The ids of the records to delete. Null to delete by the filters only; not empty.
	/// </summary>
	public List<string>? Ids { get; init; }
	/// <summary>
	/// The filter on the metadata.
	/// </summary>
	public ChromaWhereOperator? Where { get; init; }
	/// <summary>
	/// The filter on the documents.
	/// </summary>
	public ChromaWhereDocumentOperator? WhereDocument { get; init; }
	/// <summary>
	/// At most this many records, with a where or where document filter. Chroma 1.5.3 and later apply it; the earlier
	/// versions ignore it and delete every matching record, so on them the client throws a <c>ChromaException</c>
	/// before sending the request.
	/// </summary>
	public int? Limit { get; init; }
}
