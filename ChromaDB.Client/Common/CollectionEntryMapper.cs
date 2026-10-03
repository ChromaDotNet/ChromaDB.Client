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
				Embeddings = response.Embeddings?[i],
				Metadata = response.Metadatas?[i],
				Document = response.Documents?[i],
				Uri = response.Uris?[i],
#pragma warning disable CS0618 // Kept filled for the code that still reads it.
				Uris = response.Uris?[i] is { } uri ? [uri] : null,
#pragma warning restore CS0618
				Data = response.Data,
			})
			.ToList();
	}
}
