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
		yield return Case("ListCollections", c => c.ListCollections(), "GET", "tenants/t/databases/d/collections", "collections?tenant=t&database=d");
		yield return Case("ListCollectionsPage", c => c.ListCollections(limit: 2, offset: 1), "GET", "tenants/t/databases/d/collections?limit=2&offset=1", "collections?tenant=t&database=d&limit=2&offset=1");
		yield return Case("GetCollection", c => c.GetCollection("c"), "GET", "tenants/t/databases/d/collections/c", "collections/c?tenant=t&database=d");
		yield return Case("Heartbeat", c => c.Heartbeat(), "GET", "heartbeat", "heartbeat");
		yield return Case("CreateCollection", c => c.CreateCollection("c"), "POST", "tenants/t/databases/d/collections", "collections?tenant=t&database=d");
		yield return Case("GetOrCreateCollection", c => c.GetOrCreateCollection("c"), "POST", "tenants/t/databases/d/collections", "collections?tenant=t&database=d");
		yield return Case("DeleteCollection", c => c.DeleteCollection("c"), "DELETE", "tenants/t/databases/d/collections/c", "collections/c?tenant=t&database=d");
		yield return Case("GetVersion", c => c.GetVersion(), "GET", "version", "version");
		yield return Case("GetUserIdentity", c => c.GetUserIdentity(), "GET", "auth/identity", "auth/identity");
		yield return Case("Reset", c => c.Reset(), "POST", "reset", "reset");
		yield return Case("CountCollections", c => c.CountCollections(), "GET", "tenants/t/databases/d/collections_count", "count_collections?tenant=t&database=d");
		yield return Case("CreateTenant", c => c.CreateTenant("t"), "POST", "tenants", "tenants");
		yield return Case("GetTenant", c => c.GetTenant("t"), "GET", "tenants/t", "tenants/t");
		yield return Case("CreateDatabase", c => c.CreateDatabase("d"), "POST", "tenants/t/databases", "databases?tenant=t");
		yield return Case("GetDatabase", c => c.GetDatabase("d"), "GET", "tenants/t/databases/d", "databases/d?tenant=t");
		yield return Case("Get", c => c.Get(), "POST", $"tenants/t/databases/d/collections/{Id}/get", $"collections/{Id}/get");
		yield return Case("Query", c => c.Query(Embedding), "POST", $"tenants/t/databases/d/collections/{Id}/query", $"collections/{Id}/query");
		yield return Case("Add", c => c.Add(["a"], embeddings: [Embedding]), "POST", $"tenants/t/databases/d/collections/{Id}/add", $"collections/{Id}/add");
		yield return Case("Update", c => c.Update(["a"]), "POST", $"tenants/t/databases/d/collections/{Id}/update", $"collections/{Id}/update");
		yield return Case("Upsert", c => c.Upsert(["a"], embeddings: [Embedding]), "POST", $"tenants/t/databases/d/collections/{Id}/upsert", $"collections/{Id}/upsert");
		yield return Case("Delete", c => c.Delete(["a"]), "POST", $"tenants/t/databases/d/collections/{Id}/delete", $"collections/{Id}/delete");
		yield return Case("Count", c => c.Count(), "GET", $"tenants/t/databases/d/collections/{Id}/count", $"collections/{Id}/count");
		yield return Case("Peek", c => c.Peek(), "POST", $"tenants/t/databases/d/collections/{Id}/get", $"collections/{Id}/get");
		yield return Case("Modify", c => c.Modify(name: "c2"), "PUT", $"tenants/t/databases/d/collections/{Id}", $"collections/{Id}");
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
			await new ChromaClient(new ChromaConfigurationOptions().WithApiVersion(apiVersion), httpClient).Heartbeat();
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
