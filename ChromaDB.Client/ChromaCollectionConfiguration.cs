namespace ChromaDB.Client.Models;

/// <summary>
/// The settings of a new collection. A new setting of Chroma becomes a new property here, without changing the methods.
/// </summary>
public class ChromaCollectionConfiguration
{
	/// <summary>
	/// The distance function of the collection; <c>L2</c> is the default of Chroma. The client sends it as the
	/// <c>hnsw:space</c> metadata.
	/// </summary>
	public ChromaSpace? Space { get; init; }
}
