namespace ChromaDB.Client;

/// <summary>
/// What a get returns besides the ids of the records; the values can be combined.
/// </summary>
[Flags]
public enum ChromaGetInclude
{
	/// <summary>
	/// Nothing besides the ids.
	/// </summary>
	None = 0,
	/// <summary>
	/// The embeddings of the records.
	/// </summary>
	Embeddings = 1 << 0,
	/// <summary>
	/// The metadatas of the records.
	/// </summary>
	Metadatas = 1 << 1,
	/// <summary>
	/// The documents of the records.
	/// </summary>
	Documents = 1 << 2,
	/// <summary>
	/// The URIs of the records.
	/// </summary>
	Uris = 1 << 3,
}

internal static class ChromaGetIncludeExt
{
	public static List<string> ToInclude(this ChromaGetInclude include)
	{
		var result = new List<string>();
		if (include.HasFlag(ChromaGetInclude.Embeddings))
		{
			result.Add("embeddings");
		}
		if (include.HasFlag(ChromaGetInclude.Metadatas))
		{
			result.Add("metadatas");
		}
		if (include.HasFlag(ChromaGetInclude.Documents))
		{
			result.Add("documents");
		}
		if (include.HasFlag(ChromaGetInclude.Uris))
		{
			result.Add("uris");
		}
		return result;
	}
}
