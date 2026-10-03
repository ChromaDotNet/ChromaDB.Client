namespace ChromaDB.Client;

[Flags]
public enum ChromaGetInclude
{
	None = 0,
	Embeddings = 1 << 0,
	Metadatas = 1 << 1,
	Documents = 1 << 2,
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
