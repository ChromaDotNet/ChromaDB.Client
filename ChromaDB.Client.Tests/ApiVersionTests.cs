using System.Net;
using ChromaDB.Client.Models;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

[TestFixture]
public class ApiVersionTests
{
	const string Id = "11111111-2222-3333-4444-555555555555";
	static readonly ReadOnlyMemory<float> Embedding = new([1f, 0f]);

	static IEnumerable<TestCaseData> Requests()
	{
		yield return Case("ListCollections", c => c.ListCollectionsAsync(), "GET", "tenants/t/databases/d/collections", "collections?tenant=t&database=d");
		yield return Case("ListCollectionsPage", c => c.ListCollectionsAsync(limit: 2, offset: 1), "GET", "tenants/t/databases/d/collections?limit=2&offset=1", "collections?tenant=t&database=d&limit=2&offset=1");
		yield return Case("GetCollection", c => c.GetCollectionAsync("c"), "GET", "tenants/t/databases/d/collections/c", "collections/c?tenant=t&database=d");
		yield return Case("CollectionExists", c => c.CollectionExistsAsync("c"), "GET", "tenants/t/databases/d/collections/c", "collections/c?tenant=t&database=d");
		yield return Case("GetCollectionById", c => c.GetCollectionByIdAsync(Guid.Parse(Id)), "GET", $"tenants/t/databases/d/collections/by-id/{Id}", $"collections/by-id/{Id}?tenant=t&database=d");
		yield return Case("Heartbeat", c => c.HeartbeatAsync(), "GET", "heartbeat", "heartbeat");
		yield return Case("CreateCollection", c => c.CreateCollectionAsync("c"), "POST", "tenants/t/databases/d/collections", "collections?tenant=t&database=d");
		yield return Case("CreateCollectionDefinition", c => c.CreateCollectionAsync(new ChromaCollectionDefinition("c") { Configuration = new() { Space = ChromaSpace.Cosine } }), "POST", "tenants/t/databases/d/collections", "collections?tenant=t&database=d");
		yield return Case("GetOrCreateCollectionDefinition", c => c.GetOrCreateCollectionAsync(new ChromaCollectionDefinition("c")), "POST", "tenants/t/databases/d/collections", "collections?tenant=t&database=d");
		yield return Case("GetOrCreateCollection", c => c.GetOrCreateCollectionAsync("c"), "POST", "tenants/t/databases/d/collections", "collections?tenant=t&database=d");
		yield return Case("DeleteCollection", c => c.DeleteCollectionAsync("c"), "DELETE", "tenants/t/databases/d/collections/c", "collections/c?tenant=t&database=d");
		yield return Case("GetVersion", c => c.GetVersionAsync(), "GET", "version", "version");
		yield return Case("GetUserIdentity", c => c.GetUserIdentityAsync(), "GET", "auth/identity", "auth/identity");
		yield return Case("GetPreFlightChecks", c => c.GetPreFlightChecksAsync(), "GET", "pre-flight-checks", "pre-flight-checks");
		yield return Case("Reset", c => c.ResetAsync(), "POST", "reset", "reset");
		yield return Case("CountCollections", c => c.CountCollectionsAsync(), "GET", "tenants/t/databases/d/collections_count", "count_collections?tenant=t&database=d");
		yield return Case("CreateTenant", c => c.CreateTenantAsync("t"), "POST", "tenants", "tenants");
		yield return Case("GetTenant", c => c.GetTenantAsync("t"), "GET", "tenants/t", "tenants/t");
		yield return Case("CreateDatabase", c => c.CreateDatabaseAsync("d"), "POST", "tenants/t/databases", "databases?tenant=t");
		yield return Case("GetDatabase", c => c.GetDatabaseAsync("d"), "GET", "tenants/t/databases/d", "databases/d?tenant=t");
		yield return Case("ListDatabases", c => c.ListDatabasesAsync(), "GET", "tenants/t/databases", "databases?tenant=t");
		yield return Case("ListDatabasesPage", c => c.ListDatabasesAsync(limit: 2, offset: 1), "GET", "tenants/t/databases?limit=2&offset=1", "databases?tenant=t&limit=2&offset=1");
		yield return Case("DeleteDatabase", c => c.DeleteDatabaseAsync("d"), "DELETE", "tenants/t/databases/d", "databases/d?tenant=t");
		yield return Case("Get", c => c.GetAsync(), "POST", $"tenants/t/databases/d/collections/{Id}/get", $"collections/{Id}/get");
		yield return Case("Query", c => c.QueryAsync(Embedding), "POST", $"tenants/t/databases/d/collections/{Id}/query", $"collections/{Id}/query");
		yield return Case("QueryWithIds", c => c.QueryAsync(new ChromaQuery([Embedding]) { Ids = ["a"] }), "POST", $"tenants/t/databases/d/collections/{Id}/query", $"collections/{Id}/query");
		yield return Case("Add", c => c.AddAsync(["a"], embeddings: [Embedding]), "POST", $"tenants/t/databases/d/collections/{Id}/add", $"collections/{Id}/add");
		yield return Case("Update", c => c.UpdateAsync(["a"]), "POST", $"tenants/t/databases/d/collections/{Id}/update", $"collections/{Id}/update");
		yield return Case("Upsert", c => c.UpsertAsync(["a"], embeddings: [Embedding]), "POST", $"tenants/t/databases/d/collections/{Id}/upsert", $"collections/{Id}/upsert");
		yield return Case("Delete", c => c.DeleteAsync(["a"]), "POST", $"tenants/t/databases/d/collections/{Id}/delete", $"collections/{Id}/delete");
		yield return Case("Count", c => c.CountAsync(), "GET", $"tenants/t/databases/d/collections/{Id}/count", $"collections/{Id}/count");
		yield return Case("CountFromGetCollectionClient", c => c.GetCollectionClient(Guid.Parse(Id), "c").CountAsync(), "GET", $"tenants/t/databases/d/collections/{Id}/count", $"collections/{Id}/count");
		yield return Case("Peek", c => c.PeekAsync(), "POST", $"tenants/t/databases/d/collections/{Id}/get", $"collections/{Id}/get");
		yield return Case("Modify", c => c.ModifyAsync(name: "c2"), "PUT", $"tenants/t/databases/d/collections/{Id}", $"collections/{Id}");
	}

