using System.Net;
using System.Text.Json;
using ChromaDB.Client.Models;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

// The requests of the operations of the v2 API as the OpenAPI description of Chroma 1.5.9 defines them, against a fake server:
// some of them exist only on Chroma Cloud.
[TestFixture]
public class ApiV2RequestsTests
{
	static readonly Guid Id = Guid.Parse("11111111-2222-3333-4444-555555555555");
	const string CollectionPath = "/api/v2/tenants/default_tenant/databases/default_database/collections/11111111-2222-3333-4444-555555555555";
	const string OpenApiWithDeleteLimit = """{"components":{"schemas":{"DeleteCollectionRecordsPayload":{"allOf":[{"properties":{"ids":{},"limit":{}}}]}}}}""";
	const string OpenApiWithoutDeleteLimit = """{"components":{"schemas":{"DeleteCollectionRecordsPayload":{"allOf":[{"properties":{"ids":{}}}]}}}}""";

	[Test]
	public async Task Healthcheck()
	{
		var server = new FakeServer(_ => (HttpStatusCode.OK, """{"is_executor_ready":true,"is_log_client_ready":false}"""));
		var result = await Client(server).Healthcheck();
		Assert.That(server.Requests.Single().Line, Is.EqualTo("GET /api/v2/healthcheck"));
		Assert.That(result.IsExecutorReady, Is.True);
		Assert.That(result.IsLogClientReady, Is.False);
	}

	[Test]
	public async Task GetCollectionByCrn()
	{
		var server = new FakeServer(_ => (HttpStatusCode.OK, $$"""{"id":"{{Id}}","name":"c"}"""));
		var result = await Client(server).GetCollectionByCrn("tenant:database:c");
		Assert.That(Uri.UnescapeDataString(server.Requests.Single().Line), Is.EqualTo("GET /api/v2/collections/tenant:database:c"));
		Assert.That(result.Id, Is.EqualTo(Id));
	}

	[Test]
	public async Task UpdateTenant()
	{
		var server = new FakeServer(_ => (HttpStatusCode.OK, "{}"));
		await Client(server).UpdateTenant("t", "resource");
		Assert.That(server.Requests.Single().Line, Is.EqualTo("PATCH /api/v2/tenants/t"));
		Assert.That(server.Requests.Single().Body.GetProperty("resource_name").GetString(), Is.EqualTo("resource"));
	}

	[Test]
	public async Task ResourceNameOfTheTenant()
	{
		var server = new FakeServer(_ => (HttpStatusCode.OK, """{"name":"t","resource_name":"resource"}"""));
		Assert.That((await Client(server).GetTenant("t")).ResourceName, Is.EqualTo("resource"));
	}

	[Test]
	public void RegexFilters()
	{
		Assert.That(ChromaWhereDocumentOperator.Regex("^a").ToString(), Is.EqualTo("""{"$regex":"^a"}"""));
		Assert.That(ChromaWhereDocumentOperator.NotRegex("^a").ToString(), Is.EqualTo("""{"$not_regex":"^a"}"""));
		Assert.That((ChromaWhereDocumentOperator.Regex("^a") | ChromaWhereDocumentOperator.Contains("b")).ToString(), Is.EqualTo("""{"$or":[{"$regex":"^a"},{"$contains":"b"}]}"""));
	}

	[Test]
	public async Task DeleteWithLimitAndFilters()
	{
		var server = new FakeServer(r => r.Path == "/openapi.json" ? (HttpStatusCode.OK, OpenApiWithDeleteLimit) : (HttpStatusCode.OK, """{"deleted":2}"""));
		var deleted = await CollectionClient(server).Delete(new ChromaDelete { WhereDocument = ChromaWhereDocumentOperator.Contains("x"), Limit = 2 });
		Assert.That(deleted, Is.EqualTo(2));
		var delete = server.Requests.Single(x => x.Path.EndsWith("/delete"));
		Assert.That(delete.Line, Is.EqualTo($"POST {CollectionPath}/delete"));
		Assert.That(delete.Body.GetProperty("limit").GetInt32(), Is.EqualTo(2));
		Assert.That(delete.Body.GetProperty("where_document").GetProperty("$contains").GetString(), Is.EqualTo("x"));
		Assert.That(delete.Body.TryGetProperty("ids", out _), Is.False);
	}

