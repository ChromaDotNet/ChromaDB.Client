using ChromaDB.Client.Models;
using ChromaDB.Client.Models.Responses;

namespace ChromaDB.Client.Common;

internal static class CollectionEntryMapper
{
	public static List<ChromaCollectionEntry> Map(this CollectionEntriesGetResponse response, DocumentCopyReader copy = default)
	{
		return response.Ids
			.Select((id, i) => new ChromaCollectionEntry(id)
			{
				Embedding = response.Embeddings?[i],
				Metadata = copy.Metadata(response.Metadatas?[i]),
				Document = copy.Document(response.Documents?[i], response.Metadatas?[i]),
				Uri = response.Uris?[i],
			})
			.ToList();
	}
}
