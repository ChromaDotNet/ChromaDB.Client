using System.Net;
using System.Text.Json;
using ChromaDB.Client.Models;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

[TestFixture]
public class MetadataAndErrorsTests
{
	static readonly ReadOnlyMemory<float> Embedding = new([1f, 0f]);
	const string Metadata = """{"ids":["a"],"metadatas":[{"date":"2026-10-04","text":"t","int":1,"float":1.5,"bool":true,"texts":["x","y"],"ints":[1,2],"floats":[1.5,2.25],"bools":[true,false],"empty":[]}]}""";

	[Test]
	public async Task MetadataValuesAreInferredByDefault()
	{
		var metadata = (await CollectionClient(new ChromaConfigurationOptions("http://localhost:8000"), Respond(Metadata)).Get()).Single().Metadata!;
		Assert.That(metadata["date"], Is.EqualTo(new DateTime(2026, 10, 4)));
		Assert.That(metadata["texts"], Is.InstanceOf<JsonElement>());
	}

	[Test]
	public async Task ExactMetadataValues()
	{
		var options = new ChromaConfigurationOptions("http://localhost:8000").WithMetadataValues(ChromaMetadataValues.Exact);
		var metadata = (await CollectionClient(options, Respond(Metadata)).Get()).Single().Metadata!;
		Assert.That(metadata["date"], Is.EqualTo("2026-10-04"));
		Assert.That(metadata["text"], Is.EqualTo("t"));
		Assert.That(metadata["int"], Is.EqualTo(1L));
		Assert.That(metadata["float"], Is.EqualTo(1.5));
		Assert.That(metadata["bool"], Is.EqualTo(true));
		Assert.That(metadata["texts"], Is.EqualTo(new List<object> { "x", "y" }));
		Assert.That(metadata["ints"], Is.EqualTo(new List<object> { 1L, 2L }));
		Assert.That(metadata["floats"], Is.EqualTo(new List<object> { 1.5, 2.25 }));
		Assert.That(metadata["bools"], Is.EqualTo(new List<object> { true, false }));
		Assert.That(metadata["empty"], Is.EqualTo(new List<object>()));
	}

	[Test]
	public async Task ExactMetadataValuesOfCollections()
	{
		var options = new ChromaConfigurationOptions("http://localhost:8000").WithMetadataValues(ChromaMetadataValues.Exact);
		using var httpClient = new HttpClient(Respond("""{"id":"11111111-2222-3333-4444-555555555555","name":"c","metadata":{"date":"2026-10-04","texts":["x"]}}"""));
		var collection = await new ChromaClient(options, httpClient).GetCollection("c");
		Assert.That(collection.Metadata!["date"], Is.EqualTo("2026-10-04"));
		Assert.That(collection.Metadata["texts"], Is.EqualTo(new List<object> { "x" }));
	}

	// A ChromaClient from a container may read the default way: WithMetadataValues gives one that reads exactly.
	[Test]
	public async Task ClientWithMetadataValues()
	{
		using var httpClient = new HttpClient(Respond("""{"id":"11111111-2222-3333-4444-555555555555","name":"c","metadata":{"date":"2026-10-04"}}"""));
		var inferred = new ChromaClient(new ChromaConfigurationOptions("http://localhost:8000", defaultTenant: "t"), httpClient);
		var exact = inferred.WithMetadataValues(ChromaMetadataValues.Exact);
		Assert.That((await exact.GetCollection("c")).Metadata!["date"], Is.EqualTo("2026-10-04"));
		Assert.That((await inferred.GetCollection("c")).Metadata!["date"], Is.EqualTo(new DateTime(2026, 10, 4)));
		Assert.That(exact.Options.MetadataValues, Is.EqualTo(ChromaMetadataValues.Exact));
		Assert.That(exact.Options.Tenant, Is.EqualTo("t"));
		Assert.That(inferred.Options.MetadataValues, Is.EqualTo(ChromaMetadataValues.Inferred));
	}

	// The two clients share what they learned about the server: the version is asked once.
	[Test]
	public async Task ClientWithMetadataValuesSharesTheVersion()
	{
		var handler = new VersionHandler();
		handler.Release.SetResult(true);
		var inferred = new ChromaClient(new ChromaConfigurationOptions("http://localhost:8000"), new HttpClient(handler));
		var exact = inferred.WithMetadataValues(ChromaMetadataValues.Exact);
		var records = new ChromaRecords(["a"]) { Embeddings = [Embedding], Metadatas = [new() { ["tags"] = new[] { "x" } }] };
		await inferred.GetCollectionClient(Guid.NewGuid(), "a").Add(records);
		await exact.GetCollectionClient(Guid.NewGuid(), "b").Add(records);
		Assert.That(handler.VersionRequests, Is.EqualTo(1));
	}

	[Test]
	public void OtherOptionsKeepTheMetadataValues()
		=> Assert.That(new ChromaConfigurationOptions().WithMetadataValues(ChromaMetadataValues.Exact).WithTenant("t").WithApiVersion(ChromaApiVersion.V1).MetadataValues, Is.EqualTo(ChromaMetadataValues.Exact));

