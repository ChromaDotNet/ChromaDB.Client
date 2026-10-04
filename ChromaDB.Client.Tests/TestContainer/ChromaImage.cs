using System.Text.RegularExpressions;

namespace ChromaDB.Client.Tests.TestContainer;

// The Chroma image the tests run against, from CHROMA_IMAGE, and its version, which tells the tests what the server supports.
public static class ChromaImage
{
	public static readonly string Name = Environment.GetEnvironmentVariable("CHROMA_IMAGE") is { Length: > 0 } image ? image : "chromadb/chroma:0.6.3";
	public static readonly System.Version Version = ParseVersion(Name);

	// "latest" is newer than any release; other tags start with the version, like "1.5.9" or "1.5.10.dev312".
	static System.Version ParseVersion(string image)
	{
		var tag = image.Substring(image.LastIndexOf(':') + 1);
		if (tag == "latest")
			return new System.Version(int.MaxValue, 0);
		var match = Regex.Match(tag, @"^\d+\.\d+(\.\d+)?");
		return match.Success
			? System.Version.Parse(match.Value)
			: throw new InvalidOperationException($"Cannot read the Chroma version from '{image}'.");
	}
}
