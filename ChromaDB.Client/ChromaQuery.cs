namespace ChromaDB.Client.Models;

// The query to run. A new field of Chroma becomes a new property here, without changing the methods.
public class ChromaQuery
{
	public List<ReadOnlyMemory<float>> QueryEmbeddings { get; }
	public int NResults { get; init; } = 10;
	public ChromaWhereOperator? Where { get; init; }
	public ChromaWhereDocumentOperator? WhereDocument { get; init; }
	public ChromaQueryInclude? Include { get; init; }
	// Searches only the records with these ids. Chroma 0.x ignores them: then Query throws a ChromaException.
	public List<string>? Ids { get; init; }

	public ChromaQuery(List<ReadOnlyMemory<float>> queryEmbeddings)
	{
		QueryEmbeddings = queryEmbeddings;
	}
}
