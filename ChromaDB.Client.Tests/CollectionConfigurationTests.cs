using System.Net;
using System.Text.Json;
using ChromaDB.Client.Models;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

[TestFixture]
public class CollectionConfigurationTests
{
	const string Created = """{"id":"11111111-2222-3333-4444-555555555555","name":"c"}""";

	[TestCase(ChromaSpace.L2, "l2")]
	[TestCase(ChromaSpace.Cosine, "cosine")]
	[TestCase(ChromaSpace.InnerProduct, "ip")]
	public async Task CreateSendsTheSpaceInTheMetadata(ChromaSpace space, string expected)
	{
		var handler = new RecordingHandler(Created);
		await Client(handler).CreateCollection(new ChromaCollectionDefinition("c") { Metadata = new() { ["key"] = "value" }, Configuration = new() { Space = space } });
		Assert.That(handler.Body.GetProperty("metadata").GetRawText(), Is.EqualTo($$"""{"key":"value","hnsw:space":"{{expected}}"}"""));
	}

	[Test]
	public async Task GetOrCreateSendsTheSpaceInTheMetadata()
	{
		var handler = new RecordingHandler(Created);
		await Client(handler).GetOrCreateCollection(new ChromaCollectionDefinition("c") { Configuration = new() { Space = ChromaSpace.Cosine } });
		Assert.That(handler.Body.GetProperty("metadata").GetRawText(), Is.EqualTo("""{"hnsw:space":"cosine"}"""));
		Assert.That(handler.Body.GetProperty("get_or_create").GetBoolean(), Is.True);
	}

	// The metadata of the caller is not changed.
	[Test]
	public async Task CreateKeepsTheMetadataOfTheCaller()
	{
		var metadata = new Dictionary<string, object> { ["key"] = "value" };
		await Client(new RecordingHandler(Created)).CreateCollection(new ChromaCollectionDefinition("c") { Metadata = metadata, Configuration = new() { Space = ChromaSpace.Cosine } });
		Assert.That(metadata.Keys, Is.EqualTo(new[] { "key" }));
	}

	[Test]
	public async Task CreateWithoutConfigurationSendsTheMetadataAsItIs()
	{
		var handler = new RecordingHandler(Created);
		await Client(handler).CreateCollection(new ChromaCollectionDefinition("c") { Metadata = new() { ["hnsw:space"] = "ip" } });
		Assert.That(handler.Body.GetProperty("metadata").GetRawText(), Is.EqualTo("""{"hnsw:space":"ip"}"""));
	}

	[Test]
	public void CreateWithTwoDifferentSpacesThrows()
		=> Assert.ThrowsAsync<ArgumentException>(() => Client(new RecordingHandler(Created)).CreateCollection(new ChromaCollectionDefinition("c") { Metadata = new() { ["hnsw:space"] = "ip" }, Configuration = new() { Space = ChromaSpace.Cosine } }));

	[Test]
	public async Task CreateWithTheSameSpaceTwiceIsAccepted()
	{
		var handler = new RecordingHandler(Created);
		await Client(handler).CreateCollection(new ChromaCollectionDefinition("c") { Metadata = new() { ["hnsw:space"] = "cosine" }, Configuration = new() { Space = ChromaSpace.Cosine } });
		Assert.That(handler.Body.GetProperty("metadata").GetRawText(), Is.EqualTo("""{"hnsw:space":"cosine"}"""));
	}

	[TestCase("""{"id":"11111111-2222-3333-4444-555555555555","name":"c","metadata":{"hnsw:space":"cosine"},"configuration_json":{"hnsw":{"space":"l2"}}}""", ChromaSpace.Cosine)]
	[TestCase("""{"id":"11111111-2222-3333-4444-555555555555","name":"c","metadata":null,"configuration_json":{"hnsw":{"space":"ip"},"spann":null}}""", ChromaSpace.InnerProduct)]
	[TestCase("""{"id":"11111111-2222-3333-4444-555555555555","name":"c","metadata":null,"configuration_json":{"_type":"CollectionConfigurationInternal","hnsw_configuration":{"space":"l2"}}}""", null)]
	[TestCase("""{"id":"11111111-2222-3333-4444-555555555555","name":"c","metadata":null}""", null)]
	public async Task SpaceOfTheCollection(string response, ChromaSpace? expected)
	{
		using var httpClient = new HttpClient(new RecordingHandler(response));
		var collection = await new ChromaClient(new ChromaConfigurationOptions("http://localhost:8000"), httpClient).GetCollection("c");
		Assert.That(collection.Space, Is.EqualTo(expected));
	}

