namespace ChromaDB.Client;

// The functions of Chroma Cloud that can be attached to a collection, as the Python client of Chroma names them.
public static class ChromaFunctions
{
	// Counts how often each metadata value occurs.
	public const string Statistics = "statistics";
	// Counts the records.
	public const string RecordCounter = "record_counter";
}
