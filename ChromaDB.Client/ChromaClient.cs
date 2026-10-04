using System.Globalization;
using System.Net;
using ChromaDB.Client.Common;
using ChromaDB.Client.Models;
using ChromaDB.Client.Models.Requests;

namespace ChromaDB.Client;

/// <summary>
/// The client of a Chroma server, for its tenants, databases and collections. The records of a collection go through the
/// <c>ChromaCollectionClient</c> that <c>GetCollectionClient</c> returns.
/// </summary>
public class ChromaClient
{
	private readonly ChromaConfigurationOptions _options;
	private readonly ChromaHttpClient _httpClient;
	private readonly ChromaTenant _currentTenant;
	private readonly ChromaDatabase _currentDatabase;

	/// <summary>
	/// The options of this client, as it was created with them.
	/// </summary>
	public ChromaConfigurationOptions Options => _options;

	/// <summary>
	/// A client that sends its requests with the given <c>HttpClient</c>, to the server of the options. It adds the credentials
	/// of the options to each of its requests, without changing the <c>HttpClient</c>.
	/// </summary>
	public ChromaClient(ChromaConfigurationOptions options, HttpClient httpClient)
		: this(options, new ChromaHttpClient(httpClient, options))
	{ }

	private ChromaClient(ChromaConfigurationOptions options, ChromaHttpClient httpClient)
	{
		_options = options;
		_httpClient = httpClient;
		_currentTenant = options.Tenant is not null and not []
			? new ChromaTenant(options.Tenant)
			: ClientConstants.DefaultTenant;
		_currentDatabase = options.Database is not null and not []
			? new ChromaDatabase(options.Database)
			: ClientConstants.DefaultDatabase;
	}

	/// <summary>
	/// The collections in the tenant and database of the options, or in the ones it is given.
	/// </summary>
	public async Task<List<ChromaCollection>> ListCollections(string? tenant = null, string? database = null, CancellationToken cancellationToken = default)
	{
		tenant = tenant is not null and not [] ? tenant : _currentTenant.Name;
		database = database is not null and not [] ? database : _currentDatabase.Name;
		var requestParams = new RequestQueryParams()
			.Insert("{tenant}", tenant)
			.Insert("{database}", database);
		return await _httpClient.Get<List<ChromaCollection>>(_httpClient.Routes.Collections, requestParams, cancellationToken);
	}

	/// <summary>
	/// One page of the collections, in the order of the server.
	/// </summary>
	public async Task<List<ChromaCollection>> ListCollections(int limit, int offset = 0, string? tenant = null, string? database = null, CancellationToken cancellationToken = default)
	{
		tenant = tenant is not null and not [] ? tenant : _currentTenant.Name;
		database = database is not null and not [] ? database : _currentDatabase.Name;
		var requestParams = new RequestQueryParams()
			.Insert("{tenant}", tenant)
			.Insert("{database}", database)
			.Insert("{limit}", limit.ToString(CultureInfo.InvariantCulture))
			.Insert("{offset}", offset.ToString(CultureInfo.InvariantCulture));
		var route = _httpClient.Routes.Collections;
		route += (route.Contains("?") ? "&" : "?") + "limit={limit}&offset={offset}";
		return await _httpClient.Get<List<ChromaCollection>>(route, requestParams, cancellationToken);
	}

	/// <summary>
	/// The collection with the given name, in the tenant and database of the options, or in the ones it is given.
	/// </summary>
	public async Task<ChromaCollection> GetCollection(string name, string? tenant = null, string? database = null, CancellationToken cancellationToken = default)
	{
		tenant = tenant is not null and not [] ? tenant : _currentTenant.Name;
		database = database is not null and not [] ? database : _currentDatabase.Name;
		var requestParams = new RequestQueryParams()
			.Insert("{collectionName}", name)
			.Insert("{tenant}", tenant)
			.Insert("{database}", database);
		return await _httpClient.Get<ChromaCollection>(_httpClient.Routes.CollectionByName, requestParams, cancellationToken);
	}