	[TestCase(HttpStatusCode.NotFound, """{"error":"NotFoundError","message":"Collection [c] does not exist"}""")]
	[TestCase(HttpStatusCode.BadRequest, """{"error":"InvalidCollection","message":"Collection c does not exist."}""")]
	[TestCase(HttpStatusCode.InternalServerError, """{"error":"ValueError('Collection c does not exist.')"}""")]
	[TestCase(HttpStatusCode.NotFound, "")]
	public async Task StatusCodeOfTheAnswer(HttpStatusCode statusCode, string body)
	{
		using var httpClient = new HttpClient(new FixedHandler(statusCode, body));
		var ex = Assert.ThrowsAsync<ChromaException>(() => new ChromaClient(new ChromaConfigurationOptions("http://localhost:8000"), httpClient).GetCollection("c"));
		Assert.That(ex!.StatusCode, Is.EqualTo(statusCode));
	}

	[Test]
	public async Task NoStatusCodeOnTimeout()
	{
		using var httpClient = new HttpClient(new PendingHandler()) { Timeout = TimeSpan.FromMilliseconds(100) };
		var ex = Assert.ThrowsAsync<ChromaException>(() => new ChromaClient(new ChromaConfigurationOptions("http://localhost:8000"), httpClient).Heartbeat());
		Assert.That(ex!.StatusCode, Is.Null);
	}

	[TestCase(HttpStatusCode.OK, """{"id":"11111111-2222-3333-4444-555555555555","name":"c"}""", true)]
	[TestCase(HttpStatusCode.NotFound, """{"error":"NotFoundError","message":"Collection [c] does not exists"}""", false)]
	[TestCase(HttpStatusCode.BadRequest, """{"error":"InvalidCollection","message":"Collection c does not exist."}""", false)]
	[TestCase(HttpStatusCode.InternalServerError, """{"error":"ValueError('Collection c does not exist.')"}""", false)]
	public async Task CollectionExists(HttpStatusCode statusCode, string body, bool expected)
	{
		using var httpClient = new HttpClient(new FixedHandler(statusCode, body));
		Assert.That(await new ChromaClient(new ChromaConfigurationOptions("http://localhost:8000"), httpClient).CollectionExists("c"), Is.EqualTo(expected));
	}

	// Other errors are not a missing collection.
	[TestCase(HttpStatusCode.InternalServerError, """{"error":"InternalError","message":"Database is locked"}""")]
	[TestCase(HttpStatusCode.Unauthorized, """{"error":"AuthError","message":"Unauthorized"}""")]
	[TestCase(HttpStatusCode.NotFound, """{"detail":"Not Found"}""")]
	[TestCase(HttpStatusCode.NotFound, "")]
	[TestCase(HttpStatusCode.NotFound, """{"error":"NotFoundError","message":"Tenant [t] not found"}""")]
	public async Task CollectionExistsThrowsOnOtherErrors(HttpStatusCode statusCode, string body)
	{
		using var httpClient = new HttpClient(new FixedHandler(statusCode, body));
		await Assert.ThatAsync(() => new ChromaClient(new ChromaConfigurationOptions("http://localhost:8000"), httpClient).CollectionExists("c"), Throws.InstanceOf<ChromaException>());
	}

	[Test]
	public async Task ContainsAndNotContains()
	{
		var handler = Respond("""{"ids":[]}""");
		var client = CollectionClient(new ChromaConfigurationOptions("http://localhost:8000"), handler);
		await client.Get(where: ChromaWhereOperator.Contains("tags", "x"));
		Assert.That(handler.Bodies.Last().GetProperty("where").GetRawText(), Is.EqualTo("""{"tags":{"$contains":"x"}}"""));
		await client.Get(where: ChromaWhereOperator.NotContains("nums", 3));
		Assert.That(handler.Bodies.Last().GetProperty("where").GetRawText(), Is.EqualTo("""{"nums":{"$not_contains":3}}"""));
	}

	// Chroma 0.x stores the record but drops its lists: the client stops before sending them.
	[Test]
	public async Task ListsInMetadataToChroma0Throw()
	{
		var handler = Respond("\"0.6.3\"");
		var client = CollectionClient(new ChromaConfigurationOptions("http://localhost:8000"), handler);
		var ex = Assert.ThrowsAsync<ChromaException>(() => client.Add(new ChromaRecords(["a"]) { Embeddings = [Embedding], Metadatas = [new() { ["tags"] = new[] { "x" } }] }));
		Assert.That(ex!.Message, Does.Contain("1.5.0"));
		Assert.That(ex.StatusCode, Is.Null);
		await Assert.ThatAsync(() => client.Upsert(new ChromaRecords(["a"]) { Embeddings = [Embedding], Metadatas = [new() { ["tags"] = new List<int> { 1 } }] }), Throws.InstanceOf<ChromaException>());
		await Assert.ThatAsync(() => client.Update(new ChromaRecords(["a"]) { Metadatas = [new() { ["tags"] = new List<bool> { true } }] }), Throws.InstanceOf<ChromaException>());
		// The version is asked once, and no record is sent.
		Assert.That(handler.Paths, Is.EqualTo(new[] { "/api/v2/version" }));
	}

