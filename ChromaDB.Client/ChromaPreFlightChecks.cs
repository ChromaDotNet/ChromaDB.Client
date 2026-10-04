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
	/// Whether the server takes embeddings encoded in base64, which <c>Add</c>, <c>Update</c> and <c>Upsert</c> then send.
	/// Null when the server does not send it, like Chroma 1.0.12 and earlier.
	/// </summary>
	[JsonPropertyName("supports_base64_encoding")]
	public bool? SupportsBase64Encoding { get; init; }
}
