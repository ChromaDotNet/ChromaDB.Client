using System.Diagnostics;
using System.Net;
using System.Text.Json;
using ChromaDB.Client.Common;
using ChromaDB.Client.Models;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

// SupportsSparseVectorIndexes says, before a collection is created, whether the client sends the indexes added with ifSupported. The
// synchronous method is for the callers that cannot await: it does not wait for the synchronization context of its caller, and after an
// answer of the server it answers at once.
[TestFixture]
public class SparseVectorIndexSupportTests
{
	const string Chroma = """{"max_batch_size":100,"supports_base64_encoding":false}""";
	const string Declared = """{"max_batch_size":100,"server":{"name":"VecturaDB","version":"1.0.0-preview.8","features":["sparse_vector_index","bm25"]}}""";
	const string NotDeclared = """{"max_batch_size":100,"server":{"name":"VecturaDB","version":"1.0.0-preview.8","features":["search","fork"]}}""";
	const string WithoutFeatures = """{"max_batch_size":100,"server":{"name":"VecturaDB","version":"1.0.0-preview.8"}}""";
	const string Unreadable = "\"broken\"";

	// Both methods give the answer with which the client then creates a collection with an index added with ifSupported, whichever is
	// called first. Chroma Cloud costs no request, the other servers one for all the calls. An answer that cannot be read is not kept:
	// the asynchronous method and the create ask again, the synchronous method only after a while. Null is a server without
	// pre-flight-checks, like Chroma 0.4.10.
	[TestCase("https://api.trychroma.com", false, Chroma, true, 0)]
	[TestCase("http://localhost:8000", false, Chroma, false, 1)]
	[TestCase("http://localhost:8000", false, Declared, true, 1)]
	[TestCase("http://localhost:8000", false, NotDeclared, false, 1)]
	[TestCase("http://localhost:8000", false, WithoutFeatures, false, 1)]
	[TestCase("http://localhost:8000", false, null, false, 1)]
	[TestCase("http://localhost:8000", false, Unreadable, false, 4)]
	[TestCase("http://chroma.example.com:8000", true, Chroma, true, 1)]
	[TestCase("http://chroma.example.com:8000", true, Declared, true, 1)]
	[TestCase("http://chroma.example.com:8000", true, NotDeclared, false, 1)]
	[TestCase("http://chroma.example.com:8000", true, null, true, 1)]
	[TestCase("http://chroma.example.com:8000", true, Unreadable, true, 4)]
	public async Task AsTheClientSendsTheIndexes(string uri, bool distributed, string? preFlightChecks, bool expected, int requests)
	{
		foreach (var synchronousFirst in new[] { true, false })
		{
			var server = new Server(preFlightChecks);
			var client = new ChromaClient(new ChromaConfigurationOptions(uri).WithDistributedChroma(distributed), new HttpClient(server));
			var first = synchronousFirst ? client.SupportsSparseVectorIndexes() : await client.SupportsSparseVectorIndexesAsync();
			var second = synchronousFirst ? await client.SupportsSparseVectorIndexesAsync() : client.SupportsSparseVectorIndexes();
			await client.CreateCollectionAsync(new ChromaCollectionDefinition("c") { Schema = new ChromaCollectionSchema().WithBm25Index("title", ifSupported: true) });
			Assert.That((first, second, client.SupportsSparseVectorIndexes(), await client.SupportsSparseVectorIndexesAsync()), Is.EqualTo((expected, expected, expected, expected)));
			Assert.That(server.Schemas, Is.EqualTo(new[] { expected ? "title_bm25" : null }));
			Assert.That(server.Requests, Is.EqualTo(requests));
		}
	}

	// On a thread whose synchronization context runs nothing while the thread waits, as a UI thread or ASP.NET on .NET Framework, the
	// synchronous method gets the answer of a server that answers asynchronously.
	[Test]
	public void SynchronousUnderASynchronizationContext()
	{
		var client = new ChromaClient(new ChromaConfigurationOptions("http://localhost:8000"), new HttpClient(new Server(Declared) { Yields = true }));
		bool? answer = null;
		var thread = new Thread(() =>
		{
			SynchronizationContext.SetSynchronizationContext(new WaitingContext());
			answer = client.SupportsSparseVectorIndexes();
		})
		{ IsBackground = true };
		thread.Start();
		Assert.That(thread.Join(TimeSpan.FromSeconds(30)), Is.True, "The synchronous method waited for the synchronization context of its caller.");
		Assert.That(answer, Is.True);
	}

