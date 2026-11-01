using System.Net;
using ChromaDB.Client.Models;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

// A write that failed may have gone, so it goes once, but for one refusal: distributed Chroma answers 429 "log needs compaction before
// accepting more writes" when the log of the collection holds too many records not yet compacted, and its log service refuses the write
// before it takes any record. Then the same request goes again, with the attempts and the delays of WriteRetries; any other error of a
// write, also another 429, stands at once. ChromaWriteRetries.None turns it off, and ReadRetries does not change it.
[TestFixture]
public class WriteRetriesTests
{
	const string Local = "http://localhost:8000";
	const string Compaction = """{"error":"ChromaError","message":"log needs compaction before accepting more writes; please backoff exponentially and retry"}""";
	static readonly ChromaWriteRetries NoDelay = new(4, TimeSpan.Zero, TimeSpan.Zero);
	static readonly string[] Writes = ["add", "update", "upsert", "delete", "delete by a filter", "delete with a limit", "delete all"];

	[Test]
	public async Task AWriteRefusedForCompactionGoesAgain([ValueSource(nameof(Writes))] string write)
	{
		var server = new Server(Path(write), fail: 2, (HttpStatusCode.TooManyRequests, Compaction));
		await Run(Client(server, NoDelay), write);
		Assert.That(server.Writes, Is.EqualTo(3));
	}

	// The batches that went are not sent again: only the refused one goes again.
	[Test]
	public async Task OnlyTheRefusedBatchGoesAgain()
	{
		var server = new Server("add", fail: 1, (HttpStatusCode.TooManyRequests, Compaction)) { FailFrom = 2, MaxBatchSize = 1 };
		await Client(server, NoDelay).AddAsync(new ChromaRecords(["a", "b", "c"]) { Embeddings = [new([1f, 0f]), new([0f, 1f]), new([1f, 1f])] });
		Assert.That(server.Batches, Is.EqualTo(new[] { "a", "b", "b", "c" }));
	}

	// The refusal of the last attempt stands, as the server sent it.
	[Test]
	public void AfterTheLastAttemptTheRefusalStands([ValueSource(nameof(Writes))] string write)
	{
		var server = new Server(Path(write), fail: int.MaxValue, (HttpStatusCode.TooManyRequests, Compaction));
		var ex = Assert.ThrowsAsync<ChromaException>(() => Run(Client(server, NoDelay), write));
		Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
		Assert.That(ex.ErrorType, Is.EqualTo("ChromaError"));
		Assert.That(ex.Message, Does.StartWith("log needs compaction before accepting more writes"));
		Assert.That(server.Writes, Is.EqualTo(4));
	}

	// Only that refusal goes again: any other 429, the same message with another status, and an error of the server stand at once.
	[TestCase(429, """{"error":"Backoff","message":"Backoff and retry"}""")]
	[TestCase(429, """{"error":"RateLimitError","message":"rate limit exceeded"}""")]
	[TestCase(429, "Too Many Requests")]
	[TestCase(500, """{"error":"InternalError","message":"log needs compaction before accepting more writes; please backoff exponentially and retry"}""")]
	[TestCase(503, "{}")]
	public void AnyOtherErrorOfAWriteStands(int status, string body)
	{
		var server = new Server("add", fail: int.MaxValue, ((HttpStatusCode)status, body));
		Assert.ThrowsAsync<ChromaException>(() => Run(Client(server, NoDelay), "add"));
		Assert.That(server.Writes, Is.EqualTo(1));
	}

	[Test]
	public void NoneTurnsItOff()
	{
		var server = new Server("add", fail: 1, (HttpStatusCode.TooManyRequests, Compaction));
		Assert.ThrowsAsync<ChromaException>(() => Run(Client(server, ChromaWriteRetries.None), "add"));
		Assert.That(server.Writes, Is.EqualTo(1));
	}

