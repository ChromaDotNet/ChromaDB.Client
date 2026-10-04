namespace ChromaDB.Client;

/// <summary>
/// How the client reads the values of the metadata of collections and records.
/// </summary>
public enum ChromaMetadataValues
{
	/// <summary>
	/// A string that looks like a date becomes a <c>DateTime</c>, and a list stays a <c>JsonElement</c>, as in the earlier versions.
	/// The default.
	/// </summary>
	Inferred,
	/// <summary>
	/// A string stays a string, and a list becomes a <c>List&lt;object&gt;</c> of <c>string</c>, <c>long</c>, <c>double</c>
	/// and <c>bool</c>, like the single values.
	/// </summary>
	Exact,
}
