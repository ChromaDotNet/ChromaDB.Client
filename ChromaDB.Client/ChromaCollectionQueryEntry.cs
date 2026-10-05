namespace ChromaDB.Client.Models;

/// <summary>
/// A result of a query: a record and its distance from the query embedding.
/// </summary>
public class ChromaCollectionQueryEntry
{
	/// <summary>
	/// The id of the record.
	/// </summary>
	public string Id { get; }
	/// <summary>
	/// The distance of the record from the query embedding. Null when the query did not include
	/// <c>ChromaQueryInclude.Distances</c>.
	/// </summary>
	public float? Distance { get; init; }
	/// <summary>
	/// The metadata of the record, when the query included the metadatas. Null also for a record without metadata keys, as every
	/// tested Chroma returns it.
	/// </summary>
	public IReadOnlyDictionary<string, object>? Metadata { get; init; }
	/// <summary>
	/// The embedding of the record, when the query included the embeddings.
	/// </summary>
	public ReadOnlyMemory<float>? Embedding { get; init; }
	/// <summary>
	/// The document of the record, when the query included the documents.
	/// </summary>
	public string? Document { get; init; }
	/// <summary>
	/// The URI of the record. Null when the query did not include the URIs.
	/// </summary>
	public string? Uri { get; init; }

	/// <summary>
	/// Creates the entry of the record with the id.
	/// </summary>
	/// <param name="id">The id of the record.</param>
	public ChromaCollectionQueryEntry(string id)
	{
		Id = id;
	}
}
