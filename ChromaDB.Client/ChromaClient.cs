using System.Globalization;
using System.Net;
using ChromaDB.Client.Common;
using ChromaDB.Client.Models;
using ChromaDB.Client.Models.Requests;

namespace ChromaDB.Client;

public class ChromaClient
{
	private readonly ChromaHttpClient _httpClient;
	private readonly ChromaTenant _currentTenant;
	private readonly ChromaDatabase _currentDatabase;

	public ChromaClient(ChromaConfigurationOptions options, HttpClient httpClient)
	{
		_httpClient = new ChromaHttpClient(httpClient, options);
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

	// A missing collection: 404 from Chroma 1.x; 400 or 500 with "does not exist" in the message from the 0.x servers.
	public async Task<bool> CollectionExists(string name, string? tenant = null, string? database = null, CancellationToken cancellationToken = default)
	{
		try
		{
			await GetCollection(name, tenant, database, cancellationToken);
			return true;
		}
		catch (ChromaException ex) when (ex.StatusCode == HttpStatusCode.NotFound
			|| ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.InternalServerError && ex.Message.Contains("does not exist"))
		{
			return false;
		}
	}

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

	public async Task<ChromaCollection> CreateCollection(string name, Dictionary<string, object>? metadata = null, string? tenant = null, string? database = null, CancellationToken cancellationToken = default)
	{
		tenant = tenant is not null and not [] ? tenant : _currentTenant.Name;
		database = database is not null and not [] ? database : _currentDatabase.Name;
		var requestParams = new RequestQueryParams()
			.Insert("{tenant}", tenant)
			.Insert("{database}", database);
		var request = new CreateCollectionRequest()
		{
			Name = name,
			Metadata = metadata
		};
		return await _httpClient.Post<CreateCollectionRequest, ChromaCollection>(_httpClient.Routes.Collections, request, requestParams, cancellationToken);
	}

	public async Task<ChromaCollection> GetOrCreateCollection(string name, Dictionary<string, object>? metadata = null, string? tenant = null, string? database = null, CancellationToken cancellationToken = default)
	{
		tenant = tenant is not null and not [] ? tenant : _currentTenant.Name;
		database = database is not null and not [] ? database : _currentDatabase.Name;
		var requestParams = new RequestQueryParams()
			.Insert("{tenant}", tenant)
			.Insert("{database}", database);
		var request = new GetOrCreateCollectionRequest()
		{
			Name = name,
			Metadata = metadata
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
