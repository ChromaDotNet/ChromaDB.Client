namespace ChromaDB.Client;

/// <summary>
/// The functions of distributed Chroma, the one Chroma Cloud runs on, that can be attached to a collection, as the Python client of
/// Chroma names them.
/// </summary>
public static class ChromaFunctions
{
	/// <summary>
	/// Counts how often each metadata value occurs.
	/// </summary>
	public const string Statistics = "statistics";
	/// <summary>
	/// Counts the records.
	/// </summary>
	public const string RecordCounter = "record_counter";
}
