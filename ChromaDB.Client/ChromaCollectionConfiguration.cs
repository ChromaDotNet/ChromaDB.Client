namespace ChromaDB.Client.Models;

// The settings of a new collection. A new setting of Chroma becomes a new property here, without changing the methods.
public class ChromaCollectionConfiguration
{
	public ChromaSpace? Space { get; init; }
}
