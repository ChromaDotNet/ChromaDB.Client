using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models;

/// <summary>
/// The answer of the server to a heartbeat.
/// </summary>
public class ChromaHeartbeat
{
	/// <summary>
	/// The value of <c>nanosecond heartbeat</c> in the answer of the server.
	/// </summary>
	[JsonPropertyName("nanosecond heartbeat")]
	public long NanosecondHeartbeat { get; set; }
}