	// The writes have their own setting: ReadRetries.None does not stop a refused write from going again, and WriteRetries.None does not
	// stop a read.
	[Test]
	public async Task TheSettingsOfTheReadsAndOfTheWritesAreApart()
	{
		var refused = new Server("add", fail: 1, (HttpStatusCode.TooManyRequests, Compaction));
		var options = new ChromaConfigurationOptions(Local).WithReadRetries(ChromaReadRetries.None).WithWriteRetries(NoDelay);
		await Run(new ChromaCollectionClient(new ChromaCollection("c") { Id = Guid.NewGuid() }, options, new HttpClient(refused)), "add");
		Assert.That(refused.Writes, Is.EqualTo(2));

		var failing = new Server("get", fail: 1, (HttpStatusCode.InternalServerError, """{"error":"InternalError","message":"Error finding id"}"""));
		options = new ChromaConfigurationOptions(Local).WithReadRetries(new ChromaReadRetries(4, TimeSpan.Zero, TimeSpan.Zero)).WithWriteRetries(ChromaWriteRetries.None);
		await new ChromaCollectionClient(new ChromaCollection("c") { Id = Guid.NewGuid() }, options, new HttpClient(failing)).GetAsync();
		Assert.That(failing.Writes, Is.EqualTo(2), "the get went again");
	}

