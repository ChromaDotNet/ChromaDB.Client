namespace ChromaDB.Client.Models;

/// <summary>
/// A record returned by a get.
/// </summary>
public class ChromaCollectionEntry
{
	/// <summary>
	/// The id of the record.
	/// </summary>
	public string Id { get; }
	/// <summary>
	/// The embedding of the record, when the get included the embeddings.
	/// </summary>
	public ReadOnlyMemory<float>? Embedding { get; init; }
	/// <summary>
	/// The metadata of the record, when the get included the metadatas.
	/// </summary>
	public IReadOnlyDictionary<string, object>? Metadata { get; init; }
	/// <summary>
	/// The document of the record, when the get included the documents.
	/// </summary>
	public string? Document { get; init; }
	/// <summary>
	/// The URI of the record. Null when the get did not include the URIs.
	/// </summary>
	public string? Uri { get; init; }

	/// <summary>
	/// Creates the entry of the record with the id.
	/// </summary>
	/// <param name="id">The id of the record.</param>
	public ChromaCollectionEntry(string id)
	{
		Id = id;
	}
}