	// The servers that do not declare the limit ignore it and would delete every matching record: nothing is sent.
	[TestCase(HttpStatusCode.OK, OpenApiWithoutDeleteLimit)]
	[TestCase(HttpStatusCode.NotFound, "")]
	public async Task DeleteWithLimitOnAServerThatIgnoresIt(HttpStatusCode status, string description)
	{
		var server = new FakeServer(r => r.Path == "/openapi.json" ? (status, description) : (HttpStatusCode.OK, "{}"));
		await Assert.ThatAsync(() => CollectionClient(server).Delete(new ChromaDelete { Ids = ["a"], WhereDocument = ChromaWhereDocumentOperator.Contains("x"), Limit = 1 }),
			Throws.InstanceOf<ChromaException>().With.Message.Contains("Chroma 1.5.3"));
		Assert.That(server.Requests.Any(x => x.Path.EndsWith("/delete")), Is.False);
	}

	// The limit left after each batch goes in the next one, and the batches stop when it is used up.
	[Test]
	public async Task DeleteWithLimitAcrossBatches()
	{
		var server = new FakeServer(r => r.Path switch
		{
			"/openapi.json" => (HttpStatusCode.OK, OpenApiWithDeleteLimit),
			"/api/v2/pre-flight-checks" => (HttpStatusCode.OK, """{"max_batch_size":2}"""),
			_ => (HttpStatusCode.OK, $$"""{"deleted":{{Math.Min(r.Body.GetProperty("ids").GetArrayLength(), r.Body.GetProperty("limit").GetInt32())}}}"""),
		});
		var client = CollectionClient(server, new ChromaConfigurationOptions("http://localhost:8000").WithBatchSplitting());
		var deleted = await client.Delete(new ChromaDelete { Ids = ["a", "b", "c", "d", "e"], WhereDocument = ChromaWhereDocumentOperator.Contains("x"), Limit = 3 });
		Assert.That(deleted, Is.EqualTo(3));
		var limits = server.Requests.Where(x => x.Path.EndsWith("/delete")).Select(x => x.Body.GetProperty("limit").GetInt32());
		Assert.That(limits, Is.EqualTo(new[] { 3, 1 }));
	}

	// {} from Chroma 1.0.0 to 1.5.2, null from 0.x, and a list of ids from some older 0.x servers: the count is unknown.
	[TestCase("{}")]
	[TestCase("null")]
	[TestCase("""["a"]""")]
	public async Task DeleteWithoutTheCount(string response)
	{
		var server = new FakeServer(_ => (HttpStatusCode.OK, response));
		Assert.That(await CollectionClient(server).Delete(new ChromaDelete { Ids = ["a"] }), Is.Null);
	}

	// What Chroma or its Python client reject: nothing is sent, not even the request for the OpenAPI description.
	[Test]
	public async Task DeleteThatChromaRejects()
	{
		var server = new FakeServer(_ => (HttpStatusCode.OK, OpenApiWithDeleteLimit));
		var client = CollectionClient(server);
		var filter = ChromaWhereDocumentOperator.Contains("x");
		await Assert.ThatAsync(() => client.Delete(new ChromaDelete()), Throws.ArgumentException);
		await Assert.ThatAsync(() => client.Delete(new ChromaDelete { Ids = [] }), Throws.ArgumentException);
		await Assert.ThatAsync(() => client.Delete(new ChromaDelete { Ids = [], WhereDocument = filter }), Throws.ArgumentException);
		await Assert.ThatAsync(() => client.Delete(new ChromaDelete { Ids = ["a"], Limit = 1 }), Throws.ArgumentException);
		await Assert.ThatAsync(() => client.Delete(new ChromaDelete { WhereDocument = filter, Limit = -1 }), Throws.InstanceOf<ArgumentOutOfRangeException>());
		Assert.That(server.Requests, Is.Empty);
	}

	// Chroma answers {"deleted":0} to a limit of 0.
	[Test]
	public async Task DeleteWithLimitZero()
	{
		var server = new FakeServer(r => r.Path == "/openapi.json" ? (HttpStatusCode.OK, OpenApiWithDeleteLimit) : (HttpStatusCode.OK, """{"deleted":0}"""));
		Assert.That(await CollectionClient(server).Delete(new ChromaDelete { WhereDocument = ChromaWhereDocumentOperator.Contains("x"), Limit = 0 }), Is.EqualTo(0));
		Assert.That(server.Requests.Single(x => x.Path.EndsWith("/delete")).Body.GetProperty("limit").GetInt32(), Is.EqualTo(0));
	}

