using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using ChromaDB.Client.DependencyInjection;
using ChromaDB.Client.Models;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

[TestFixture]
public class Base64AndLimitsTests
{
	static readonly ReadOnlyMemory<float> Embedding = new([0.1f, -2.5f, 3.25f, 1e-7f]);
	static readonly ChromaConfigurationOptions Options = new("http://localhost:8000");
	const string WithBase64 = """{"max_batch_size":1000,"supports_base64_encoding":true}""";
	const string WithoutBase64 = """{"max_batch_size":1000}""";

	[Test]
	public async Task EmbeddingsInBase64WhereTheServerDeclaresIt()
	{
		var handler = new Handler(WithBase64);
		var client = Client(Options, handler);
		await client.Add(["a"], embeddings: [Embedding]);
		await client.Update(["a"], embeddings: [Embedding]);
		await client.Upsert(["a"], embeddings: [Embedding]);
		Assert.That(handler.Paths.Select(Last), Is.EqualTo(new[] { "pre-flight-checks", "add", "update", "upsert" }));
		foreach (var body in handler.Bodies)
		{
			var base64 = body.GetProperty("embeddings")[0].GetString()!;
			// Little-endian float32 values, whatever the order of the bytes of the machine.
			var bytes = Convert.FromBase64String(base64);
			Assert.That(Enumerable.Range(0, bytes.Length / 4).Select(i => System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * 4))), Is.EqualTo(Embedding.ToArray()));
		}
	}

	// Queries always send numbers: the servers reject base64 there.
	[Test]
	public async Task QueriesSendNumbers()
	{
		var handler = new Handler(WithBase64);
		await Client(Options, handler).Query(Embedding);
		Assert.That(handler.Bodies.Single().GetProperty("query_embeddings")[0].GetRawText(), Is.EqualTo("[0.1,-2.5,3.25,1E-07]"));
	}

	[TestCase(WithoutBase64)]
	[TestCase("""{"max_batch_size":1000,"supports_base64_encoding":false}""")]
	[TestCase(null)]
	// An answer the client cannot read: the write still goes, with numbers.
	[TestCase("\"broken\"")]
	public async Task NumbersWhereTheServerDoesNotDeclareIt(string? preFlightChecks)
	{
		var handler = new Handler(preFlightChecks);
		await Client(Options, handler).Add(["a"], embeddings: [Embedding]);
		Assert.That(handler.Bodies.Single().GetProperty("embeddings")[0].GetRawText(), Is.EqualTo("[0.1,-2.5,3.25,1E-07]"));
	}

	// Without embeddings the client does not need to know.
	[Test]
	public async Task NoPreFlightChecksWithoutEmbeddings()
	{
		var handler = new Handler(WithBase64);
		await Client(Options.WithBatchSplitting(false), handler).Update(["a"], documents: ["d"]);
		Assert.That(handler.Paths.Select(Last), Is.EqualTo(new[] { "update" }));
	}

	[TestCase(2, """{"max_batch_size":1000}""", new[] { 2, 2, 1 })]
	[TestCase(3, """{"max_batch_size":2}""", new[] { 2, 2, 1 })]
	[TestCase(2, null, new[] { 2, 2, 1 })]
	[TestCase(10, """{"max_batch_size":1000}""", new[] { 5 })]
	public async Task BatchesOfTheSmallerLimit(int maxBatchSize, string? preFlightChecks, int[] batches)
	{
		var handler = new Handler(preFlightChecks);
		await Client(Options.WithBatchSplitting(maxBatchSize), handler).Add(["a", "b", "c", "d", "e"], embeddings: Enumerable.Repeat(Embedding, 5).ToList());
		Assert.That(handler.Bodies.Select(b => b.GetProperty("ids").GetArrayLength()), Is.EqualTo(batches));
	}

	[Test]
	public void MaxBatchSizeOption()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => Options.WithBatchSplitting(0));
		Assert.Throws<ArgumentOutOfRangeException>(() => _ = new ChromaConfigurationOptions { BatchSplitting = true, MaxBatchSize = 0 });
		var options = Options.WithBatchSplitting(300).WithTenant("t");
		Assert.That(options.BatchSplitting, Is.True);
		Assert.That(options.MaxBatchSize, Is.EqualTo(300));
		Assert.That(Options.WithBatchSplitting().MaxBatchSize, Is.Null);
	}

	[TestCase(HttpStatusCode.NotFound, """{"error":"NotFoundError","message":"Collection [c] does not exist"}""", "NotFoundError", "Collection [c] does not exist")]
	[TestCase(HttpStatusCode.BadRequest, """{"error":"InvalidCollection","message":"Collection c does not exist."}""", "InvalidCollection", "Collection c does not exist.")]
	[TestCase(HttpStatusCode.InternalServerError, """{"error":"ValueError('Collection c does not exist.')"}""", "ValueError", "Collection c does not exist.")]
	[TestCase(HttpStatusCode.InternalServerError, """{"detail":"Internal Server Error"}""", null, "Internal Server Error")]
	[TestCase(HttpStatusCode.NotFound, """{"error":"NotFoundError","detail":"Not found"}""", "NotFoundError", "Not found")]
	// The validation errors of FastAPI, from the 0.x servers.
	[TestCase((HttpStatusCode)422, """{"detail":[{"type":"int_parsing","loc":["body","n_results"],"msg":"Input should be a valid integer","input":"x"}]}""", null, "body.n_results: Input should be a valid integer")]
	[TestCase((HttpStatusCode)422, """{"detail":[{"loc":["body","include",0],"msg":"Input should be 'documents'"},{"loc":["body","include",0],"msg":"Input should be 'embeddings'"}]}""", null, "body.include.0: Input should be 'documents'; body.include.0: Input should be 'embeddings'")]
	[TestCase((HttpStatusCode)422, """{"detail":[{"msg":"Field required"}]}""", null, "Field required")]
	[TestCase(HttpStatusCode.NotFound, """{"error":"NotFoundError"}""", "NotFoundError", "Couldn't identify the error message: {\"error\":\"NotFoundError\"}")]
	[TestCase(HttpStatusCode.NotFound, "", null, "NotFound: GET /api/v2/tenants/default_tenant/databases/default_database/collections/c")]
	public async Task ErrorTypeOfTheServer(HttpStatusCode statusCode, string body, string? errorType, string message)
	{
		using var httpClient = new HttpClient(new FixedHandler(statusCode, body));
		var ex = Assert.ThrowsAsync<ChromaException>(() => new ChromaClient(Options, httpClient).GetCollection("c"));
		Assert.That(ex!.ErrorType, Is.EqualTo(errorType));
		Assert.That(ex.Message, Is.EqualTo(message));
		await Task.CompletedTask;
	}

	// A missing collection named by the kind of error, even if the wording of the message changes.
	[TestCase("""{"error":"NotFoundError","message":"Collection [c] not found"}""", false)]
	[TestCase("""{"error":"NotFoundError","message":"Tenant [t] not found"}""", null)]
	public async Task CollectionExistsByTheErrorType(string body, bool? expected)
	{
		using var httpClient = new HttpClient(new FixedHandler(HttpStatusCode.NotFound, body));
		var client = new ChromaClient(Options, httpClient);
		if (expected is { } exists)
		{
			Assert.That(await client.CollectionExists("c"), Is.EqualTo(exists));
		}
		else
		{
			Assert.ThrowsAsync<ChromaException>(() => client.CollectionExists("c"));
		}
	}

	// The settings of the HttpClient reach the singleton: a handler in the pipeline and a timeout.
	[Test]
	public async Task ConfigureTheHttpClient()
	{
		var headers = new List<string?>();
		var services = new ServiceCollection();
		services.AddChromaClient(_ => Options, builder => builder
			.AddHttpMessageHandler(() => new HeaderHandler("plain", headers))
			.ConfigurePrimaryHttpMessageHandler(() => new HeartbeatOrWaitHandler())
			.ConfigureHttpClient(c => c.Timeout = TimeSpan.FromMilliseconds(300)));
		services.AddKeyedChromaClient("k", _ => Options, builder => builder
			.AddHttpMessageHandler(() => new HeaderHandler("keyed", headers))
			.ConfigurePrimaryHttpMessageHandler(() => new HeartbeatOrWaitHandler())
			.ConfigureHttpClient(c => c.Timeout = TimeSpan.FromMilliseconds(300)));
		using var provider = services.BuildServiceProvider();
		var plain = provider.GetRequiredService<ChromaClient>();
		var keyed = provider.GetRequiredKeyedService<ChromaClient>("k");
		await plain.Heartbeat();
		await keyed.Heartbeat();
		Assert.That(headers, Is.EqualTo(new[] { "plain", "keyed" }));
		// The timeout of 300 ms, not the 100 s of a default HttpClient.
		foreach (var client in new[] { plain, keyed })
		{
			var watch = System.Diagnostics.Stopwatch.StartNew();
			var timeout = Assert.ThrowsAsync<ChromaException>(() => client.GetVersion());
			Assert.That(timeout!.InnerException, Is.InstanceOf<TaskCanceledException>());
			Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)));
		}
	}

	static string Last(string path) => path.Substring(path.LastIndexOf('/') + 1);

	static ChromaCollectionClient Client(ChromaConfigurationOptions options, HttpMessageHandler handler)
		=> new(new ChromaCollection("collection") { Id = Guid.NewGuid() }, options, new HttpClient(handler));

	// Answers pre-flight-checks with the given body, or 404 when it is null, and the other requests as a server would.
	sealed class Handler(string? preFlightChecks) : HttpMessageHandler
	{
		public List<string> Paths { get; } = [];
		public List<JsonElement> Bodies { get; } = [];

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Paths.Add(request.RequestUri!.AbsolutePath);
			if (request.RequestUri.AbsolutePath.EndsWith("/pre-flight-checks"))
			{
				return preFlightChecks is null
					? new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("""{"detail":"Not Found"}""") }
					: new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(preFlightChecks) };
			}
			Bodies.Add(JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement.Clone());
			return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(request.RequestUri.AbsolutePath.EndsWith("/query") ? """{"ids":[[]]}""" : "true") };
		}
	}

	sealed class FixedHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			=> Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(body) });
	}

	sealed class HeaderHandler(string name, List<string?> headers) : DelegatingHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			if (request.RequestUri!.AbsolutePath.EndsWith("/heartbeat"))
			{
				lock (headers) headers.Add(name);
			}
			return base.SendAsync(request, cancellationToken);
		}
	}

	// Answers the heartbeat, and makes the other requests wait until they time out.
	sealed class HeartbeatOrWaitHandler : HttpMessageHandler
	{
		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			if (!request.RequestUri!.AbsolutePath.EndsWith("/heartbeat"))
			{
				await Task.Delay(Timeout.Infinite, cancellationToken);
			}
			return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"nanosecond heartbeat":1}""") };
		}
	}
}
