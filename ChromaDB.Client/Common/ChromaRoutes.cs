namespace ChromaDB.Client.Common;

// The request paths of each version of the Chroma API, relative to the base URI.
internal sealed class ChromaRoutes
{
	public static ChromaRoutes V2 { get; } = new()
	{
		Heartbeat = "heartbeat",
		Healthcheck = "healthcheck",
		Version = "version",
		Reset = "reset",
		UserIdentity = "auth/identity",
		PreFlightChecks = "pre-flight-checks",
		Tenants = "tenants",
		Tenant = "tenants/{tenant}",
		CollectionByCrn = "collections/{crn}",
		Databases = "tenants/{tenant}/databases",
		Database = "tenants/{tenant}/databases/{database}",
		Collections = "tenants/{tenant}/databases/{database}/collections",
		CollectionByName = "tenants/{tenant}/databases/{database}/collections/{collectionName}",
		CollectionById = "tenants/{tenant}/databases/{database}/collections/by-id/{collection_id}",
		CollectionsCount = "tenants/{tenant}/databases/{database}/collections_count",
		Collection = "tenants/{tenant}/databases/{database}/collections/{collection_id}",
	};

	// In the v1 API the tenant and the database are query parameters, and the requests on a collection need only its id.
	public static ChromaRoutes V1 { get; } = new()
	{
		Heartbeat = "heartbeat",
		Healthcheck = "healthcheck",
		Version = "version",
		Reset = "reset",
		UserIdentity = "auth/identity",
		PreFlightChecks = "pre-flight-checks",
		Tenants = "tenants",
		Tenant = "tenants/{tenant}",
		CollectionByCrn = "collections/{crn}",
		Databases = "databases?tenant={tenant}",
		Database = "databases/{database}?tenant={tenant}",
		Collections = "collections?tenant={tenant}&database={database}",
		CollectionByName = "collections/{collectionName}?tenant={tenant}&database={database}",
		CollectionById = "collections/by-id/{collection_id}?tenant={tenant}&database={database}",
		CollectionsCount = "count_collections?tenant={tenant}&database={database}",
		Collection = "collections/{collection_id}",
	};

	public required string Heartbeat { get; init; }
	// Only the v2 API of Chroma 1.0.0 and later has it.
	public required string Healthcheck { get; init; }
	public required string Version { get; init; }
	public required string Reset { get; init; }
	// Only the v2 API has it.
	public required string UserIdentity { get; init; }
	public required string PreFlightChecks { get; init; }
	public required string Tenants { get; init; }
	public required string Tenant { get; init; }
	// Only Chroma Cloud has it.
	public required string CollectionByCrn { get; init; }
	public required string Databases { get; init; }
	public required string Database { get; init; }
	public required string Collections { get; init; }
	public required string CollectionByName { get; init; }
	// Only the v2 API of Chroma 1.5.7 and later has it.
	public required string CollectionById { get; init; }
	public required string CollectionsCount { get; init; }
	public required string Collection { get; init; }
}