	[TestCase(ChromaReadLevel.IndexAndWal, "index_and_wal")]
	[TestCase(ChromaReadLevel.IndexOnly, "index_only")]
	[TestCase(ChromaReadLevel.IndexAndBoundedWal, "index_and_bounded_wal")]
	public async Task CountAtAReadLevel(ChromaReadLevel readLevel, string name)
	{
		var server = new FakeServer(_ => (HttpStatusCode.OK, "4"));
		Assert.That(await CollectionClient(server).Count(readLevel), Is.EqualTo(4));
		Assert.That(server.Requests.Single().Line, Is.EqualTo($"GET {CollectionPath}/count?read_level={name}"));
	}

	// A single server sends "hnsw" with "spann" null, Chroma Cloud the other way around.
	[TestCase("""{"hnsw":{"ef_search":100},"spann":null}""", """{"hnsw":{"ef_search":200}}""")]
	[TestCase("""{"hnsw":null,"spann":{"search_nprobe":64}}""", """{"spann":{"search_nprobe":32}}""")]
	public async Task ModifyConfiguration(string configuration, string expected)
	{
		var server = new FakeServer(_ => (HttpStatusCode.OK, "{}"));
		var update = configuration.StartsWith("""{"hnsw":{""")
			? new ChromaCollectionConfigurationUpdate { Hnsw = new() { EfSearch = 200 } }
			: new ChromaCollectionConfigurationUpdate { Spann = new() { SearchNprobe = 32 } };
		await CollectionClient(server, collection: Collection(configuration)).ModifyConfiguration(update);
		var request = server.Requests.Single();
		Assert.That(request.Line, Is.EqualTo($"PUT {CollectionPath}"));
		Assert.That(request.Body.GetProperty("new_configuration").GetRawText(), Is.EqualTo(expected));
	}

	// Chroma Cloud answers 500 to HNSW settings on its SPANN index; a single server answers 200 to SPANN settings and keeps "spann" null.
	[TestCase("""{"hnsw":null,"spann":{"search_nprobe":64}}""", true, "set Spann")]
	[TestCase("""{"hnsw":{"ef_search":100},"spann":null}""", false, "set Hnsw")]
	public async Task ModifyConfigurationOfAnotherIndex(string configuration, bool hnsw, string message)
	{
		var server = new FakeServer(_ => (HttpStatusCode.OK, "{}"));
		var update = hnsw
			? new ChromaCollectionConfigurationUpdate { Hnsw = new() { EfSearch = 200 } }
			: new ChromaCollectionConfigurationUpdate { Spann = new() { SearchNprobe = 32 } };
		await Assert.ThatAsync(() => CollectionClient(server, collection: Collection(configuration)).ModifyConfiguration(update),
			Throws.InstanceOf<ChromaException>().With.Message.Contains(message));
		Assert.That(server.Requests, Is.Empty);
	}

	// Chroma 0.5.4 to 1.0.5 send "hnsw_configuration", 0.4.10 to 0.5.3 no configuration: they answer without applying it.
	[TestCase("""{"_type":"CollectionConfigurationInternal","hnsw_configuration":{"ef_search":10}}""")]
	[TestCase(null)]
	public async Task ModifyConfigurationOnAServerThatIgnoresIt(string? configuration)
	{
		var server = new FakeServer(_ => (HttpStatusCode.OK, "{}"));
		var collection = new ChromaCollection("c") { Id = Id, ConfigurationJson = configuration is null ? JsonDocument.Parse("null").RootElement : JsonDocument.Parse(configuration).RootElement };
		await Assert.ThatAsync(() => CollectionClient(server, collection: collection).ModifyConfiguration(new() { Hnsw = new() { EfSearch = 200 } }),
			Throws.InstanceOf<ChromaException>().With.Message.Contains("Chroma 1.0.6"));
		Assert.That(server.Requests.Any(x => x.Method == "PUT"), Is.False);
	}

	// A collection client made from the id and the name has no configuration at hand: it reads the collection first.
	[Test]
	public async Task ModifyConfigurationReadsTheCollection()
	{
		var server = new FakeServer(r => r.Method == "GET" ? (HttpStatusCode.OK, $$$$"""{"id":"{{{{Id}}}}","name":"c","configuration_json":{"hnsw":{}}}""") : (HttpStatusCode.OK, "{}"));
		await CollectionClient(server).ModifyConfiguration(new() { Hnsw = new() { EfSearch = 200 } });
		Assert.That(server.Requests.Select(x => x.Line), Is.EqualTo(new[] { "GET /api/v2/tenants/default_tenant/databases/default_database/collections/c", $"PUT {CollectionPath}" }));
	}