	static TestCaseData Case(string name, Func<ChromaClient, Task> call, string method, string v2, string v1)
		=> new TestCaseData(new Func<ChromaClient, ChromaCollectionClient, Task>((c, _) => call(c)), method, v2, v1).SetName($"ChromaClient.{name}");

	static TestCaseData Case(string name, Func<ChromaCollectionClient, Task> call, string method, string v2, string v1)
		=> new TestCaseData(new Func<ChromaClient, ChromaCollectionClient, Task>((_, c) => call(c)), method, v2, v1).SetName($"ChromaCollectionClient.{name}");

	[TestCaseSource(nameof(Requests))]
	public async Task V2(Func<ChromaClient, ChromaCollectionClient, Task> call, string method, string v2, string v1)
		=> await AssertRequest(ChromaApiVersion.V2, call, method, "/api/v2/" + v2);

	[TestCaseSource(nameof(Requests))]
	public async Task V1(Func<ChromaClient, ChromaCollectionClient, Task> call, string method, string v2, string v1)
		=> await AssertRequest(ChromaApiVersion.V1, call, method, "/api/v1/" + v1);

	// The default URI is the address of the server, so it follows the chosen version.
	[TestCase(ChromaApiVersion.V2, "http://localhost:8000/api/v2/heartbeat")]
	[TestCase(ChromaApiVersion.V1, "http://localhost:8000/api/v1/heartbeat")]
	public async Task DefaultOptions(ChromaApiVersion apiVersion, string expected)
	{
		var handler = new RecordingHandler();
		using var httpClient = new HttpClient(handler);
		try
		{
			await new ChromaClient(new ChromaConfigurationOptions().WithApiVersion(apiVersion), httpClient).HeartbeatAsync();
		}
		catch (ChromaException)
		{
		}
		Assert.That(handler.Uri, Is.EqualTo(expected));
	}

	[Test]
	public void DefaultIsV2()
		=> Assert.That(new ChromaConfigurationOptions().ApiVersion, Is.EqualTo(ChromaApiVersion.V2));

	static async Task AssertRequest(ChromaApiVersion apiVersion, Func<ChromaClient, ChromaCollectionClient, Task> call, string method, string expected)
	{
		var handler = new RecordingHandler();
		using var httpClient = new HttpClient(handler);
		var options = new ChromaConfigurationOptions("http://localhost:8000", defaultTenant: "t", defaultDatabase: "d").WithApiVersion(apiVersion);
		var collection = new ChromaCollection("c") { Id = Guid.Parse(Id) };
		try
		{
			await call(new ChromaClient(options, httpClient), new ChromaCollectionClient(collection, options, httpClient));
		}
		catch (ChromaException)
		{
			// The empty answer does not fit every response type: only the request matters here.
		}
		Assert.That(handler.Method, Is.EqualTo(method));
		Assert.That(handler.PathAndQuery, Is.EqualTo(expected));
	}

	sealed class RecordingHandler : HttpMessageHandler
	{
		public string? Method { get; private set; }
		public string? PathAndQuery { get; private set; }
		public string? Uri { get; private set; }

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Method = request.Method.Method;
			PathAndQuery = request.RequestUri?.PathAndQuery;
			Uri = request.RequestUri?.AbsoluteUri;
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
		}
	}
}
