namespace ChromaDB.Client;

// The header that carries the token, like chroma_auth_token_transport_header in the Python client.
public enum ChromaTokenTransportHeader
{
	// X-Chroma-Token: <token>, the header of Chroma Cloud.
	XChromaToken,
	// Authorization: Bearer <token>, the default of the token authentication of the Chroma 0.x servers.
	Authorization,
}
