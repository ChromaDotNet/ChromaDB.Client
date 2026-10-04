using System.Globalization;
using System.Net;
using ChromaDB.Client.Common;
using ChromaDB.Client.Models;
using ChromaDB.Client.Models.Requests;

namespace ChromaDB.Client;

public class ChromaClient
{
	private readonly ChromaConfigurationOptions _options;
	private readonly ChromaHttpClient _httpClient;
	private readonly ChromaTenant _currentTenant;
	private readonly ChromaDatabase _currentDatabase;

	// The options of this client, as it was created with them.
	public ChromaConfigurationOptions Options => _options;

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

	public async Task<List<ChromaCollection>> ListCollections(string? tenant = null, string? database = null, CancellationToken cancellationToken = default)
	{
		tenant = tenant is not null and not [] ? tenant : _currentTenant.Name;
		database = database is not null and not [] ? database : _currentDatabase.Name;
		var requestParams = new RequestQueryParams()
			.Insert("{tenant}", tenant)
			.Insert("{database}", database);
		return await _httpClient.Get<List<ChromaCollection>>(_httpClient.Routes.Collections, requestParams, cancellationToken);
	}

	// One page of the collections, in the order of the server.
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

	// A missing collection: 404 from Chroma 1.x, 400 or 500 from the 0.x servers, always with "does not exist" in the message,
	// also when the tenant or the database is missing. Any other error, like a bare 404 from a wrong address, still throws.
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

	// The same client, reading metadata values another way: same HttpClient and options, and what it learned about the server.
	// No request is sent, and this client does not change.
	public ChromaClient WithMetadataValues(ChromaMetadataValues metadataValues)
		=> new(_options.WithMetadataValues(metadataValues), _httpClient.WithMetadataValues(metadataValues));

	// A client for the records of the collection, with the options and the HttpClient of this client: no request is sent.
	public ChromaCollectionClient GetCollectionClient(ChromaCollection collection)
		=> new(collection, _options, _httpClient);

	// The same without getting the collection first: the requests on a collection need only its id.
	public ChromaCollectionClient GetCollectionClient(Guid collectionId, string collectionName)
		=> new(new ChromaCollection(collectionName) { Id = collectionId }, _options, _httpClient);

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

	public async Task<ChromaHeartbeat> Heartbeat(CancellationToken cancellationToken = default)
	{
		return await _httpClient.Get<ChromaHeartbeat>(_httpClient.Routes.Heartbeat, new RequestQueryParams(), cancellationToken);
	}

	public Task<ChromaCollection> CreateCollection(string name, Dictionary<string, object>? metadata = null, string? tenant = null, string? database = null, CancellationToken cancellationToken = default)
		=> CreateCollection(new ChromaCollectionDefinition(name) { Metadata = metadata }, tenant, database, cancellationToken);

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

	public Task<ChromaCollection> GetOrCreateCollection(string name, Dictionary<string, object>? metadata = null, string? tenant = null, string? database = null, CancellationToken cancellationToken = default)
		=> GetOrCreateCollection(new ChromaCollectionDefinition(name) { Metadata = metadata }, tenant, database, cancellationToken);

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

	public async Task<string> GetVersion(CancellationToken cancellationToken = default)
	{
		return await _httpClient.Get<string>(_httpClient.Routes.Version, new RequestQueryParams(), cancellationToken);
	}

	public async Task<ChromaUserIdentity> GetUserIdentity(CancellationToken cancellationToken = default)
	{
		return await _httpClient.Get<ChromaUserIdentity>(_httpClient.Routes.UserIdentity, new RequestQueryParams(), cancellationToken);
	}

	public async Task<ChromaPreFlightChecks> GetPreFlightChecks(CancellationToken cancellationToken = default)
	{
		return await _httpClient.Get<ChromaPreFlightChecks>(_httpClient.Routes.PreFlightChecks, new RequestQueryParams(), cancellationToken);
	}

	public async Task<bool> Reset(CancellationToken cancellationToken = default)
	{
		return await _httpClient.Post<ResetRequest, bool>(_httpClient.Routes.Reset, null, new RequestQueryParams(), cancellationToken);
	}

	public async Task<int> CountCollections(string? tenant = null, string? database = null, CancellationToken cancellationToken = default)
	{
		tenant = tenant is not null and not [] ? tenant : _currentTenant.Name;
		database = database is not null and not [] ? database : _currentDatabase.Name;
		var requestParams = new RequestQueryParams()
			.Insert("{tenant}", tenant)
			.Insert("{database}", database);
		return await _httpClient.Get<int>(_httpClient.Routes.CollectionsCount, requestParams, cancellationToken);
	}

	public async Task CreateTenant(string name, CancellationToken cancellationToken = default)
	{
		var request = new CreateTenantRequest()
		{
			Name = name,
		};
		await _httpClient.Post(_httpClient.Routes.Tenants, request, new RequestQueryParams(), cancellationToken);
	}

	public async Task<ChromaTenant> GetTenant(string name, CancellationToken cancellationToken = default)
	{
		var requestParams = new RequestQueryParams()
			.Insert("{tenant}", name);
		return await _httpClient.Get<ChromaTenant>(_httpClient.Routes.Tenant, requestParams, cancellationToken);
	}

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

	public async Task<ChromaDatabase> GetDatabase(string name, string? tenant = null, CancellationToken cancellationToken = default)
	{
		tenant = tenant is not null and not [] ? tenant : _currentTenant.Name;
		var requestParams = new RequestQueryParams()
			.Insert("{database}", name)
			.Insert("{tenant}", tenant);
		return await _httpClient.Get<ChromaDatabase>(_httpClient.Routes.Database, requestParams, cancellationToken);
	}

	public async Task<List<ChromaDatabase>> ListDatabases(string? tenant = null, CancellationToken cancellationToken = default)
	{
		tenant = tenant is not null and not [] ? tenant : _currentTenant.Name;
		var requestParams = new RequestQueryParams()
			.Insert("{tenant}", tenant);
		return await _httpClient.Get<List<ChromaDatabase>>(_httpClient.Routes.Databases, requestParams, cancellationToken);
	}

	// One page of the databases, in the order of the server.
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

	public async Task DeleteDatabase(string name, string? tenant = null, CancellationToken cancellationToken = default)
	{
		tenant = tenant is not null and not [] ? tenant : _currentTenant.Name;
		var requestParams = new RequestQueryParams()
			.Insert("{database}", name)
			.Insert("{tenant}", tenant);
		await _httpClient.Delete(_httpClient.Routes.Database, requestParams, cancellationToken);
	}
}
