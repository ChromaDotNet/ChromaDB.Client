using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models;

// The limits of the server, to check before sending large requests.
public class ChromaPreFlightChecks
{
	// The most records a single add, update, upsert or delete can carry.
	[JsonPropertyName("max_batch_size")]
	public int MaxBatchSize { get; init; }

	// Null when the server does not send it, like Chroma 1.0.12 and earlier.
	[JsonPropertyName("supports_base64_encoding")]
	public bool? SupportsBase64Encoding { get; init; }
}