	// The default goes again too, with its delays: 12 attempts, from 0.5 to 1 second before the second one.
	[Test]
	public async Task TheDefault()
	{
		var server = new Server("add", fail: 1, (HttpStatusCode.TooManyRequests, Compaction));
		var watch = System.Diagnostics.Stopwatch.StartNew();
		await Run(new ChromaCollectionClient(new ChromaCollection("c") { Id = Guid.NewGuid() }, new ChromaConfigurationOptions(Local), new HttpClient(server)), "add");
		Assert.That(watch.Elapsed, Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(450)), "at least half the base, 500 ms");
		Assert.That(server.Writes, Is.EqualTo(2));
		Assert.That(new ChromaConfigurationOptions(Local).WriteRetries, Is.SameAs(ChromaWriteRetries.Default));
		Assert.That((ChromaWriteRetries.Default.MaxAttempts, ChromaWriteRetries.Default.MinDelay, ChromaWriteRetries.Default.BaseDelay, ChromaWriteRetries.Default.MaxDelay),
			Is.EqualTo((12, TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10))));
		Assert.That((ChromaWriteRetries.None.MaxAttempts, ChromaWriteRetries.None.MinDelay, ChromaWriteRetries.None.BaseDelay, ChromaWriteRetries.None.MaxDelay),
			Is.EqualTo((1, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero)));
	}

	// The default waits from 42.5 to 85 seconds in all: 11 delays, from half the bound to the bound, the bounds 1, 2, 4 and 8 seconds and
	// then 10.
	[Test]
	public void TheWaitsOfTheDefault()
	{
		var retries = ChromaWriteRetries.Default;
		var shortest = Enumerable.Range(0, retries.MaxAttempts - 1).Sum(retry => retries.Delay(retry, 0).TotalMilliseconds);
		var longest = Enumerable.Range(0, retries.MaxAttempts - 1).Sum(retry => retries.Delay(retry, 1).TotalMilliseconds);
		Assert.That((shortest, longest), Is.EqualTo((42500d, 85000d)));
	}

	// A Retry-After of the refusal is waited instead of the backoff, whose delay is here 10 minutes: the write goes again after 1 second.
	[Test]
	public async Task ARetryAfterIsWaited()
	{
		var server = new Server("add", fail: 1, (HttpStatusCode.TooManyRequests, Compaction)) { RetryAfter = new(TimeSpan.FromSeconds(1)) };
		var client = Client(server, new ChromaWriteRetries(2, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10)) { Jitter = () => 0 });
		var watch = System.Diagnostics.Stopwatch.StartNew();
		await Run(client, "add");
		Assert.That(watch.Elapsed, Is.InRange(TimeSpan.FromMilliseconds(950), TimeSpan.FromSeconds(30)));
		Assert.That(server.Writes, Is.EqualTo(2));
	}

	// A Retry-After as a date already gone is no wait; it counts against the attempts as the others.
	[Test]
	public void ARetryAfterAsADateCountsAgainstTheAttempts()
	{
		var server = new Server("add", fail: int.MaxValue, (HttpStatusCode.TooManyRequests, Compaction)) { RetryAfter = new(DateTimeOffset.UtcNow.AddMinutes(-1)) };
		var client = Client(server, new ChromaWriteRetries(3, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10)));
		var watch = System.Diagnostics.Stopwatch.StartNew();
		Assert.ThrowsAsync<ChromaException>(() => Run(client, "add"));
		Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(30)));
		Assert.That(server.Writes, Is.EqualTo(3));
	}

	// A Retry-After of another error does not make the write go again.
	[TestCase(429, """{"error":"RateLimitError","message":"rate limit exceeded"}""")]
	[TestCase(503, "{}")]
	public void ARetryAfterOfAnotherError(int status, string body)
	{
		var server = new Server("add", fail: int.MaxValue, ((HttpStatusCode)status, body)) { RetryAfter = new(TimeSpan.Zero) };
		Assert.ThrowsAsync<ChromaException>(() => Run(Client(server, NoDelay), "add"));
		Assert.That(server.Writes, Is.EqualTo(1));
	}

	// The delay of a Retry-After and up to a tenth more, at most 30 seconds, or the largest delay when it is longer.
	[TestCase(2000, 10000, 0.0, 2000)]
	[TestCase(2000, 10000, 1.0, 2200)]
	[TestCase(20000, 10000, 0.5, 21000)]
	[TestCase(60000, 10000, 1.0, 30000)]
	[TestCase(60000, 50000, 1.0, 50000)]
	[TestCase(0, 10000, 1.0, 0)]
	public void TheDelayOfARetryAfter(int retryAfter, int maxDelay, double random, int milliseconds)
		=> Assert.That(new ChromaWriteRetries(5, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(maxDelay)).Delay(0, TimeSpan.FromMilliseconds(retryAfter), random), Is.EqualTo(TimeSpan.FromMilliseconds(milliseconds)));

	// Without a Retry-After, the backoff.
	[Test]
	public void WithoutARetryAfter()
		=> Assert.That(new ChromaWriteRetries(5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10)).Delay(2, null, 1.0), Is.EqualTo(TimeSpan.FromSeconds(4)));

	// Before each new attempt the write waits as a read does: the jitter is 1, so the delays are the base and then twice the base.
	[Test]
	public async Task TheDelays()
	{
		var server = new Server("add", fail: 2, (HttpStatusCode.TooManyRequests, Compaction));
		var client = Client(server, new ChromaWriteRetries(3, TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(5)) { Jitter = () => 1 });
		var watch = System.Diagnostics.Stopwatch.StartNew();
		await Run(client, "add");
		Assert.That(watch.Elapsed, Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(850)), "300 ms and then 600 ms");
		Assert.That(server.Writes, Is.EqualTo(3));
	}

	// The equal jitter: a random time between half the bound and the bound, never under the smallest delay, here 0.8 times the base, 80
	// ms. The bound is the base times 2 to the attempts before, up to the largest delay.
	[TestCase(0, 0.0, 80)]
	[TestCase(0, 1.0, 100)]
	[TestCase(1, 0.0, 100)]
	[TestCase(3, 0.0, 400)]
	[TestCase(3, 1.0, 800)]
	[TestCase(4, 0.5, 750)]
	[TestCase(62, 0.0, 500)]
	[TestCase(1000, 1.0, 1000)]
	public void TheDelay(int retry, double random, int milliseconds)
		=> Assert.That(new ChromaWriteRetries(5, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1)).Delay(retry, random), Is.EqualTo(TimeSpan.FromMilliseconds(milliseconds)));

	[TestCase(0, 0.0, 50)]
	[TestCase(0, 0.5, 75)]
	[TestCase(2, 0.5, 300)]
	public void TheDelayWithASmallerSmallestDelay(int retry, double random, int milliseconds)
		=> Assert.That(new ChromaWriteRetries(5, TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1)).Delay(retry, random), Is.EqualTo(TimeSpan.FromMilliseconds(milliseconds)));

	// Without a smallest delay the first delay is from half the base to the base.
	[TestCase(0, 0.0, 500)]
	[TestCase(0, 1.0, 1000)]
	[TestCase(4, 0.0, 5000)]
	public void TheDelayWithoutASmallestDelay(int retry, double random, int milliseconds)
		=> Assert.That(new ChromaWriteRetries(5, TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10)).Delay(retry, random), Is.EqualTo(TimeSpan.FromMilliseconds(milliseconds)));

	// 0 <= minDelay <= baseDelay <= maxDelay <= Int32.MaxValue milliseconds and at least one attempt, each error named by its parameter.
	[TestCase(1, -1, 0, 0, "minDelay")]
	[TestCase(1, 10, 5, 20, "minDelay")]
	[TestCase(1, 0, -1, 0, "baseDelay")]
	[TestCase(1, 0, 10, 5, "maxDelay")]
	[TestCase(0, 0, 0, 0, "maxAttempts")]
	public void WrongSettings(int attempts, int minDelay, int baseDelay, int maxDelay, string parameter)
		=> Assert.That(() => new ChromaWriteRetries(attempts, TimeSpan.FromMilliseconds(minDelay), TimeSpan.FromMilliseconds(baseDelay), TimeSpan.FromMilliseconds(maxDelay)),
			Throws.InstanceOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo(parameter));

	[Test]
	public void TheLongestDelay()
	{
		var longest = TimeSpan.FromMilliseconds(int.MaxValue);
		Assert.That(new ChromaWriteRetries(2, longest, longest).Delay(0, 1.0), Is.EqualTo(longest));
		Assert.That(() => new ChromaWriteRetries(2, TimeSpan.Zero, longest + TimeSpan.FromMilliseconds(1)), Throws.InstanceOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo("maxDelay"));
		Assert.That(() => new ChromaWriteRetries(2, TimeSpan.FromMilliseconds(-1), TimeSpan.Zero), Throws.InstanceOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo("baseDelay"));
	}

	// WriteRetries is never null, also when set directly.
	[Test]
	public void NoNull()
	{
		Assert.That(() => new ChromaConfigurationOptions(Local) { WriteRetries = null! }, Throws.ArgumentNullException);
		Assert.That(() => new ChromaConfigurationOptions(Local).WithWriteRetries(null!), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("writeRetries"));
	}

	// The setting stays through the other options.
	[Test]
	public void TheSettingStays()
		=> Assert.That(new ChromaConfigurationOptions(Local).WithWriteRetries(ChromaWriteRetries.None).WithTenant("t").WithBatchSplitting(false).WithReadRetries(ChromaReadRetries.None).WriteRetries, Is.SameAs(ChromaWriteRetries.None));

	// And through WithMetadataValues of a client made by name, as a provider of Microsoft.Extensions.VectorData makes it.
	[Test]
	public void TheSettingStaysThroughWithMetadataValues()
	{
		var server = new Server("add", fail: int.MaxValue, (HttpStatusCode.TooManyRequests, Compaction));
		var client = new ChromaClient(new ChromaConfigurationOptions(Local).WithWriteRetries(ChromaWriteRetries.None), new HttpClient(server));
		var collection = client.GetCollectionClient("c").WithMetadataValues(ChromaMetadataValues.Exact);
		Assert.ThrowsAsync<ChromaException>(() => Run(collection, "add"));
		Assert.That(server.Writes, Is.EqualTo(1));
	}

	// A cancellation during the delay stops the write at once: no other attempt goes.
	[Test]
	public void ACancellationBetweenTheAttempts()
	{
		using var cancellation = new CancellationTokenSource();
		var server = new Server("add", fail: 1, (HttpStatusCode.TooManyRequests, Compaction)) { OnFail = () => cancellation.CancelAfter(TimeSpan.FromMilliseconds(200)) };
		var client = Client(server, new ChromaWriteRetries(2, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10)) { Jitter = () => 1 });
		Assert.That(() => client.AddAsync(new ChromaRecords(["a"]) { Embeddings = [new([1f, 0f])] }, cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
		Assert.That(server.Writes, Is.EqualTo(1));
	}

	static string Path(string write) => write switch
	{
		"update" or "upsert" => write,
		"add" => "add",
		_ => "delete",
	};

	static Task Run(ChromaCollectionClient client, string write)
	{
		var records = new ChromaRecords(["a"]) { Embeddings = [new([1f, 0f])] };
		return write switch
		{
			"add" => client.AddAsync(records),
			"update" => client.UpdateAsync(records),
			"upsert" => client.UpsertAsync(records),
			"delete" => client.DeleteAsync(["a"]),
			"delete by a filter" => client.DeleteAsync(new ChromaDelete { WhereDocument = ChromaWhereDocumentOperator.Contains("x") }),
			"delete with a limit" => client.DeleteAsync(new ChromaDelete { WhereDocument = ChromaWhereDocumentOperator.Contains("x"), Limit = 0 }),
			_ => client.DeleteAllAsync(),
		};
	}

	static ChromaCollectionClient Client(HttpMessageHandler handler, ChromaWriteRetries retries)
		=> new(new ChromaCollection("c") { Id = Guid.NewGuid() }, new ChromaConfigurationOptions(Local).WithWriteRetries(retries), new HttpClient(handler));

	// Answers the requests to the path named in failing, from the one numbered FailFrom, with the error, fail times, and then as Chroma
	// 1.5.9 does; a get returns the record "a" until a delete goes.
	sealed class Server(string failing, int fail, (HttpStatusCode Status, string Body) error) : HttpMessageHandler
	{
		public int FailFrom { get; init; } = 1;
		public int MaxBatchSize { get; init; } = 100;
		public Action? OnFail { get; init; }
		public System.Net.Http.Headers.RetryConditionHeaderValue? RetryAfter { get; init; }
		public int Writes { get; private set; }
		public List<string> Batches { get; } = [];
		bool _deleted;
		int _failed;

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var path = request.RequestUri!.AbsolutePath;
			var last = path[(path.LastIndexOf('/') + 1)..];
			if (last == failing)
			{
				Writes++;
				var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
				if (body.Length > 0 && System.Text.Json.JsonDocument.Parse(body).RootElement.TryGetProperty("ids", out var ids) && ids.ValueKind == System.Text.Json.JsonValueKind.Array)
				{
					Batches.AddRange(ids.EnumerateArray().Select(id => id.GetString()!));
				}
				if (Writes >= FailFrom && _failed < fail)
				{
					_failed++;
					OnFail?.Invoke();
					var refusal = new HttpResponseMessage(error.Status) { Content = new StringContent(error.Body) };
					refusal.Headers.RetryAfter = RetryAfter;
					return refusal;
				}
				_deleted |= last == "delete";
			}
			var answer = last switch
			{
				"version" => "\"1.5.9\"",
				"openapi.json" => """{"components":{"schemas":{"DeleteCollectionRecordsPayload":{"properties":{"limit":{}}}}}}""",
				"pre-flight-checks" => $$"""{"max_batch_size":{{MaxBatchSize}},"supports_base64_encoding":false}""",
				"get" => _deleted ? """{"ids":[],"include":[]}""" : """{"ids":["a"],"include":[]}""",
				"delete" => """{"deleted":1}""",
				"c" => """{"id":"11111111-2222-3333-4444-555555555555","name":"c"}""",
				_ => "{}",
			};
			return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer) };
		}
	}
}
