using ChromaDB.Client.Models;
using ChromaDB.Client.Models.Responses;

namespace ChromaDB.Client.Common;

internal static class CollectionEntryMapper
{
	public static List<ChromaCollectionEntry> Map(this CollectionEntriesGetResponse response)
	{
		return response.Ids
			.Select((id, i) => new ChromaCollectionEntry(id)
			{
				Embedding = response.Embeddings?[i],
				Metadata = response.Metadatas?[i],
				Document = response.Documents?[i],
				Uri = response.Uris?[i],
				Data = response.Data,
			})
			.ToList();
	}
}