	[Test]
	public async Task Fork()
	{
		var server = new FakeServer(_ => (HttpStatusCode.OK, """{"id":"22222222-2222-3333-4444-555555555555","name":"copy"}"""));
		var result = await CollectionClient(server).Fork("copy");
		Assert.That(server.Requests.Single().Line, Is.EqualTo($"POST {CollectionPath}/fork"));
		Assert.That(server.Requests.Single().Body.GetProperty("new_name").GetString(), Is.EqualTo("copy"));
		Assert.That(result.Name, Is.EqualTo("copy"));
	}

	[Test]
	public async Task ForkCount()
	{
		var server = new FakeServer(_ => (HttpStatusCode.OK, """{"count":3}"""));
		Assert.That(await CollectionClient(server).ForkCount(), Is.EqualTo(3));
		Assert.That(server.Requests.Single().Line, Is.EqualTo($"GET {CollectionPath}/fork_count"));
	}

	[Test]
	public async Task IndexingStatus()
	{
		var server = new FakeServer(_ => (HttpStatusCode.OK, """{"op_indexing_progress":0.25,"num_unindexed_ops":3,"num_indexed_ops":1,"total_ops":4}"""));
		var result = await CollectionClient(server).GetIndexingStatus();
		Assert.That(server.Requests.Single().Line, Is.EqualTo($"GET {CollectionPath}/indexing_status"));
		Assert.That((result.OpIndexingProgress, result.NumUnindexedOps, result.NumIndexedOps, result.TotalOps), Is.EqualTo((0.25f, 3L, 1L, 4L)));
	}

	[Test]
	public async Task AttachFunction()
	{
		var server = new FakeServer(_ => (HttpStatusCode.OK, """{"attached_function":{"id":"33333333-2222-3333-4444-555555555555","name":"stats","function_name":"statistics"},"created":true}"""));
		var (attached, created) = await CollectionClient(server).AttachFunction(ChromaFunctions.Statistics, "stats", "stats_output", new() { ["k"] = 1 });
		var request = server.Requests.Single();
		Assert.That(request.Line, Is.EqualTo($"POST {CollectionPath}/functions/attach"));
		Assert.That(request.Body.GetRawText(), Is.EqualTo("""{"name":"stats","function_id":"statistics","output_collection":"stats_output","params":{"k":1}}"""));
		Assert.That((attached.Id, attached.Name, attached.FunctionName, created), Is.EqualTo((Guid.Parse("33333333-2222-3333-4444-555555555555"), "stats", "statistics", true)));
	}

	[TestCase(""","created":false""", false)]
	[TestCase("", true)]
	public async Task AttachFunctionCreated(string created, bool expected)
	{
		var server = new FakeServer(_ => (HttpStatusCode.OK, $$"""{"attached_function":{"id":"33333333-2222-3333-4444-555555555555","name":"stats","function_name":"statistics"}{{created}}}"""));
		Assert.That((await CollectionClient(server).AttachFunction(ChromaFunctions.Statistics, "stats", "stats_output")).Created, Is.EqualTo(expected));
	}

	[Test]
	public async Task GetAttachedFunction()
	{
		var server = new FakeServer(_ => (HttpStatusCode.OK, $$$"""
			{"attached_function":{"id":"33333333-2222-3333-4444-555555555555","name":"stats","function_name":"statistics","input_collection_id":"{{{Id}}}",
			"output_collection":"stats_output","output_collection_id":null,"tenant_id":"t","database_id":"d","params":"{\"k\":1}","completion_offset":7,"min_records_for_invocation":100}}
			"""));
		var result = await CollectionClient(server).GetAttachedFunction("stats");
		Assert.That(server.Requests.Single().Line, Is.EqualTo($"GET {CollectionPath}/functions/stats"));
		Assert.That((result.Name, result.FunctionName, result.InputCollectionId, result.OutputCollection, result.OutputCollectionId), Is.EqualTo(("stats", "statistics", (Guid?)Id, "stats_output", (Guid?)null)));
		Assert.That((result.Tenant, result.Database, result.Params, result.CompletionOffset, result.MinRecordsForInvocation), Is.EqualTo(("t", "d", """{"k":1}""", (long?)7, (long?)100)));
	}

