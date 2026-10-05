namespace ChromaDB.Client.Models;

/// <summary>
/// The query to run. A new field of Chroma becomes a new property here, without changing the methods.
/// </summary>
public class ChromaQuery
{
	/// <summary>
	/// The embeddings to search for; the results come in one list per embedding.
	/// </summary>
	public IReadOnlyList<ReadOnlyMemory<float>> QueryEmbeddings { get; }
	/// <summary>
	/// How many results to return for each query embedding, 10 by default.
	/// </summary>
	public int NResults { get; init; } = 10;
	/// <summary>
	/// The filter on the metadata.
	/// </summary>
	public ChromaWhereOperator? Where { get; init; }
	/// <summary>
	/// The filter on the documents.
	/// </summary>
	public ChromaWhereDocumentOperator? WhereDocument { get; init; }
	/// <summary>
	/// What the results include. When null, the metadatas, the documents and the distances.
	/// </summary>
	public ChromaQueryInclude? Include { get; init; }
	/// <summary>
	/// Searches only the records with these ids. Chroma 0.x ignores them: then <c>QueryAsync</c> throws a
	/// <c>ChromaException</c>.
	/// </summary>
	public IReadOnlyList<string>? Ids { get; init; }

	/// <summary>
	/// Creates a query for the embeddings.
	/// </summary>
	/// <param name="queryEmbeddings">The query embeddings: the results are one list for each.</param>
	public ChromaQuery(IReadOnlyList<ReadOnlyMemory<float>> queryEmbeddings)
	{
		QueryEmbeddings = queryEmbeddings;
	}
}
