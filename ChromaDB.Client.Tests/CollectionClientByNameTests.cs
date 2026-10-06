using System.Net;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

// A collection client made by name reads the collection before its first request, and again, once, when the server no longer finds
// the id it has: the operation then runs again on the collection of that name.
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

	// An id just read is not read again, and the operation runs again only once.
	[Test]
	public async Task OnlyOnce()
	{
		var server = new FakeServer(First) { MissingId = First };
		var collection = Client(server).GetCollectionClient("c");
		await Assert.ThatAsync(() => collection.CountAsync(), Throws.InstanceOf<ChromaException>().With.Message.Contains("does not exist"));
		Assert.That(server.Requests, Is.EqualTo(new[] { "GET collections/c", $"GET collections/{First}/count" }));
		await Assert.ThatAsync(() => collection.CountAsync(), Throws.InstanceOf<ChromaException>());
		Assert.That(server.Requests.Skip(2), Is.EqualTo(new[] { $"GET collections/{First}/count", "GET collections/c", $"GET collections/{First}/count" }));
	}

	[Test]
	public async Task OtherErrorsAreThrown()
	{
		var server = new FakeServer(First);
		var collection = Client(server).GetCollectionClient("c");
		await collection.CountAsync();
		server.Error = (HttpStatusCode.InternalServerError, """{"error":"InternalError","message":"boom"}""");
		await Assert.ThatAsync(() => collection.CountAsync(), Throws.InstanceOf<ChromaException>().With.Message.EqualTo("boom"));
		Assert.That(server.Requests.Count(x => x == "GET collections/c"), Is.EqualTo(1));
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

	// Answers the collection c with Id, a missing collection for the requests on MissingId, Error when it is set, and 3 for a count;
	// records the requests, without the prefix of the database.
	sealed class FakeServer(string id) : HttpMessageHandler
	{
		public string Id { get; set; } = id;
		public string? MissingId { get; set; }
		public (HttpStatusCode Status, string Body)? Error { get; set; }
		public List<string> Requests { get; } = [];

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var path = request.RequestUri!.AbsolutePath.Replace("/api/v2/tenants/default_tenant/databases/default_database/", "");
			Requests.Add($"{request.Method} {path}");
			var (status, body) = path == "collections/c" ? (HttpStatusCode.OK, $$"""{"id":"{{Id}}","name":"c"}""")
				: MissingId is { } missing && path.Contains(missing) ? (HttpStatusCode.NotFound, Missing)
				: Error is { } error ? error
				: path.EndsWith("/count") ? (HttpStatusCode.OK, "3")
				: (HttpStatusCode.OK, "{}");
			return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
		}
	}
}