	// A server that does not answer: after the timeout of the probe, the answer is what the options say, and it is not kept. The
	// asynchronous method asks again at the next call; the synchronous one answers the options at once for a while, and then asks again.
	[TestCase(false, false)]
	[TestCase(true, true)]
	public async Task ServerThatDoesNotAnswer(bool distributed, bool expected)
	{
		var timeout = ChromaHttpClient.ProbeTimeout;
		var failed = ChromaHttpClient.FailedProbeLifetime;
		ChromaHttpClient.ProbeTimeout = TimeSpan.FromMilliseconds(200);
		ChromaHttpClient.FailedProbeLifetime = TimeSpan.FromSeconds(1);
		try
		{
			var server = new Server(Declared) { Hangs = true };
			var client = new ChromaClient(new ChromaConfigurationOptions("http://chroma.example.com:8000").WithDistributedChroma(distributed), new HttpClient(server));
			var watch = Stopwatch.StartNew();
			Assert.That(client.SupportsSparseVectorIndexes(), Is.EqualTo(expected));
			Assert.That(client.SupportsSparseVectorIndexes(), Is.EqualTo(expected));
			Assert.That(await client.SupportsSparseVectorIndexesAsync(), Is.EqualTo(expected));
			Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)));
			Assert.That(server.Requests, Is.EqualTo(2));
			server.Hangs = false;
			Thread.Sleep(TimeSpan.FromSeconds(1.5));
			Assert.That(client.SupportsSparseVectorIndexes(), Is.True);
			Assert.That(server.Requests, Is.EqualTo(3));
		}
		finally
		{
			ChromaHttpClient.ProbeTimeout = timeout;
			ChromaHttpClient.FailedProbeLifetime = failed;
		}
	}

	// A cancellation of the caller goes as it is, also before the timeout of the probe.
	[Test]
	public void CancellationOfTheCaller()
	{
		var client = new ChromaClient(new ChromaConfigurationOptions("http://localhost:8000"), new HttpClient(new Server(Declared) { Hangs = true }));
		using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
		Assert.That(() => client.SupportsSparseVectorIndexesAsync(cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
	}

	// After an answer of the server, the synchronous method answers at once, also when the server stops answering: when the
	// pre-flight-checks it read are no longer current, with them, while it asks for them again in the background, one request at a time:
	// the calls while that request waits ask nothing, also when it gets its answer.
	[Test]
	public void SynchronousAtOnceAfterAnAnswer()
	{
		var lifetime = ChromaHttpClient.ServerFacts.Lifetime;
		var timeout = ChromaHttpClient.ProbeTimeout;
		ChromaHttpClient.ServerFacts.Lifetime = TimeSpan.Zero;
		ChromaHttpClient.ProbeTimeout = TimeSpan.FromSeconds(10);
		try
		{
			var server = new Server(Declared);
			var client = new ChromaClient(new ChromaConfigurationOptions("http://localhost:8000"), new HttpClient(server));
			Assert.That(client.SupportsSparseVectorIndexes(), Is.True);
			server.Hangs = true;
			var watch = Stopwatch.StartNew();
			Assert.That(client.SupportsSparseVectorIndexes(), Is.True);
			WaitFor(() => server.Requests == 2);
			for (var i = 0; i < 5; i++)
			{
				Assert.That(client.SupportsSparseVectorIndexes(), Is.True);
			}
			Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
			server.Release();
			// Time for the requests that the calls would have left waiting.
			Thread.Sleep(500);
			Assert.That(server.Requests, Is.EqualTo(2));
		}
		finally
		{
			ChromaHttpClient.ServerFacts.Lifetime = lifetime;
			ChromaHttpClient.ProbeTimeout = timeout;
		}
	}

	// What the server answers in the background is the next answer of the synchronous method; the asynchronous one waits for it.
	[Test]
	public async Task SynchronousAfterTheRefresh()
	{
		var lifetime = ChromaHttpClient.ServerFacts.Lifetime;
		ChromaHttpClient.ServerFacts.Lifetime = TimeSpan.Zero;
		try
		{
			var server = new Server(Declared);
			var client = new ChromaClient(new ChromaConfigurationOptions("http://localhost:8000"), new HttpClient(server));
			Assert.That(client.SupportsSparseVectorIndexes(), Is.True);
			server.PreFlightChecks = Chroma;
			Assert.That(client.SupportsSparseVectorIndexes(), Is.True);
			WaitFor(() => !client.SupportsSparseVectorIndexes());
			Assert.That(await client.SupportsSparseVectorIndexesAsync(), Is.False);
		}
		finally
		{
			ChromaHttpClient.ServerFacts.Lifetime = lifetime;
		}
	}

	static void WaitFor(Func<bool> condition)
	{
		var watch = Stopwatch.StartNew();
		while (!condition())
		{
			Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)), "The client did not ask the server again in the background.");
			Thread.Sleep(10);
		}
	}

	// The synchronization context of a thread that waits: what is posted to it never runs.
	sealed class WaitingContext : SynchronizationContext
	{
		public override void Post(SendOrPostCallback d, object? state)
		{
		}
	}

	// Answers pre-flight-checks with PreFlightChecks, or 404 when it is null, and counts those requests; while Hangs, it answers them
	// only when they are canceled or after Release. It answers the version, and a new collection, recording the keys of its schema, or
	// null without one. With Yields, it answers after giving its thread back.
	sealed class Server(string? preFlightChecks) : HttpMessageHandler
	{
		int _requests;
		readonly TaskCompletionSource<bool> _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public string? PreFlightChecks { get; set; } = preFlightChecks;

		public bool Hangs { get; set; }

		public bool Yields { get; set; }

		public int Requests => Volatile.Read(ref _requests);

		public List<string?> Schemas { get; } = [];

		// The requests that wait get their answer, and the next ones do not wait.
		public void Release()
		{
			Hangs = false;
			_released.TrySetResult(true);
		}

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			if (Yields)
			{
				await Task.Yield();
			}
			var path = request.RequestUri!.AbsolutePath;
			if (path.EndsWith("/pre-flight-checks", StringComparison.Ordinal))
			{
				Interlocked.Increment(ref _requests);
				if (Hangs)
				{
					await Task.WhenAny(_released.Task, Task.Delay(Timeout.Infinite, cancellationToken));
					cancellationToken.ThrowIfCancellationRequested();
				}
				return PreFlightChecks is { } answer
					? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer) }
					: new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
			}
			if (path.EndsWith("/collections", StringComparison.Ordinal))
			{
				var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync()).RootElement;
				Schemas.Add(body.TryGetProperty("schema", out var schema) && schema.ValueKind == JsonValueKind.Object
					? string.Join(",", schema.GetProperty("keys").EnumerateObject().Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal))
					: null);
				return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"id":"11111111-2222-3333-4444-555555555555","name":"c","schema":{"defaults":{},"keys":{}}}""") };
			}
			return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(path.EndsWith("/version", StringComparison.Ordinal) ? "\"1.5.9\"" : "{}") };
		}
	}
}
