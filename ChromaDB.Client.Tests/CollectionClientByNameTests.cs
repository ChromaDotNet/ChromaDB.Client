using System.Net;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

// A collection client made by name reads the collection before its first request, and again when a request fails on the id it read
// before: when the name has another id now, the operation runs again, once, on that collection.
[TestFixture]
public class CollectionClientByNameTests
{
	const string First = "11111111-1111-1111-1111-111111111111";
	const string Second = "22222222-2222-2222-2222-222222222222";
	const string Missing = """{"error":"NotFoundError","message":"Collection [11111111-1111-1111-1111-111111111111] does not exist."}""";

	[Test]
	public async Task ReadsTheCollectionOnce()
	{
		var server = new FakeServer(First);
		var collection = Client(server).GetCollectionClient("c");
		Assert.That((collection.Collection.Name, collection.Collection.Id), Is.EqualTo(("c", Guid.Empty)));
		Assert.That(server.Requests, Is.Empty);
		Assert.That(await collection.CountAsync(), Is.EqualTo(3));
		Assert.That(await collection.CountAsync(), Is.EqualTo(3));
		Assert.That(server.Requests, Is.EqualTo(new[] { "GET collections/c", $"GET collections/{First}/count", $"GET collections/{First}/count" }));
		Assert.That(collection.Collection.Id, Is.EqualTo(Guid.Parse(First)));
	}

	// The collection was deleted and created again elsewhere: the client reads it again and counts on the new one.
	[Test]
	public async Task ReadsTheCollectionAgainWhenItsIdIsGone()
	{
		var server = new FakeServer(First);
		var collection = Client(server).GetCollectionClient("c");
		await collection.CountAsync();
		server.Id = Second;
		server.MissingId = First;
		Assert.That(await collection.CountAsync(), Is.EqualTo(3));
		Assert.That(server.Requests.Skip(2), Is.EqualTo(new[] { $"GET collections/{First}/count", "GET collections/c", $"GET collections/{Second}/count" }));
		Assert.That(collection.Collection.Id, Is.EqualTo(Guid.Parse(Second)));
	}

	// An id just read is not read again, and an id that stays the same keeps the failure.
	[Test]
	public async Task TheSameIdKeepsTheFailure()
	{
		var server = new FakeServer(First) { MissingId = First };
		var collection = Client(server).GetCollectionClient("c");
		await Assert.ThatAsync(() => collection.CountAsync(), Throws.InstanceOf<ChromaException>().With.Message.Contains("does not exist"));
		Assert.That(server.Requests, Is.EqualTo(new[] { "GET collections/c", $"GET collections/{First}/count" }));
		await Assert.ThatAsync(() => collection.CountAsync(), Throws.InstanceOf<ChromaException>().With.Message.Contains("does not exist"));
		Assert.That(server.Requests.Skip(2), Is.EqualTo(new[] { $"GET collections/{First}/count", "GET collections/c" }));
	}

	// Any failure counts, as the servers tell a collection gone in their own ways: Chroma 0.4 answers 500 with "coroutine raised StopIteration".
	[Test]
	public async Task AnyFailureReadsTheCollectionAgain()
	{
		var server = new FakeServer(First);
		var collection = Client(server).GetCollectionClient("c");
		await collection.CountAsync();
		server.MissingId = First;
		server.MissingAnswer = (HttpStatusCode.InternalServerError, """{"error":"RuntimeError('coroutine raised StopIteration')"}""");
		await Assert.ThatAsync(() => collection.CountAsync(), Throws.InstanceOf<ChromaException>().With.Message.EqualTo("coroutine raised StopIteration"));
		server.Id = Second;
		Assert.That(await collection.CountAsync(), Is.EqualTo(3));
		Assert.That(server.Requests.Skip(2), Is.EqualTo(new[] { $"GET collections/{First}/count", "GET collections/c", $"GET collections/{First}/count", "GET collections/c", $"GET collections/{Second}/count" }));
	}

	// When the name has no collection either, the failure of the operation stands.
	[Test]
	public async Task NoCollectionOfTheNameKeepsTheFailure()
	{
		var server = new FakeServer(First);
		var collection = Client(server).GetCollectionClient("c");
		await collection.CountAsync();
		server.MissingId = First;
		server.NameMissing = true;
		await Assert.ThatAsync(() => collection.CountAsync(), Throws.InstanceOf<ChromaException>().With.Message.Contains(First));
		Assert.That(server.Requests.Skip(2), Is.EqualTo(new[] { $"GET collections/{First}/count", "GET collections/c" }));
	}

	// Also an operation without a result, like an upsert, runs again on the collection of the name.
	[Test]
	public async Task OperationsWithoutAResult()
	{
		var server = new FakeServer(First);
		var collection = Client(server).GetCollectionClient("c");
		await collection.DeleteAsync(["a"]);
		server.Id = Second;
		server.MissingId = First;
		await collection.DeleteAsync(["a"]);
		Assert.That(server.Requests.Where(x => x.EndsWith("/delete")), Is.EqualTo(new[] { $"POST collections/{First}/delete", $"POST collections/{First}/delete", $"POST collections/{Second}/delete" }));
	}

	[Test]
	public async Task GetCollectionAsync()
	{
		var server = new FakeServer(First);
		var client = Client(server);
		var byName = client.GetCollectionClient("c");
		Assert.That((await byName.GetCollectionAsync()).Id, Is.EqualTo(Guid.Parse(First)));
		Assert.That((await byName.GetCollectionAsync()).Id, Is.EqualTo(Guid.Parse(First)));
		var byId = client.GetCollectionClient(Guid.Parse(Second), "c");
		Assert.That((await byId.GetCollectionAsync()).Id, Is.EqualTo(Guid.Parse(Second)));
		Assert.That(server.Requests, Is.EqualTo(new[] { "GET collections/c" }));
	}

	static ChromaClient Client(HttpMessageHandler handler)
		=> new(new ChromaConfigurationOptions("http://localhost:8000").WithBatchSplitting(false), new HttpClient(handler));

	// Answers the collection c with Id, or as missing with NameMissing, MissingAnswer to the requests on MissingId, and 3 for a count;
	// records the requests, without the prefix of the database.
	sealed class FakeServer(string id) : HttpMessageHandler
	{
		public string Id { get; set; } = id;
		public string? MissingId { get; set; }
		public (HttpStatusCode Status, string Body) MissingAnswer { get; set; } = (HttpStatusCode.NotFound, Missing);
		public bool NameMissing { get; set; }
		public List<string> Requests { get; } = [];

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var path = request.RequestUri!.AbsolutePath.Replace("/api/v2/tenants/default_tenant/databases/default_database/", "");
			Requests.Add($"{request.Method} {path}");
			var (status, body) = path == "collections/c" && NameMissing ? (HttpStatusCode.NotFound, """{"error":"NotFoundError","message":"Collection [c] does not exist"}""")
				: path == "collections/c" ? (HttpStatusCode.OK, $$"""{"id":"{{Id}}","name":"c"}""")
				: MissingId is { } missing && path.Contains(missing) ? MissingAnswer
				: path.EndsWith("/count") ? (HttpStatusCode.OK, "3")
				: (HttpStatusCode.OK, "{}");
			return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
		}
	}
}
