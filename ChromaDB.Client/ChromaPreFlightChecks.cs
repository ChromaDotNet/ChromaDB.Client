using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models;

/// <summary>
/// The limits of the server, to check before sending large requests.
/// </summary>
public class ChromaPreFlightChecks
{
	/// <summary>
	/// The most records a single add, update, upsert or delete can carry.
	/// </summary>
	[JsonPropertyName("max_batch_size")]
	public int MaxBatchSize { get; init; }

	/// <summary>
	/// Whether the server takes embeddings encoded in base64, which <c>AddAsync</c>, <c>UpdateAsync</c> and <c>UpsertAsync</c> then send.
	/// Null when the server does not send it, like Chroma 1.0.12 and earlier.
	/// </summary>
	[JsonPropertyName("supports_base64_encoding")]
	public bool? SupportsBase64Encoding { get; init; }

	/// <summary>
	/// The server and the features it declares, like VecturaDB does. Null when the server does not send it, like Chroma and Chroma Cloud.
	/// </summary>
	[JsonPropertyName("server")]
	public ChromaPreFlightServer? Server { get; init; }
}

/// <summary>
/// The server block of <c>pre-flight-checks</c>: the name and the version of the server, and the features it declares.
/// </summary>
public class ChromaPreFlightServer
{
	/// <summary>
	/// The name of the server, like <c>VecturaDB</c>.
	/// </summary>
	[JsonPropertyName("name")]
	public string? Name { get; init; }

	/// <summary>
	/// The version of the server, as it sends it.
	/// </summary>
	[JsonPropertyName("version")]
	public string? Version { get; init; }

	/// <summary>
	/// The features the server declares, like <c>sparse_vector_index</c>: with it, the indexes added with <c>ifSupported</c> go to the server.
	/// </summary>
	[JsonPropertyName("features")]
	public IReadOnlyList<string>? Features { get; init; }
}
