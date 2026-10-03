namespace ChromaDB.Client;

// The version of the Chroma API the client uses, for all its requests.
public enum ChromaApiVersion
{
	// Chroma 0.5.16 and later.
	V2,
	// The only API of Chroma 0.5.15 and earlier; Chroma 0.5.16 to 0.5.20 also serve it.
	V1,
}
