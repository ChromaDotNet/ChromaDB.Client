namespace ChromaDB.Client.Models;

/// <summary>
/// The settings of a new collection. A new setting of Chroma becomes a new property here, without changing the methods.
/// </summary>
public class ChromaCollectionConfiguration
{
	/// <summary>
	/// The distance function of the collection; <c>L2</c> is the default of Chroma. The client sends it as the
	/// <c>hnsw:space</c> metadata, which every tested Chroma applies; with <c>Spann</c>, in the SPANN settings, and with a schema, in
	/// the schema.
	/// </summary>
	public ChromaSpace? Space { get; init; }

	/// <summary>
	/// The settings of the HNSW index, the index of a single Chroma server. The client sends them as the <c>hnsw:</c> metadata, which
	/// every tested Chroma applies, or in the schema when there is one. Chroma Cloud ignores them, and Chroma rejects them together with
	/// <c>Spann</c>: then it throws.
	/// </summary>
	public ChromaHnswConfiguration? Hnsw { get; init; }

	/// <summary>
	/// The settings of the SPANN index, the index of Chroma Cloud, in the <c>configuration</c> of the request, which Chroma 1.0.0 and
	/// later take, or in the schema for the settings that only a schema takes. A single server ignores them, and Chroma rejects them
	/// together with <c>Hnsw</c>: then it throws.
	/// </summary>
	public ChromaSpannConfiguration? Spann { get; init; }

	/// <summary>
	/// The embedding function the collection declares, so that the clients of Chroma that know it compute the embeddings, in the
	/// <c>configuration</c> of the request, which Chroma 1.0.0 and later take, or in the schema when there is one. The client only declares it.
	/// </summary>
	public ChromaEmbeddingFunctionReference? EmbeddingFunction { get; init; }
}
