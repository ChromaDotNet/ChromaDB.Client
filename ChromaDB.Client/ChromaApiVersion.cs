namespace ChromaDB.Client;

/// <summary>
/// The version of the Chroma API the client uses, for all its requests.
/// </summary>
public enum ChromaApiVersion
{
	/// <summary>
	/// The v2 API: Chroma 0.5.16 and later. The default.
	/// </summary>
	V2,
	/// <summary>
	/// The v1 API, the only API of Chroma 0.5.15 and earlier; Chroma 0.5.16 to 0.5.20 also serve it.
	/// Chroma 1.x answers it with <c>410 Gone</c>.
	/// </summary>
	V1,
}
