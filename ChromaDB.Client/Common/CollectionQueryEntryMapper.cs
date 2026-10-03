using ChromaDB.Client.Models;
using ChromaDB.Client.Models.Responses;

namespace ChromaDB.Client.Common;

internal static class CollectionQueryEntryMapper
{
	public static List<List<ChromaCollectionQueryEntry>> Map(this CollectionEntriesQueryResponse response)
	{
		return response.Ids
			.Select((_, i) => response.Ids[i]
				.Select((id, j) => new ChromaCollectionQueryEntry(id)
				{
					Distance = response.Distances?[i][j],
					Metadata = response.Metadatas?[i][j],
					Embeddings = response.Embeddings?[i][j],
					Document = response.Documents?[i][j],
					Uri = response.Uris?[i][j],
#pragma warning disable CS0618 // Kept filled for the code that still reads it.
					Uris = response.Uris?[i][j] is { } uri ? [uri] : null,
#pragma warning restore CS0618
					Data = response.Data,
				})
				.ToList())
			.ToList();
	}
}