	[Test]
	public async Task ConfigurationJsonAsTheServerSendsIt()
	{
		using var httpClient = new HttpClient(new RecordingHandler("""{"id":"11111111-2222-3333-4444-555555555555","name":"c","configuration_json":{"hnsw":{"space":"l2","ef_search":100}}}"""));
		var collection = await new ChromaClient(new ChromaConfigurationOptions("http://localhost:8000"), httpClient).GetCollection("c");
		Assert.That(collection.ConfigurationJson!.Value.GetProperty("hnsw").GetProperty("ef_search").GetInt32(), Is.EqualTo(100));
	}

	[Test]
	public async Task CollectionClientFromTheId()
	{
		var handler = new RecordingHandler("3");
		var id = Guid.NewGuid();
		var options = new ChromaConfigurationOptions("http://localhost:8000", defaultTenant: "t", defaultDatabase: "d");
		var client = new ChromaCollectionClient(id, "c", options, new HttpClient(handler));
		Assert.That(await client.Count(), Is.EqualTo(3));
		Assert.That(handler.Path, Is.EqualTo($"/api/v2/tenants/t/databases/d/collections/{id}/count"));
		Assert.That(client.Collection.Id, Is.EqualTo(id));
		Assert.That(client.Collection.Name, Is.EqualTo("c"));
	}

	[Test]
	public async Task GetCollectionClientUsesTheOptionsOfTheClient()
	{
		var handler = new RecordingHandler("3");
		var id = Guid.NewGuid();
		var client = new ChromaClient(new ChromaConfigurationOptions("http://localhost:8000", defaultTenant: "t", defaultDatabase: "d"), new HttpClient(handler));
		Assert.That(await client.GetCollectionClient(id, "c").Count(), Is.EqualTo(3));
		Assert.That(handler.Path, Is.EqualTo($"/api/v2/tenants/t/databases/d/collections/{id}/count"));
		Assert.That(await client.GetCollectionClient(new ChromaCollection("c") { Id = id, Tenant = "t2", Database = "d2" }).Count(), Is.EqualTo(3));
		Assert.That(handler.Path, Is.EqualTo($"/api/v2/tenants/t2/databases/d2/collections/{id}/count"));
	}

	// The collection clients of one ChromaClient share what it keeps: the version of the server is asked once.
	[Test]
	public async Task CollectionClientsOfAClientShareTheVersion()
	{
		var handler = new CountingHandler();
		var client = new ChromaClient(new ChromaConfigurationOptions("http://localhost:8000"), new HttpClient(handler));
		foreach (var name in new[] { "a", "b", "c" })
		{
			await client.GetCollectionClient(Guid.NewGuid(), name).Add(new ChromaRecords(["x"]) { Embeddings = [new([1f, 0f])], Metadatas = [new() { ["tags"] = new[] { "y" } }] });
		}
		Assert.That(handler.VersionRequests, Is.EqualTo(1));
		Assert.That(handler.AddRequests, Is.EqualTo(3));
	}

	static ChromaClient Client(HttpMessageHandler handler)
		=> new(new ChromaConfigurationOptions("http://localhost:8000"), new HttpClient(handler));

	sealed class CountingHandler : HttpMessageHandler
	{
		public int VersionRequests { get; private set; }
		public int AddRequests { get; private set; }

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var isVersion = request.RequestUri!.AbsolutePath.EndsWith("/version");
			if (isVersion) VersionRequests++; else AddRequests++;
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(isVersion ? "\"1.0.0\"" : "true") });
		}
	}

	sealed class RecordingHandler(string response) : HttpMessageHandler
	{
		public JsonElement Body { get; private set; }
		public string? Path { get; private set; }

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Path = request.RequestUri!.AbsolutePath;
			if (request.Content is not null)
			{
				Body = JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken)).RootElement.Clone();
			}
			return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response) };
		}
	}
}
