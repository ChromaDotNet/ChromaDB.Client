using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models;

/// <summary>
/// What the commit of a conditional transaction wrote.
/// </summary>
public class ChromaConditionalCommitResult
{
	/// <summary>
	/// The position in the log of the first record that the commit wrote; null when it wrote none, as for a transaction without writes.
	/// </summary>
	[JsonPropertyName("first_inserted_record_offset")]
	public long? FirstInsertedRecordOffset { get; init; }

	/// <summary>
	/// How many records the commit wrote.
	/// </summary>
	[JsonPropertyName("record_count")]
	public int RecordCount { get; init; }
}