	/// <summary>
	/// Whether the collection exists. A missing collection: <c>404</c> from Chroma 1.x, <c>400</c> or <c>500</c> from the 0.x
	/// servers, always with "does not exist" in the message, also when the tenant or the database is missing. Any other error,
	/// like a bare <c>404</c> from a wrong address, still throws.
	/// </summary>
	public async Task<bool> CollectionExists(string name, string? tenant = null, string? database = null, CancellationToken cancellationToken = default)
	{
		try
		{
			await GetCollection(name, tenant, database, cancellationToken);
			return true;
		}
		catch (ChromaException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest or HttpStatusCode.InternalServerError
			&& (ex.Message.Contains("does not exist") || ex.ErrorType == "NotFoundError" && ex.Message.StartsWith("Collection", StringComparison.Ordinal)))
		{
			return false;
		}
	}

	/// <summary>
	/// The same client, reading metadata values another way: same <c>HttpClient</c> and options, and what it learned about the
	/// server. No request is sent, and this client does not change.
	/// </summary>
	public ChromaClient WithMetadataValues(ChromaMetadataValues metadataValues)
		=> new(_options.WithMetadataValues(metadataValues), _httpClient.WithMetadataValues(metadataValues));

	/// <summary>
	/// The same client, with the tenant and the database of its credentials, which the server tells in <c>auth/identity</c>, as the
	/// <c>CloudClient</c> of the Python client of Chroma does. The tenant is taken unless it is <c>*</c>; the database only when the
	/// credentials have exactly one, other than <c>*</c>: on Chroma Cloud an API key for one database gives both, an API key for a
	/// whole tenant only the tenant. A tenant or a database of the options other than the default one must match the credentials,
	/// otherwise it throws a <c>ChromaException</c>. The same <c>HttpClient</c> and what this client learned about the server are kept,
	/// and this client does not change.
	/// </summary>
	public async Task<ChromaClient> WithTenantAndDatabaseFromIdentity(CancellationToken cancellationToken = default)
	{
		var identity = await GetUserIdentity(cancellationToken);
		var tenant = identity.Tenant is { Length: > 0 } and not "*" ? identity.Tenant : null;
		var databases = identity.Databases?.Distinct().ToList();
		var database = databases is [{ Length: > 0 } single] && single != "*" ? single : null;
		if (tenant is not null && _options.Tenant is { Length: > 0 } givenTenant && givenTenant != ClientConstants.DefaultTenantName && givenTenant != tenant)
		{
			throw new ChromaException($"The tenant {givenTenant} of the options is not {tenant}, the one of the credentials.");
		}
		if (database is not null && _options.Database is { Length: > 0 } givenDatabase && givenDatabase != ClientConstants.DefaultDatabaseName && givenDatabase != database)
		{
			throw new ChromaException($"The database {givenDatabase} of the options is not {database}, the one of the credentials.");
		}
		var options = tenant is not null ? _options.WithTenant(tenant) : _options;
		options = database is not null ? options.WithDatabase(database) : options;
		return new(options, _httpClient);
	}

	/// <summary>
	/// A client for the records of the collection, with the options and the <c>HttpClient</c> of this client: no request is sent.
	/// </summary>
	public ChromaCollectionClient GetCollectionClient(ChromaCollection collection)
		=> new(collection, _options, _httpClient);

	/// <summary>
	/// The same as the overload that takes a <c>ChromaCollection</c>, without getting the collection first: the requests on
	/// a collection need only its id.
	/// </summary>
	public ChromaCollectionClient GetCollectionClient(Guid collectionId, string collectionName)
		=> new(new ChromaCollection(collectionName) { Id = collectionId }, _options, _httpClient);

	/// <summary>
	/// The collection with the given id, in the tenant and database of the options, or in the ones it is given. It needs the v2 API
	/// of Chroma 1.5.7 or later: the older servers answer <c>404 Not Found</c>.
	/// </summary>
	public async Task<ChromaCollection> GetCollectionById(Guid id, string? tenant = null, string? database = null, CancellationToken cancellationToken = default)
	{
		tenant = tenant is not null and not [] ? tenant : _currentTenant.Name;
		database = database is not null and not [] ? database : _currentDatabase.Name;
		var requestParams = new RequestQueryParams()
			.Insert("{collection_id}", id.ToString())
			.Insert("{tenant}", tenant)
			.Insert("{database}", database);
		return await _httpClient.Get<ChromaCollection>(_httpClient.Routes.CollectionById, requestParams, cancellationToken);
	}

