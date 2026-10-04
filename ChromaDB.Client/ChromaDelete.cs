namespace ChromaDB.Client.Models;

// The records to delete: the ones with the ids, the ones the filters match, or both. A new field of Chroma becomes a new property here.
public class ChromaDelete
{
	public List<string>? Ids { get; init; }
	public ChromaWhereOperator? Where { get; init; }
	public ChromaWhereDocumentOperator? WhereDocument { get; init; }
	// At most this many records. Chroma 1.5.3 and later apply it; the earlier versions ignore it and delete every matching record,
	// so on them the client throws a ChromaException before sending the request.
	public int? Limit { get; init; }
}
