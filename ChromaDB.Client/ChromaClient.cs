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
public class ChromaClient : IDisposable
{
	private readonly ChromaConfigurationOptions _options;
	private readonly ChromaHttpClient _httpClient;
	private readonly HttpClient? _ownHttpClient;
	private readonly ChromaTenant _currentTenant;
	private readonly ChromaDatabase _currentDatabase;

	/// <summary>
	/// The options of this client, as it was created with them.
	/// </summary>
	public ChromaConfigurationOptions Options => _options;

	/// <summary>
	/// A client that sends its requests with the given <c>HttpClient</c>, to the server of the options. It adds the credentials
	/// of the options to each of its requests, without changing the <c>HttpClient</c>, and <c>Dispose</c> leaves it open.
	/// </summary>
	public ChromaClient(ChromaConfigurationOptions options, HttpClient httpClient)
		: this(options, new ChromaHttpClient(httpClient, options))
	{ }

	/// <summary>
	/// A client of the server at the URI, like <c>http://localhost:8000</c>, with an <c>HttpClient</c> of its own, which
	/// <c>Dispose</c> closes.
	/// </summary>
	public ChromaClient(string uri)
		: this(new ChromaConfigurationOptions(uri))
	{ }

	/// <summary>
	/// A client of the server of the options, with an <c>HttpClient</c> of its own, which <c>Dispose</c> closes. On .NET 8 and later
	/// its connections last two minutes, as with <c>AddChromaClient</c>, so a change of the address of the server in the DNS is seen.
	/// The clients that <c>WithMetadataValues</c>, <c>WithTenantAndDatabaseFromIdentityAsync</c> and <c>GetCollectionClient</c> return
	/// share that <c>HttpClient</c>: they work until this client is disposed.
	/// </summary>
	public ChromaClient(ChromaConfigurationOptions options)
		: this(options, CreateHttpClient(options, out var ownHttpClient))
	{
		_ownHttpClient = ownHttpClient;
	}

	// An HttpClient of its own, closed when the options are rejected, like basic authentication with a token in the Authorization header.
	private static ChromaHttpClient CreateHttpClient(ChromaConfigurationOptions options, out HttpClient httpClient)
	{
		httpClient = NewHttpClient();
		try
		{
			return new ChromaHttpClient(httpClient, options);
		}
		catch
		{
			httpClient.Dispose();
			throw;
		}
	}

	// As the handlers of IHttpClientFactory, renewed every two minutes by default; SocketsHttpHandler is not in .NET Standard 2.0.
	private static HttpClient NewHttpClient()
#if NET
		=> new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) });
#else
		=> new();