	/// <summary>
	/// The heartbeat of the server, a time in nanoseconds.
	/// </summary>
	public async Task<ChromaHeartbeat> Heartbeat(CancellationToken cancellationToken = default)
	{
		return await _httpClient.Get<ChromaHeartbeat>(_httpClient.Routes.Heartbeat, new RequestQueryParams(), cancellationToken);
	}

	/// <summary>
	/// Whether the server is ready to serve requests, from Chroma 1.0.0; a server that is not ready answers <c>503</c>,
	/// a <c>ChromaException</c>.
	/// </summary>
	public async Task<ChromaHealthcheck> Healthcheck(CancellationToken cancellationToken = default)
	{
		return await _httpClient.Get<ChromaHealthcheck>(_httpClient.Routes.Healthcheck, new RequestQueryParams(), cancellationToken);
	}

	/// <summary>
	/// A collection by its Chroma Resource Name, <c>&lt;tenant resource name&gt;:&lt;database&gt;:&lt;collection&gt;</c>:
	/// Chroma Cloud only, as in the JavaScript client of Chroma. The operation is hidden in the OpenAPI description. On Chroma Cloud
	/// this request, sent with an API key limited to one database and with an API key for the whole tenant, got <c>403</c>, also for
	/// a collection of that tenant.
	/// </summary>
	public async Task<ChromaCollection> GetCollectionByCrn(string crn, CancellationToken cancellationToken = default)
	{
		var requestParams = new RequestQueryParams()
			.Insert("{crn}", crn);
		return await _httpClient.Get<ChromaCollection>(_httpClient.Routes.CollectionByCrn, requestParams, cancellationToken);
	}

	/// <summary>
	/// Creates a collection with the given name and metadata, in the tenant and database of the options, or in the ones it is given.
	/// </summary>
	public Task<ChromaCollection> CreateCollection(string name, Dictionary<string, object>? metadata = null, string? tenant = null, string? database = null, CancellationToken cancellationToken = default)
		=> CreateCollection(new ChromaCollectionDefinition(name) { Metadata = metadata }, tenant, database, cancellationToken);

	/// <summary>
	/// Creates a collection from its definition, with its name, metadata and configuration, in the tenant and database of the
	/// options, or in the ones it is given. The space of the configuration goes in the <c>hnsw:space</c> metadata.
	/// </summary>
	public async Task<ChromaCollection> CreateCollection(ChromaCollectionDefinition definition, string? tenant = null, string? database = null, CancellationToken cancellationToken = default)
	{
		tenant = tenant is not null and not [] ? tenant : _currentTenant.Name;
		database = database is not null and not [] ? database : _currentDatabase.Name;
		var requestParams = new RequestQueryParams()
			.Insert("{tenant}", tenant)
			.Insert("{database}", database);
		var request = new CreateCollectionRequest()
		{
			Name = definition.Name,
			Metadata = definition.ToRequestMetadata()
		};
		return await _httpClient.Post<CreateCollectionRequest, ChromaCollection>(_httpClient.Routes.Collections, request, requestParams, cancellationToken);
	}

	/// <summary>
	/// The collection with the given name, created when it does not exist, in the tenant and database of the options, or in
	/// the ones it is given.
	/// </summary>
	public Task<ChromaCollection> GetOrCreateCollection(string name, Dictionary<string, object>? metadata = null, string? tenant = null, string? database = null, CancellationToken cancellationToken = default)
		=> GetOrCreateCollection(new ChromaCollectionDefinition(name) { Metadata = metadata }, tenant, database, cancellationToken);

