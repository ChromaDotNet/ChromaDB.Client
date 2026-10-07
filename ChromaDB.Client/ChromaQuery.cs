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
	public int NResults
	{
		get => _nResults;
		init => _nResults = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(NResults), value, "The number of results of a query cannot be negative.");
	}

	private int _nResults = 10;
	/// <summary>
	/// How many of the nearest records to skip for each query embedding before the results, 0 by default. Chroma has no offset in
	/// queries: the client asks for <c>NResults</c> plus the offset, and leaves out the first ones.
	/// </summary>
	public int Offset
	{
		get => _offset;
		init => _offset = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(Offset), value, "The offset of a query cannot be negative.");
	}
	private readonly int _offset;
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
	/// The space the distances of the results are expected in: <c>QueryAsync</c> throws an <c>InvalidOperationException</c>, before
	/// the query, when the collection has another one. A collection whose space the server does not report is taken. Null for any.
	/// </summary>
	public ChromaSpace? ExpectedSpace { get; init; }

	/// <summary>
	/// Creates a query for the embeddings.
	/// </summary>
	/// <param name="queryEmbeddings">The query embeddings: the results are one list for each.</param>
	public ChromaQuery(IReadOnlyList<ReadOnlyMemory<float>> queryEmbeddings)
	{
		QueryEmbeddings = queryEmbeddings;
	}
}
