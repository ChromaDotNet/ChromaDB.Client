using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models;

/// <summary>
/// The state of the server, from Chroma 1.0.0.
/// </summary>
public class ChromaHealthcheck
{
	/// <summary>
	/// Whether the executor of the server is ready.
	/// </summary>
	[JsonPropertyName("is_executor_ready")]
	public bool? IsExecutorReady { get; init; }

	/// <summary>
	/// Whether the log client of the server is ready.
	/// </summary>
	[JsonPropertyName("is_log_client_ready")]
	public bool? IsLogClientReady { get; init; }
}