	/// <summary>
	/// The collection with the name of the definition, created from the definition when it does not exist, in the tenant and
	/// database of the options, or in the ones it is given. The space of the configuration goes in the <c>hnsw:space</c> metadata.
	/// </summary>
	public async Task<ChromaCollection> GetOrCreateCollection(ChromaCollectionDefinition definition, string? tenant = null, string? database = null, CancellationToken cancellationToken = default)
	{
		tenant = tenant is not null and not [] ? tenant : _currentTenant.Name;
		database = database is not null and not [] ? database : _currentDatabase.Name;
		var requestParams = new RequestQueryParams()
			.Insert("{tenant}", tenant)
			.Insert("{database}", database);
		var request = new GetOrCreateCollectionRequest()
		{
			Name = definition.Name,
			Metadata = definition.ToRequestMetadata()
		};
		return await _httpClient.Post<GetOrCreateCollectionRequest, ChromaCollection>(_httpClient.Routes.Collections, request, requestParams, cancellationToken);
	}

	/// <summary>
	/// Deletes the collection with the given name, in the tenant and database of the options, or in the ones it is given.
	/// </summary>
	public async Task DeleteCollection(string name, string? tenant = null, string? database = null, CancellationToken cancellationToken = default)
	{
		tenant = tenant is not null and not [] ? tenant : _currentTenant.Name;
		database = database is not null and not [] ? database : _currentDatabase.Name;
		var requestParams = new RequestQueryParams()
			.Insert("{collectionName}", name)
			.Insert("{tenant}", tenant)
			.Insert("{database}", database);
		await _httpClient.Delete(_httpClient.Routes.CollectionByName, requestParams, cancellationToken);
	}

	/// <summary>
	/// The version of the server. The 0.x servers send their own version; every Chroma 1.x answers <c>1.0.0</c>.
	/// </summary>
	public async Task<string> GetVersion(CancellationToken cancellationToken = default)
	{
		return await _httpClient.Get<string>(_httpClient.Routes.Version, new RequestQueryParams(), cancellationToken);
	}

	/// <summary>
	/// The user the server sees for the credentials of the client, with its tenant and databases.
	/// </summary>
	public async Task<ChromaUserIdentity> GetUserIdentity(CancellationToken cancellationToken = default)
	{
		return await _httpClient.Get<ChromaUserIdentity>(_httpClient.Routes.UserIdentity, new RequestQueryParams(), cancellationToken);
	}

	/// <summary>
	/// The limits of the server from <c>pre-flight-checks</c>, like <c>max_batch_size</c>, the most records a single add, update,
	/// upsert or delete can carry.
	/// </summary>
	public async Task<ChromaPreFlightChecks> GetPreFlightChecks(CancellationToken cancellationToken = default)
	{
		return await _httpClient.Get<ChromaPreFlightChecks>(_httpClient.Routes.PreFlightChecks, new RequestQueryParams(), cancellationToken);
	}

	/// <summary>
	/// Resets the server and returns its answer. Chroma Cloud does not allow it to an API key.
	/// </summary>
	public async Task<bool> Reset(CancellationToken cancellationToken = default)
	{
		return await _httpClient.Post<ResetRequest, bool>(_httpClient.Routes.Reset, null, new RequestQueryParams(), cancellationToken);
	}

	/// <summary>
	/// The number of collections in the tenant and database of the options, or in the ones it is given.
	/// </summary>
	public async Task<int> CountCollections(string? tenant = null, string? database = null, CancellationToken cancellationToken = default)
	{
		tenant = tenant is not null and not [] ? tenant : _currentTenant.Name;
		database = database is not null and not [] ? database : _currentDatabase.Name;
		var requestParams = new RequestQueryParams()
			.Insert("{tenant}", tenant)
			.Insert("{database}", database);
		return await _httpClient.Get<int>(_httpClient.Routes.CollectionsCount, requestParams, cancellationToken);
	}

	/// <summary>
	/// Creates a tenant with the given name. Chroma Cloud does not allow it to an API key.
	/// </summary>
	public async Task CreateTenant(string name, CancellationToken cancellationToken = default)
	{
		var request = new CreateTenantRequest()
		{
			Name = name,
		};
		await _httpClient.Post(_httpClient.Routes.Tenants, request, new RequestQueryParams(), cancellationToken);
	}