#endif

	/// <summary>
	/// Closes the <c>HttpClient</c> the client created; one given to the constructor stays open.
	/// </summary>
	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	/// <summary>
	/// Closes the <c>HttpClient</c> the client created, when <c>disposing</c>.
	/// </summary>
	protected virtual void Dispose(bool disposing)
	{
		if (disposing)
		{
			_ownHttpClient?.Dispose();
		}
	}

	// The span and the duration of each operation, on the server, on a tenant, or in a database: the tenant and the database
	// are the ones the request goes to.
	private Task<T> ServerOperation<T>(string name, Func<Task<T>> body)
		=> ChromaInstrumentation.Run(name, null, null, _options.Uri, body);

	private Task<T> TenantOperation<T>(string name, string? tenant, Func<Task<T>> body)
		=> ChromaInstrumentation.Run(name, null, TenantName(tenant), _options.Uri, body);

	private Task TenantOperation(string name, string? tenant, Func<Task> body)
		=> ChromaInstrumentation.Run(name, null, TenantName(tenant), _options.Uri, body);

	private Task<T> DatabaseOperation<T>(string name, string? collection, string? tenant, string? database, Func<Task<T>> body)
		=> ChromaInstrumentation.Run(name, collection, $"{TenantName(tenant)}|{DatabaseName(database)}", _options.Uri, body);

	private Task DatabaseOperation(string name, string? collection, string? tenant, string? database, Func<Task> body)
		=> ChromaInstrumentation.Run(name, collection, $"{TenantName(tenant)}|{DatabaseName(database)}", _options.Uri, body);

	// The message when the collection has another space than the one the schema gave it, which the server reports.
	private static string? SpaceIgnored(ChromaCollectionDefinition definition, ChromaCollection collection)
		=> definition.Schema is not null && definition.Configuration?.Space is { } space && collection.Space is { } actual && actual != space
			? $"The collection has the space {ChromaSpaceNames.ToName(actual)}, not {ChromaSpaceNames.ToName(space)}: Chroma 1.3.2 and later apply the space with a schema."
			: null;

	private string TenantName(string? tenant)
		=> tenant is not null and not [] ? tenant : _currentTenant.Name;

	private string DatabaseName(string? database)
		=> database is not null and not [] ? database : _currentDatabase.Name;

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
	public Task<IReadOnlyList<ChromaCollection>> ListCollectionsAsync(string? tenant = null, string? database = null, CancellationToken cancellationToken = default)
		=> DatabaseOperation<IReadOnlyList<ChromaCollection>>("list_collections", null, tenant, database, async () =>
		{
			tenant = tenant is not null and not [] ? tenant : _currentTenant.Name;
			database = database is not null and not [] ? database : _currentDatabase.Name;
			var requestParams = new RequestQueryParams()
				.Insert("{tenant}", tenant)
				.Insert("{database}", database);
			return await _httpClient.Get<List<ChromaCollection>>(_httpClient.Routes.Collections, requestParams, cancellationToken);
		});

	/// <summary>
	/// One page of the collections, in the order of the server.
	/// </summary>
	public Task<IReadOnlyList<ChromaCollection>> ListCollectionsAsync(int limit, int offset = 0, string? tenant = null, string? database = null, CancellationToken cancellationToken = default)
		=> DatabaseOperation<IReadOnlyList<ChromaCollection>>("list_collections", null, tenant, database, async () =>
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
		});

	/// <summary>
	/// The collection with the given name, in the tenant and database of the options, or in the ones it is given.
	/// </summary>
	public Task<ChromaCollection> GetCollectionAsync(string name, string? tenant = null, string? database = null, CancellationToken cancellationToken = default)
		=> DatabaseOperation("get_collection", name, tenant, database, () => GetCollectionCore(name, tenant, database, cancellationToken));

	// Without a span of its own: CollectionExists has one, where a missing collection is not an error.
	private async Task<ChromaCollection> GetCollectionCore(string name, string? tenant, string? database, CancellationToken cancellationToken)
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
	public Task<bool> CollectionExistsAsync(string name, string? tenant = null, string? database = null, CancellationToken cancellationToken = default)
		=> DatabaseOperation("collection_exists", name, tenant, database, async () =>
		{
			try
			{
				await GetCollectionCore(name, tenant, database, cancellationToken);
				return true;
			}
			catch (ChromaException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest or HttpStatusCode.InternalServerError
				&& (ex.Message.Contains("does not exist") || ex.ErrorType == "NotFoundError" && ex.Message.StartsWith("Collection", StringComparison.Ordinal)))
			{
				return false;
			}
		});

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
	/// whole tenant only the tenant. A tenant or a database of the options other than the default one must match the one the
	/// credentials give, when they give one, otherwise it throws a <c>ChromaException</c>; with several databases in the credentials,
	/// the one of the options stays as it is, as in the Python client. The same <c>HttpClient</c> and what this client learned about
	/// the server are kept, and this client does not change.
	/// </summary>
	public async Task<ChromaClient> WithTenantAndDatabaseFromIdentityAsync(CancellationToken cancellationToken = default)
	{
		var identity = await GetUserIdentityAsync(cancellationToken);
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
	public Task<ChromaCollection> GetCollectionByIdAsync(Guid id, string? tenant = null, string? database = null, CancellationToken cancellationToken = default)
		=> DatabaseOperation("get_collection", null, tenant, database, async () =>
		{
			tenant = tenant is not null and not [] ? tenant : _currentTenant.Name;
			database = database is not null and not [] ? database : _currentDatabase.Name;
			var requestParams = new RequestQueryParams()
				.Insert("{collection_id}", id.ToString())
				.Insert("{tenant}", tenant)
				.Insert("{database}", database);
			return await _httpClient.Get<ChromaCollection>(_httpClient.Routes.CollectionById, requestParams, cancellationToken);
		});

	/// <summary>
	/// The heartbeat of the server, a time in nanoseconds.
	/// </summary>
	public Task<ChromaHeartbeat> HeartbeatAsync(CancellationToken cancellationToken = default)
		=> ServerOperation("heartbeat", async () =>
		{
			return await _httpClient.Get<ChromaHeartbeat>(_httpClient.Routes.Heartbeat, new RequestQueryParams(), cancellationToken);
		});

	/// <summary>
	/// Whether the server is ready to serve requests, from Chroma 1.0.0; a server that is not ready answers <c>503</c>,
	/// a <c>ChromaException</c>.
	/// </summary>
	public Task<ChromaHealthcheck> HealthcheckAsync(CancellationToken cancellationToken = default)
		=> ServerOperation("healthcheck", async () =>
		{
			return await _httpClient.Get<ChromaHealthcheck>(_httpClient.Routes.Healthcheck, new RequestQueryParams(), cancellationToken);
		});

	/// <summary>
	/// A collection by its Chroma Resource Name, <c>&lt;tenant resource name&gt;:&lt;database&gt;:&lt;collection&gt;</c>:
	/// Chroma Cloud only, as in the JavaScript client of Chroma. The operation is hidden in the OpenAPI description. On Chroma Cloud
	/// this request, sent with an API key limited to one database and with an API key for the whole tenant, got <c>403</c>, also for
	/// a collection of that tenant.
	/// </summary>
	public Task<ChromaCollection> GetCollectionByCrnAsync(string crn, CancellationToken cancellationToken = default)
		=> ServerOperation("get_collection", async () =>
		{
			var requestParams = new RequestQueryParams()
				.Insert("{crn}", crn);
			return await _httpClient.Get<ChromaCollection>(_httpClient.Routes.CollectionByCrn, requestParams, cancellationToken);
		});

	/// <summary>
	/// Creates a collection with the given name and metadata, in the tenant and database of the options, or in the ones it is given.
	/// </summary>
	public Task<ChromaCollection> CreateCollectionAsync(string name, IReadOnlyDictionary<string, object>? metadata = null, string? tenant = null, string? database = null, CancellationToken cancellationToken = default)
		=> CreateCollectionAsync(new ChromaCollectionDefinition(name) { Metadata = metadata }, tenant, database, cancellationToken);

	/// <summary>
	/// Creates a collection from its definition, with its name, metadata and configuration, in the tenant and database of the
	/// options, or in the ones it is given. The space of the configuration goes in the <c>hnsw:space</c> metadata.
	/// </summary>
	public Task<ChromaCollection> CreateCollectionAsync(ChromaCollectionDefinition definition, string? tenant = null, string? database = null, CancellationToken cancellationToken = default)
		=> DatabaseOperation("create_collection", definition.Name, tenant, database, async () =>
		{
			tenant = tenant is not null and not [] ? tenant : _currentTenant.Name;
			database = database is not null and not [] ? database : _currentDatabase.Name;
			var requestParams = new RequestQueryParams()
				.Insert("{tenant}", tenant)
				.Insert("{database}", database);
			var request = new CreateCollectionRequest()
			{
				Name = definition.Name,
				Metadata = definition.ToRequestMetadata(),
				Schema = definition.ToRequestSchema(),
			};
			var collection = await _httpClient.Post<CreateCollectionRequest, ChromaCollection>(_httpClient.Routes.Collections, request, requestParams, cancellationToken);
			// Chroma 1.0.0 to 1.2.2 create the collection without the schema and without an error: the collection just created goes.
			if (definition.Schema is not null && collection.SchemaJson is not { ValueKind: System.Text.Json.JsonValueKind.Object })
			{
				// Not canceled with the call: the answer arrived, so the collection is created, and it must go. A call canceled before the
				// answer leaves nothing to tell whether the collection was created, so nothing is deleted then.
				await DeleteCollectionAsync(collection.Name, tenant, database, CancellationToken.None);
				throw new ChromaException("The server creates the collection without its schema: Chroma 1.3.0 and later apply it. The collection was deleted.");
			}
			// Chroma 1.3.0 creates the collection with the space of the schema ignored: l2.
			if (SpaceIgnored(definition, collection) is { } ignored)
			{
				await DeleteCollectionAsync(collection.Name, tenant, database, CancellationToken.None);
				throw new ChromaException($"{ignored} The collection was deleted.");
			}
			return collection;
		});

	/// <summary>
	/// The collection with the given name, created when it does not exist, in the tenant and database of the options, or in
	/// the ones it is given.
	/// </summary>
	public Task<ChromaCollection> GetOrCreateCollectionAsync(string name, IReadOnlyDictionary<string, object>? metadata = null, string? tenant = null, string? database = null, CancellationToken cancellationToken = default)
		=> GetOrCreateCollectionAsync(new ChromaCollectionDefinition(name) { Metadata = metadata }, tenant, database, cancellationToken);

	/// <summary>
	/// The collection with the name of the definition, created from the definition when it does not exist, in the tenant and
	/// database of the options, or in the ones it is given. The space of the configuration goes in the <c>hnsw:space</c> metadata.
	/// </summary>
	public Task<ChromaCollection> GetOrCreateCollectionAsync(ChromaCollectionDefinition definition, string? tenant = null, string? database = null, CancellationToken cancellationToken = default)
		=> DatabaseOperation("get_or_create_collection", definition.Name, tenant, database, async () =>
		{
			tenant = tenant is not null and not [] ? tenant : _currentTenant.Name;
			database = database is not null and not [] ? database : _currentDatabase.Name;
			var requestParams = new RequestQueryParams()
				.Insert("{tenant}", tenant)
				.Insert("{database}", database);
			var request = new GetOrCreateCollectionRequest()
			{
				Name = definition.Name,
				Metadata = definition.ToRequestMetadata(),
				Schema = definition.ToRequestSchema(),
			};
			var collection = await _httpClient.Post<GetOrCreateCollectionRequest, ChromaCollection>(_httpClient.Routes.Collections, request, requestParams, cancellationToken);
			// As in CreateCollection, but the collection stays: it may have existed before.
			if (definition.Schema is not null && collection.SchemaJson is not { ValueKind: System.Text.Json.JsonValueKind.Object })
			{
				throw new ChromaException("The server answers without the schema of the collection: Chroma 1.3.0 and later apply it.");
			}
			if (SpaceIgnored(definition, collection) is { } ignored)
			{
				throw new ChromaException(ignored);
			}
			return collection;
		});

	/// <summary>
	/// Deletes the collection with the given name, in the tenant and database of the options, or in the ones it is given.
	/// </summary>
	public Task DeleteCollectionAsync(string name, string? tenant = null, string? database = null, CancellationToken cancellationToken = default)
		=> DatabaseOperation("delete_collection", name, tenant, database, async () =>
		{
			tenant = tenant is not null and not [] ? tenant : _currentTenant.Name;
			database = database is not null and not [] ? database : _currentDatabase.Name;
			var requestParams = new RequestQueryParams()
				.Insert("{collectionName}", name)
				.Insert("{tenant}", tenant)
				.Insert("{database}", database);
			await _httpClient.Delete(_httpClient.Routes.CollectionByName, requestParams, cancellationToken);
		});

	/// <summary>
	/// The version of the server. The 0.x servers send their own version; every Chroma 1.x answers <c>1.0.0</c>.
	/// </summary>
	public Task<string> GetVersionAsync(CancellationToken cancellationToken = default)
		=> ServerOperation("get_version", async () =>
		{
			return await _httpClient.Get<string>(_httpClient.Routes.Version, new RequestQueryParams(), cancellationToken);
		});

	/// <summary>
	/// The user the server sees for the credentials of the client, with its tenant and databases.
	/// </summary>
	public Task<ChromaUserIdentity> GetUserIdentityAsync(CancellationToken cancellationToken = default)
		=> ServerOperation("get_user_identity", async () =>
		{
			return await _httpClient.Get<ChromaUserIdentity>(_httpClient.Routes.UserIdentity, new RequestQueryParams(), cancellationToken);
		});

	/// <summary>
	/// The limits of the server from <c>pre-flight-checks</c>, like <c>max_batch_size</c>, the most records a single add, update,
	/// upsert or delete can carry.
	/// </summary>
	public Task<ChromaPreFlightChecks> GetPreFlightChecksAsync(CancellationToken cancellationToken = default)
		=> ServerOperation("get_pre_flight_checks", async () =>
		{
			return await _httpClient.Get<ChromaPreFlightChecks>(_httpClient.Routes.PreFlightChecks, new RequestQueryParams(), cancellationToken);
		});

	/// <summary>
	/// Resets the server and returns its answer. Chroma Cloud does not allow it to an API key.
	/// </summary>
	public Task<bool> ResetAsync(CancellationToken cancellationToken = default)
		=> ServerOperation("reset", async () =>
		{
			return await _httpClient.Post<ResetRequest, bool>(_httpClient.Routes.Reset, null, new RequestQueryParams(), cancellationToken);
		});

	/// <summary>
	/// The number of collections in the tenant and database of the options, or in the ones it is given.
	/// </summary>
	public Task<int> CountCollectionsAsync(string? tenant = null, string? database = null, CancellationToken cancellationToken = default)
		=> DatabaseOperation("count_collections", null, tenant, database, async () =>
		{
			tenant = tenant is not null and not [] ? tenant : _currentTenant.Name;
			database = database is not null and not [] ? database : _currentDatabase.Name;
			var requestParams = new RequestQueryParams()
				.Insert("{tenant}", tenant)
				.Insert("{database}", database);
			return await _httpClient.Get<int>(_httpClient.Routes.CollectionsCount, requestParams, cancellationToken);
		});

	/// <summary>
	/// Creates a tenant with the given name. Chroma Cloud does not allow it to an API key.
	/// </summary>
	public Task CreateTenantAsync(string name, CancellationToken cancellationToken = default)
		=> TenantOperation("create_tenant", name, async () =>
		{
			var request = new CreateTenantRequest()
			{
				Name = name,
			};
			await _httpClient.Post(_httpClient.Routes.Tenants, request, new RequestQueryParams(), cancellationToken);
		});

	/// <summary>
	/// The tenant with the given name, with the resource name that <c>UpdateTenantAsync</c> sets.
	/// </summary>
	public Task<ChromaTenant> GetTenantAsync(string name, CancellationToken cancellationToken = default)
		=> TenantOperation("get_tenant", name, async () =>
		{
			var requestParams = new RequestQueryParams()
				.Insert("{tenant}", name);
			return await _httpClient.Get<ChromaTenant>(_httpClient.Routes.Tenant, requestParams, cancellationToken);
		});

	/// <summary>
	/// Sets the name of the tenant in the resource names of Chroma Cloud, like the CRN of a collection.
	/// </summary>
	public Task UpdateTenantAsync(string name, string resourceName, CancellationToken cancellationToken = default)
		=> TenantOperation("update_tenant", name, async () =>
		{
			var requestParams = new RequestQueryParams()
				.Insert("{tenant}", name);
			var request = new UpdateTenantRequest()
			{
				ResourceName = resourceName,
			};
			await _httpClient.Patch(_httpClient.Routes.Tenant, request, requestParams, cancellationToken);
		});

	/// <summary>
	/// Creates a database with the given name, in the tenant of the options, or in the one it is given.
	/// </summary>
	public Task CreateDatabaseAsync(string name, string? tenant = null, CancellationToken cancellationToken = default)
		=> DatabaseOperation("create_database", null, tenant, name, async () =>
		{
			tenant = tenant is not null and not [] ? tenant : _currentTenant.Name;
			var requestParams = new RequestQueryParams()
				.Insert("{tenant}", tenant);
			var request = new CreateDatabaseRequest()
			{
				Name = name,
			};
			await _httpClient.Post(_httpClient.Routes.Databases, request, requestParams, cancellationToken);
		});

	/// <summary>
	/// The database with the given name, in the tenant of the options, or in the one it is given.
	/// </summary>
	public Task<ChromaDatabase> GetDatabaseAsync(string name, string? tenant = null, CancellationToken cancellationToken = default)
		=> DatabaseOperation("get_database", null, tenant, name, async () =>
		{
			tenant = tenant is not null and not [] ? tenant : _currentTenant.Name;
			var requestParams = new RequestQueryParams()
				.Insert("{database}", name)
				.Insert("{tenant}", tenant);
			return await _httpClient.Get<ChromaDatabase>(_httpClient.Routes.Database, requestParams, cancellationToken);
		});

	/// <summary>
	/// The databases in the tenant of the options, or in the one it is given. It needs the v2 API of Chroma 0.6.3 or later:
	/// the older servers answer <c>405 Method Not Allowed</c>.
	/// </summary>
	public Task<IReadOnlyList<ChromaDatabase>> ListDatabasesAsync(string? tenant = null, CancellationToken cancellationToken = default)
		=> TenantOperation<IReadOnlyList<ChromaDatabase>>("list_databases", tenant, async () =>
		{
			tenant = tenant is not null and not [] ? tenant : _currentTenant.Name;
			var requestParams = new RequestQueryParams()
				.Insert("{tenant}", tenant);
			return await _httpClient.Get<List<ChromaDatabase>>(_httpClient.Routes.Databases, requestParams, cancellationToken);
		});

	/// <summary>
	/// One page of the databases, in the order of the server.
	/// </summary>
	public Task<IReadOnlyList<ChromaDatabase>> ListDatabasesAsync(int limit, int offset = 0, string? tenant = null, CancellationToken cancellationToken = default)
		=> TenantOperation<IReadOnlyList<ChromaDatabase>>("list_databases", tenant, async () =>
		{
			tenant = tenant is not null and not [] ? tenant : _currentTenant.Name;
			var requestParams = new RequestQueryParams()
				.Insert("{tenant}", tenant)
				.Insert("{limit}", limit.ToString(CultureInfo.InvariantCulture))
				.Insert("{offset}", offset.ToString(CultureInfo.InvariantCulture));
			var route = _httpClient.Routes.Databases;
			route += (route.Contains("?") ? "&" : "?") + "limit={limit}&offset={offset}";
			return await _httpClient.Get<List<ChromaDatabase>>(route, requestParams, cancellationToken);
		});

	/// <summary>
	/// Deletes the database with the given name, in the tenant of the options, or in the one it is given. It needs the v2 API of
	/// Chroma 0.6.3 or later: the older servers answer <c>405 Method Not Allowed</c>.
	/// </summary>
	public Task DeleteDatabaseAsync(string name, string? tenant = null, CancellationToken cancellationToken = default)
		=> DatabaseOperation("delete_database", null, tenant, name, async () =>
		{
			tenant = tenant is not null and not [] ? tenant : _currentTenant.Name;
			var requestParams = new RequestQueryParams()
				.Insert("{database}", name)
				.Insert("{tenant}", tenant);
			await _httpClient.Delete(_httpClient.Routes.Database, requestParams, cancellationToken);
		});
}
