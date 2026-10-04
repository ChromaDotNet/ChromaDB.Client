using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models;

// The state of the server, from Chroma 1.0.0.
public class ChromaHealthcheck
{
	[JsonPropertyName("is_executor_ready")]
	public bool? IsExecutorReady { get; init; }

	[JsonPropertyName("is_log_client_ready")]
	public bool? IsLogClientReady { get; init; }
}
