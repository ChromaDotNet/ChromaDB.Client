namespace ChromaDB.Client.Common;

internal static class ChromaSpaceNames
{
	public const string MetadataKey = "hnsw:space";

	public static string ToName(ChromaSpace space) => space switch
	{
		ChromaSpace.L2 => "l2",
		ChromaSpace.Cosine => "cosine",
		ChromaSpace.InnerProduct => "ip",
		_ => throw new ArgumentOutOfRangeException(nameof(space)),
	};

	public static ChromaSpace? FromName(string? name) => name switch
	{
		"l2" => ChromaSpace.L2,
		"cosine" => ChromaSpace.Cosine,
		"ip" => ChromaSpace.InnerProduct,
		_ => null,
	};
}
