namespace ChromaDB.Client;

/// <summary>
/// The default quotas of a tenant on Chroma Cloud, as <see href="https://docs.trychroma.com/cloud/quotas-limits">its documentation</see>
/// lists them; a tenant can ask for higher ones. Beyond a quota Chroma Cloud answers <c>422</c> with <c>Quota exceeded</c>. A single
/// Chroma server has none of them.
/// </summary>
public static class ChromaCloudQuotas
{
	/// <summary>
	/// The most records in a write, and in the results of a read: the batch size of the client on Chroma Cloud.
	/// </summary>
	public static readonly int MaxRecordsPerRequest = 300;

	/// <summary>
	/// The most bytes of a value in the metadata of a record, in UTF-8.
	/// </summary>
	public static readonly int MaxMetadataValueBytes = 8182;

	/// <summary>
	/// The most bytes of a document, in UTF-8.
	/// </summary>
	public static readonly int MaxDocumentBytes = 16384;

	/// <summary>
	/// The most keys in the metadata of a record.
	/// </summary>
	public static readonly int MaxMetadataKeys = 32;

	/// <summary>
	/// The most bytes of a metadata key, in UTF-8.
	/// </summary>
	public static readonly int MaxMetadataKeyBytes = 36;

	/// <summary>
	/// The most predicates in a <c>where</c> filter, nested ones included; the values of <c>$in</c> do not count.
	/// </summary>
	public static readonly int MaxWherePredicates = 8;

	/// <summary>
	/// The most bytes of the text of a full-text or regular expression filter on the documents, in UTF-8.
	/// </summary>
	public static readonly int MaxDocumentFilterBytes = 256;

	/// <summary>
	/// The most bytes of the id of a record, in UTF-8.
	/// </summary>
	public static readonly int MaxIdBytes = 128;

	/// <summary>
	/// The most bytes of the URI of a record, in UTF-8.
	/// </summary>
	public static readonly int MaxUriBytes = 256;

	/// <summary>
	/// The most dimensions of an embedding.
	/// </summary>
	public static readonly int MaxEmbeddingDimensions = 4096;
}