	// A list read with the default ChromaMetadataValues.Inferred is a JsonElement: written back, it is a list too.
	[Test]
	public async Task JsonArraysInMetadataToChroma0Throw()
	{
		var handler = Respond("\"0.6.3\"");
		var client = CollectionClient(new ChromaConfigurationOptions("http://localhost:8000"), handler);
		var list = JsonDocument.Parse("""["x"]""").RootElement.Clone();
		Assert.ThrowsAsync<ChromaException>(() => client.Add(new ChromaRecords(["a"]) { Embeddings = [Embedding], Metadatas = [new() { ["tags"] = list }] }));
		Assert.That(handler.Paths, Is.EqualTo(new[] { "/api/v2/version" }));
	}

	[Test]
	public async Task ConcurrentListsAskTheVersionOnce()
	{
		var handler = new VersionHandler();
		var client = CollectionClient(new ChromaConfigurationOptions("http://localhost:8000"), handler);
		var adds = Enumerable.Range(0, 5).Select(_ => client.Add(new ChromaRecords(["a"]) { Embeddings = [Embedding], Metadatas = [new() { ["tags"] = new[] { "x" } }] })).ToList();
		handler.Release.SetResult(true);
		await Task.WhenAll(adds);
		Assert.That(handler.VersionRequests, Is.EqualTo(1));
	}

	// A canceled version request is not kept: the next call asks again.
	[Test]
	public async Task CanceledVersionRequestIsAskedAgain()
	{
		var handler = new VersionHandler();
		var client = CollectionClient(new ChromaConfigurationOptions("http://localhost:8000"), handler);
		using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
		await Assert.ThatAsync(() => client.Add(new ChromaRecords(["a"]) { Embeddings = [Embedding], Metadatas = [new() { ["tags"] = new[] { "x" } }] }, cts.Token), Throws.InstanceOf<OperationCanceledException>());
		handler.Release.SetResult(true);
		await client.Add(new ChromaRecords(["a"]) { Embeddings = [Embedding], Metadatas = [new() { ["tags"] = new[] { "x" } }] });
		Assert.That(handler.VersionRequests, Is.EqualTo(2));
	}

	[Test]
	public async Task ListsInMetadataToChroma1AreSent()
	{
		var handler = Respond("\"1.0.0\"");
		var client = CollectionClient(new ChromaConfigurationOptions("http://localhost:8000"), handler);
		await client.Add(new ChromaRecords(["a"]) { Embeddings = [Embedding], Metadatas = [new() { ["tags"] = new[] { "x" } }] });
		Assert.That(handler.Paths, Has.Count.EqualTo(2));
		Assert.That(handler.Paths[0], Is.EqualTo("/api/v2/version"));
		Assert.That(handler.Bodies.Last().GetProperty("metadatas")[0].GetProperty("tags").GetRawText(), Is.EqualTo("""["x"]"""));
	}

	// Without lists the version is not needed.
	[Test]
	public async Task MetadataWithoutListsDoesNotAskTheVersion()
	{
		var handler = Respond("true");
		var client = CollectionClient(new ChromaConfigurationOptions("http://localhost:8000"), handler);
		await client.Add(new ChromaRecords(["a"]) { Embeddings = [Embedding], Metadatas = [new() { ["text"] = "x", ["int"] = 1 }] });
		Assert.That(handler.Paths, Has.Count.EqualTo(1));
		Assert.That(handler.Paths[0], Does.EndWith("/add"));
	}

	static ChromaCollectionClient CollectionClient(ChromaConfigurationOptions options, HttpMessageHandler handler)
	{
		var collection = new ChromaCollection("collection") { Id = Guid.NewGuid() };
		return new ChromaCollectionClient(collection, options, new HttpClient(handler));
	}

	static RecordingHandler Respond(string body) => new(body);

	sealed class RecordingHandler(string response) : HttpMessageHandler
	{
		public List<string> Paths { get; } = [];
		public List<JsonElement> Bodies { get; } = [];

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Paths.Add(request.RequestUri!.AbsolutePath);
			if (request.Content is not null)
			{
				Bodies.Add(JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken)).RootElement.Clone());
			}
			return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response) };
		}
	}

	// Answers "1.0.0" to the version requests once Release is set, and true to the others.
	sealed class VersionHandler : HttpMessageHandler
	{
		int _versionRequests;
		public int VersionRequests => _versionRequests;
		public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			if (request.RequestUri!.AbsolutePath.EndsWith("/version"))
			{
				Interlocked.Increment(ref _versionRequests);
				await Release.Task.WaitAsync(cancellationToken);
				return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("\"1.0.0\"") };
			}
			return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("true") };
		}
	}

	sealed class FixedHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			=> Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(body) });
	}

	sealed class PendingHandler : HttpMessageHandler
	{
		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			await Task.Delay(Timeout.Infinite, cancellationToken);
			throw new InvalidOperationException();
		}
	}
}