	/// <summary>
	/// The tenant with the given name, with the resource name that <c>UpdateTenant</c> sets.
	/// </summary>
	public async Task<ChromaTenant> GetTenant(string name, CancellationToken cancellationToken = default)
	{
		var requestParams = new RequestQueryParams()
			.Insert("{tenant}", name);
		return await _httpClient.Get<ChromaTenant>(_httpClient.Routes.Tenant, requestParams, cancellationToken);
	}

	/// <summary>
	/// Sets the name of the tenant in the resource names of Chroma Cloud, like the CRN of a collection.
	/// </summary>
	public async Task UpdateTenant(string name, string resourceName, CancellationToken cancellationToken = default)
	{
		var requestParams = new RequestQueryParams()
			.Insert("{tenant}", name);
		var request = new UpdateTenantRequest()
		{
			ResourceName = resourceName,
		};
		await _httpClient.Patch(_httpClient.Routes.Tenant, request, requestParams, cancellationToken);
	}

	/// <summary>
	/// Creates a database with the given name, in the tenant of the options, or in the one it is given.
	/// </summary>
	public async Task CreateDatabase(string name, string? tenant = null, CancellationToken cancellationToken = default)
	{
		tenant = tenant is not null and not [] ? tenant : _currentTenant.Name;
		var requestParams = new RequestQueryParams()
			.Insert("{tenant}", tenant);
		var request = new CreateDatabaseRequest()
		{
			Name = name,
		};
		await _httpClient.Post(_httpClient.Routes.Databases, request, requestParams, cancellationToken);
	}

	/// <summary>
	/// The database with the given name, in the tenant of the options, or in the one it is given.
	/// </summary>
	public async Task<ChromaDatabase> GetDatabase(string name, string? tenant = null, CancellationToken cancellationToken = default)
	{
		tenant = tenant is not null and not [] ? tenant : _currentTenant.Name;
		var requestParams = new RequestQueryParams()
			.Insert("{database}", name)
			.Insert("{tenant}", tenant);
		return await _httpClient.Get<ChromaDatabase>(_httpClient.Routes.Database, requestParams, cancellationToken);
	}

	/// <summary>
	/// The databases in the tenant of the options, or in the one it is given. It needs the v2 API of Chroma 0.6.3 or later:
	/// the older servers answer <c>405 Method Not Allowed</c>.
	/// </summary>
	public async Task<List<ChromaDatabase>> ListDatabases(string? tenant = null, CancellationToken cancellationToken = default)
	{
		tenant = tenant is not null and not [] ? tenant : _currentTenant.Name;
		var requestParams = new RequestQueryParams()
			.Insert("{tenant}", tenant);
		return await _httpClient.Get<List<ChromaDatabase>>(_httpClient.Routes.Databases, requestParams, cancellationToken);
	}

	/// <summary>
	/// One page of the databases, in the order of the server.
	/// </summary>
	public async Task<List<ChromaDatabase>> ListDatabases(int limit, int offset = 0, string? tenant = null, CancellationToken cancellationToken = default)
	{
		tenant = tenant is not null and not [] ? tenant : _currentTenant.Name;
		var requestParams = new RequestQueryParams()
			.Insert("{tenant}", tenant)
			.Insert("{limit}", limit.ToString(CultureInfo.InvariantCulture))
			.Insert("{offset}", offset.ToString(CultureInfo.InvariantCulture));
		var route = _httpClient.Routes.Databases;
		route += (route.Contains("?") ? "&" : "?") + "limit={limit}&offset={offset}";
		return await _httpClient.Get<List<ChromaDatabase>>(route, requestParams, cancellationToken);
	}

	/// <summary>
	/// Deletes the database with the given name, in the tenant of the options, or in the one it is given. It needs the v2 API of
	/// Chroma 0.6.3 or later: the older servers answer <c>405 Method Not Allowed</c>.
	/// </summary>
	public async Task DeleteDatabase(string name, string? tenant = null, CancellationToken cancellationToken = default)
	{
		tenant = tenant is not null and not [] ? tenant : _currentTenant.Name;
		var requestParams = new RequestQueryParams()
			.Insert("{database}", name)
			.Insert("{tenant}", tenant);
		await _httpClient.Delete(_httpClient.Routes.Database, requestParams, cancellationToken);
	}
}
