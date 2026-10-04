namespace ChromaDB.Client;

// How the client reads the values of the metadata of collections and records.
public enum ChromaMetadataValues
{
	// A string that looks like a date becomes a DateTime, and a list stays a JsonElement, as in the earlier versions.
	Inferred,
	// A string stays a string, and a list becomes a List<object> of string, long, double and bool, like the single values.
	Exact,
}
