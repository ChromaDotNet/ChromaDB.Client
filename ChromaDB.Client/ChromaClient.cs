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
		return await _httpClient.Get<List<ChromaCollection>>("tenants/{tenant}/databases/{database}/collections", requestParams, cancellationToken);
	}

	public async Task<ChromaCollection> GetCollection(string name, string? tenant = null, string? database = null, CancellationToken cancellationToken = default)
	{
		tenant = tenant is not null and not [] ? tenant : _currentTenant.Name;
		database = database is not null and not [] ? database : _currentDatabase.Name;
		var requestParams = new RequestQueryParams()
			.Insert("{collectionName}", name)
			.Insert("{tenant}", tenant)
			.Insert("{database}", database);
		return await _httpClient.Get<ChromaCollection>("tenants/{tenant}/databases/{database}/collections/{collectionName}", requestParams, cancellationToken);
	}

	public async Task<ChromaHeartbeat> Heartbeat(CancellationToken cancellationToken = default)
	{
		return await _httpClient.Get<ChromaHeartbeat>("heartbeat", new RequestQueryParams(), cancellationToken);
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
		return await _httpClient.Post<CreateCollectionRequest, ChromaCollection>("tenants/{tenant}/databases/{database}/collections", request, requestParams, cancellationToken);
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
		return await _httpClient.Post<GetOrCreateCollectionRequest, ChromaCollection>("tenants/{tenant}/databases/{database}/collections", request, requestParams, cancellationToken);
	}

	public async Task DeleteCollection(string name, string? tenant = null, string? database = null, CancellationToken cancellationToken = default)
	{
		tenant = tenant is not null and not [] ? tenant : _currentTenant.Name;
		database = database is not null and not [] ? database : _currentDatabase.Name;
		var requestParams = new RequestQueryParams()
			.Insert("{collectionName}", name)
			.Insert("{tenant}", tenant)
			.Insert("{database}", database);
		await _httpClient.Delete("tenants/{tenant}/databases/{database}/collections/{collectionName}", requestParams, cancellationToken);
	}

	public async Task<string> GetVersion(CancellationToken cancellationToken = default)
	{
		return await _httpClient.Get<string>("version", new RequestQueryParams(), cancellationToken);
	}

	public async Task<bool> Reset(CancellationToken cancellationToken = default)
	{
		return await _httpClient.Post<ResetRequest, bool>("reset", null, new RequestQueryParams(), cancellationToken);
	}

	public async Task<int> CountCollections(string? tenant = null, string? database = null, CancellationToken cancellationToken = default)
	{
		tenant = tenant is not null and not [] ? tenant : _currentTenant.Name;
		database = database is not null and not [] ? database : _currentDatabase.Name;
		var requestParams = new RequestQueryParams()
			.Insert("{tenant}", tenant)
			.Insert("{database}", database);
		return await _httpClient.Get<int>("tenants/{tenant}/databases/{database}/collections_count", requestParams, cancellationToken);
	}

	public async Task CreateTenant(string name, CancellationToken cancellationToken = default)
	{
		var request = new CreateTenantRequest()
		{
			Name = name,
		};
		await _httpClient.Post("tenants", request, new RequestQueryParams(), cancellationToken);
	}

	public async Task<ChromaTenant> GetTenant(string name, CancellationToken cancellationToken = default)
	{
		var requestParams = new RequestQueryParams()
			.Insert("{tenant}", name);
		return await _httpClient.Get<ChromaTenant>("tenants/{tenant}", requestParams, cancellationToken);
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
		await _httpClient.Post("tenants/{tenant}/databases", request, requestParams, cancellationToken);
	}

	public async Task<ChromaDatabase> GetDatabase(string name, string? tenant = null, CancellationToken cancellationToken = default)
	{
		tenant = tenant is not null and not [] ? tenant : _currentTenant.Name;
		var requestParams = new RequestQueryParams()
			.Insert("{database}", name)
			.Insert("{tenant}", tenant);
		return await _httpClient.Get<ChromaDatabase>("tenants/{tenant}/databases/{database}", requestParams, cancellationToken);
	}
}
