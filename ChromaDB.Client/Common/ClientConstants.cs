using ChromaDB.Client.Models;

namespace ChromaDB.Client.Common;

internal static class ClientConstants
{
	public const string DefaultTenantName = "default_tenant";
	public const string DefaultDatabaseName = "default_database";
	// The address of the server: the client adds the path of the API version.
	public const string DefaultUri = "http://localhost:8000";
	public const string ChromaTokenHeader = "X-Chroma-Token";

	public static ChromaTenant DefaultTenant { get; } = new(DefaultTenantName);
	public static ChromaDatabase DefaultDatabase { get; } = new(DefaultDatabaseName);
}