	[Test]
	public async Task DetachFunction()
	{
		var server = new FakeServer(_ => (HttpStatusCode.OK, """{"success":true}"""));
		Assert.That(await CollectionClient(server).DetachFunction("stats", deleteOutputCollection: true), Is.True);
		Assert.That(server.Requests.Single().Line, Is.EqualTo($"POST {CollectionPath}/attached_functions/stats/detach"));
		Assert.That(server.Requests.Single().Body.GetProperty("delete_output").GetBoolean(), Is.True);
	}

	// Like the CloudClient of the Python client of Chroma: the tenant unless it is "*", the database only when there is exactly one.
	[TestCase("""{"tenant":"t1","databases":["d1"]}""", "t1", "d1")]
	[TestCase("""{"tenant":"t1","databases":["d1","d1"]}""", "t1", "d1")]
	[TestCase("""{"tenant":"t1","databases":[]}""", "t1", null)]
	[TestCase("""{"tenant":"t1","databases":["d1","d2"]}""", "t1", null)]
	[TestCase("""{"tenant":"*","databases":["*"]}""", null, null)]
	[TestCase("""{"tenant":null,"databases":null}""", null, null)]
	public async Task TenantAndDatabaseFromIdentity(string identity, string? tenant, string? database)
	{
		var server = new FakeServer(r => r.Path.EndsWith("/auth/identity") ? (HttpStatusCode.OK, identity) : (HttpStatusCode.OK, "[]"));
		var client = await Client(server).WithTenantAndDatabaseFromIdentity();
		Assert.That((client.Options.Tenant, client.Options.Database), Is.EqualTo((tenant, database)));
		await client.ListCollections();
		Assert.That(server.Requests.Last().Line, Is.EqualTo($"GET /api/v2/tenants/{tenant ?? "default_tenant"}/databases/{database ?? "default_database"}/collections"));
	}

	// A tenant or a database of the options other than the default one must match the credentials.
	[TestCase("other", null, "tenant other")]
	[TestCase(null, "other", "database other")]
	public async Task TenantAndDatabaseFromIdentityThatDoNotMatch(string? tenant, string? database, string message)
	{
		var server = new FakeServer(_ => (HttpStatusCode.OK, """{"tenant":"t1","databases":["d1"]}"""));
		var options = new ChromaConfigurationOptions("http://localhost:8000", defaultTenant: tenant, defaultDatabase: database);
		await Assert.ThatAsync(() => new ChromaClient(options, new HttpClient(server)).WithTenantAndDatabaseFromIdentity(),
			Throws.InstanceOf<ChromaException>().With.Message.Contains(message));
	}

	[Test]
	public async Task TenantAndDatabaseFromIdentityReplaceTheDefaults()
	{
		var server = new FakeServer(_ => (HttpStatusCode.OK, """{"tenant":"t1","databases":["d1"]}"""));
		var options = new ChromaConfigurationOptions("http://localhost:8000", defaultTenant: "default_tenant", defaultDatabase: "default_database");
		var client = await new ChromaClient(options, new HttpClient(server)).WithTenantAndDatabaseFromIdentity();
		Assert.That((client.Options.Tenant, client.Options.Database), Is.EqualTo(("t1", "d1")));
	}

	static ChromaClient Client(HttpMessageHandler handler)
		=> new(new ChromaConfigurationOptions("http://localhost:8000"), new HttpClient(handler));

	static ChromaCollection Collection(string configuration)
		=> new("c") { Id = Id, ConfigurationJson = JsonDocument.Parse(configuration).RootElement };

	static ChromaCollectionClient CollectionClient(HttpMessageHandler handler, ChromaConfigurationOptions? options = null, ChromaCollection? collection = null)
		=> new(collection ?? new ChromaCollection("c") { Id = Id }, options ?? new ChromaConfigurationOptions("http://localhost:8000"), new HttpClient(handler));

	sealed record Request(string Method, string Path, string Line, JsonElement Body);

	// Answers each request with the status and body the function returns for it, and keeps the requests.
	sealed class FakeServer(Func<Request, (HttpStatusCode Status, string Body)> answer) : HttpMessageHandler
	{
		public List<Request> Requests { get; } = [];

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var text = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
			var body = text is { Length: > 0 } ? JsonDocument.Parse(text).RootElement.Clone() : default;
			var path = request.RequestUri!.AbsolutePath;
			var recorded = new Request(request.Method.Method, path, $"{request.Method.Method} {request.RequestUri.PathAndQuery}", body);
			Requests.Add(recorded);
			var (status, response) = answer(recorded);
			return new HttpResponseMessage(status) { Content = new StringContent(response) };
		}
	}
}
