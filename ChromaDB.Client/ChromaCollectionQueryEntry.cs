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
	/// The metadata of the record, when the query included the metadatas.
	/// </summary>
	public Dictionary<string, object>? Metadata { get; init; }
	/// <summary>
	/// The embedding of the record, when the query included the embeddings.
	/// </summary>
	public ReadOnlyMemory<float>? Embeddings { get; init; }
	/// <summary>
	/// The document of the record, when the query included the documents.
	/// </summary>
	public string? Document { get; init; }
	/// <summary>
	/// The URI of the record. Null when the query did not include the URIs.
	/// </summary>
	public string? Uri { get; init; }
	/// <summary>
	/// The URI of the record in a list of one, or null without it. Obsolete: a record has one URI, use <c>Uri</c>.
	/// </summary>
	[Obsolete("A record has one URI: use Uri.")]
	public List<string?>? Uris { get; init; }
	/// <summary>
	/// The <c>data</c> field of the answer of the server, the same for every result of the answer.
	/// </summary>
	public dynamic? Data { get; init; }

	/// <summary>
	/// Creates the entry of the record with the id.
	/// </summary>
	public ChromaCollectionQueryEntry(string id)
	{
		Id = id;
	}
}
