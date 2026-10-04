namespace ChromaDB.Client;

/// <summary>
/// What a query returns besides the ids of the results; the values can be combined.
/// </summary>
[Flags]
public enum ChromaQueryInclude
{
	/// <summary>
	/// Nothing besides the ids.
	/// </summary>
	None = 0,
	/// <summary>
	/// The embeddings of the results.
	/// </summary>
	Embeddings = 1 << 0,
	/// <summary>
	/// The metadatas of the results.
	/// </summary>
	Metadatas = 1 << 1,
	/// <summary>
	/// The documents of the results.
	/// </summary>
	Documents = 1 << 2,
	/// <summary>
	/// The distances of the results from the query embedding.
	/// </summary>
	Distances = 1 << 3,
	/// <summary>
	/// The URIs of the results.
	/// </summary>
	Uris = 1 << 4,
}

internal static class ChromaQueryIncludeExt
{
	public static List<string> ToInclude(this ChromaQueryInclude include)
	{
		var result = new List<string>();
		if (include.HasFlag(ChromaQueryInclude.Embeddings))
		{
			result.Add("embeddings");
		}
		if (include.HasFlag(ChromaQueryInclude.Metadatas))
		{
			result.Add("metadatas");
		}
		if (include.HasFlag(ChromaQueryInclude.Documents))
		{
			result.Add("documents");
		}
		if (include.HasFlag(ChromaQueryInclude.Distances))
		{
			result.Add("distances");
		}
		if (include.HasFlag(ChromaQueryInclude.Uris))
		{
			result.Add("uris");
		}
		return result;
	}
}
