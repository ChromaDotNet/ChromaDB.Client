namespace ChromaDB.Client;

/// <summary>
/// How <c>UpdateAsync</c> and <c>UpsertAsync</c> write the embeddings of records that exist. Chroma 1.0.21 to 1.5.9, installed on your
/// own servers, may lose a record from the vector index after an update or an upsert with embeddings of records that exist, also with
/// the embeddings they had: a query no longer finds it, while a get does (KD-49 in docs/COMPATIBILITY.md). Chroma Cloud has not shown
/// it, and on Chroma 0.x, which has not the defect, the writes go as they are whatever the strategy.
/// </summary>
public enum ChromaUpsertStrategy
{
	/// <summary>
	/// The update and the upsert of the server, as they are: the default.
	/// </summary>
	Server,

	/// <summary>
	/// A record whose embedding does not change is updated without it, which does not touch the vector index; the other records go by
	/// the update or the upsert of the server. No record is deleted, so there is no risk of data loss, but a record whose embedding
	/// changes can still be lost by the vector index. It costs a get of the records first.
	/// </summary>
	SkipUnchangedEmbeddings,
}
