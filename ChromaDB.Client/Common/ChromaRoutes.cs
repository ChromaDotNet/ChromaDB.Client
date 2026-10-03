namespace ChromaDB.Client.Common;

// The request paths of each version of the Chroma API, relative to the base URI.
internal sealed class ChromaRoutes
{
	public static ChromaRoutes V2 { get; } = new()
	{
		Heartbeat = "heartbeat",
		Version = "version",
		Reset = "reset",
		Tenants = "tenants",
		Tenant = "tenants/{tenant}",
		Databases = "tenants/{tenant}/databases",
		Database = "tenants/{tenant}/databases/{database}",
		Collections = "tenants/{tenant}/databases/{database}/collections",
		CollectionByName = "tenants/{tenant}/databases/{database}/collections/{collectionName}",
		CollectionsCount = "tenants/{tenant}/databases/{database}/collections_count",
		Collection = "tenants/{tenant}/databases/{database}/collections/{collection_id}",
	};

	// In the v1 API the tenant and the database are query parameters, and the requests on a collection need only its id.
	public static ChromaRoutes V1 { get; } = new()
	{
		Heartbeat = "heartbeat",
		Version = "version",
		Reset = "reset",
		Tenants = "tenants",
		Tenant = "tenants/{tenant}",
		Databases = "databases?tenant={tenant}",
		Database = "databases/{database}?tenant={tenant}",
		Collections = "collections?tenant={tenant}&database={database}",
		CollectionByName = "collections/{collectionName}?tenant={tenant}&database={database}",
		CollectionsCount = "count_collections?tenant={tenant}&database={database}",
		Collection = "collections/{collection_id}",
	};

	public required string Heartbeat { get; init; }
	public required string Version { get; init; }
	public required string Reset { get; init; }
	public required string Tenants { get; init; }
	public required string Tenant { get; init; }
	public required string Databases { get; init; }
	public required string Database { get; init; }
	public required string Collections { get; init; }
	public required string CollectionByName { get; init; }
	public required string CollectionsCount { get; init; }
	public required string Collection { get; init; }
}
