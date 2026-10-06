namespace ChromaDB.Client.Common;

// How a client with a document copy key reads the documents: an empty document without its copy in the metadata is one deleted with
// NullDocumentsDelete, which the client wrote empty as Chroma cannot delete a document. The metadata, read for it, goes away from the
// results that did not ask for it. The default reads the documents and the metadata as they are.
internal readonly struct DocumentCopyReader
{
	private readonly string? _key;
	private readonly bool _dropMetadata;

	public DocumentCopyReader(string key, bool keepMetadata)
	{
		_key = key;
		_dropMetadata = !keepMetadata;
	}

	// Whether the documents need the metadata.
	public bool ReadsMetadata => _key is not null;

	public string? Document(string? document, IReadOnlyDictionary<string, object>? metadata)
		=> _key is not null && document is "" && metadata?.ContainsKey(_key) != true ? null : document;

	public IReadOnlyDictionary<string, object>? Metadata(IReadOnlyDictionary<string, object>? metadata)
		=> _dropMetadata ? null : metadata;
}
