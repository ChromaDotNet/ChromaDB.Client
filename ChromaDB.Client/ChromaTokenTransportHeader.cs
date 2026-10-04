namespace ChromaDB.Client;

/// <summary>
/// The header that carries the token, like <c>chroma_auth_token_transport_header</c> in the Python client.
/// </summary>
public enum ChromaTokenTransportHeader
{
	/// <summary>
	/// <c>X-Chroma-Token: &lt;token&gt;</c>, the header of Chroma Cloud. The default.
	/// </summary>
	XChromaToken,
	/// <summary>
	/// <c>Authorization: Bearer &lt;token&gt;</c>, the default of the token authentication of the Chroma 0.x servers.
	/// </summary>
	Authorization,
}
