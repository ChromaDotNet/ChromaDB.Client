using System.Net;
using System.Text.Json;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

// Chroma 1.5 keeps the lists in the metadata of the records of a deleted collection or database, and gives them to the next records
// it stores: with deleteRecordsFirst, on Chroma 1.x the client deletes the records first, a page of the batch size at a time.
[TestFixture]
public class DeleteRecordsFirstTests
{
	const string Id = "11111111-2222-3333-4444-555555555555";
	const string OtherId = "66666666-7777-8888-9999-000000000000";

	// By default each deletion is one request, as in Chroma.
	[Test]
	public async Task OneRequestByDefault()
	{
		var server = new FakeServer("1.0.0", "{}");
		var client = Client(server);
		await client.DeleteCollectionAsync("c");
		await client.DeleteCollectionIfExistsAsync("c");
		await client.DeleteDatabaseAsync("d");
		Assert.That(server.Requests.Select(x => x.Request), Is.EqualTo(new[]
		{
			"DELETE tenants/default_tenant/databases/default_database/collections/c",
			"DELETE tenants/default_tenant/databases/default_database/collections/c",
			"DELETE tenants/default_tenant/databases/d",
		}));
	}

	[Test]
	public async Task DeleteCollectionDeletesTheRecordsFirst()
	{
		var server = new FakeServer("1.0.0", $$"""{"id":"{{Id}}","name":"c","tenant":"t2","database":"d2"}""", """["a","b"]""", """["c"]""", "[]");
		await Client(server).DeleteCollectionAsync("c", tenant: "t2", database: "d2", deleteRecordsFirst: true);
		var records = $"tenants/t2/databases/d2/collections/{Id}";
		Assert.That(server.Requests.Select(x => x.Request), Is.EqualTo(new[]
		{
			"GET version",
			"GET tenants/t2/databases/d2/collections/c",
			"GET pre-flight-checks",
			$"POST {records}/get",
			$"POST {records}/delete",
			$"POST {records}/get",
			$"POST {records}/delete",
			$"POST {records}/get",
			"DELETE tenants/t2/databases/d2/collections/c",
		}));
		var get = server.Requests[3].Body;
		Assert.That(get.GetProperty("limit").GetInt32(), Is.EqualTo(2));
		Assert.That(get.GetProperty("include").EnumerateArray(), Is.Empty);
		Assert.That(Ids(server.Requests[4].Body), Is.EqualTo(new[] { "a", "b" }));
		Assert.That(Ids(server.Requests[6].Body), Is.EqualTo(new[] { "c" }));
	}

	[Test]
	public async Task DeleteCollectionOnChroma0()
	{
		var server = new FakeServer("0.6.3", "{}");
		await Client(server).DeleteCollectionAsync("c", deleteRecordsFirst: true);
		Assert.That(server.Requests.Select(x => x.Request), Is.EqualTo(new[] { "GET version", "DELETE tenants/default_tenant/databases/default_database/collections/c" }));
	}

	[Test]
	public async Task DeleteCollectionOnChromaCloud()
	{
		var server = new FakeServer("1.0.0", "{}");
		await Client(server, "https://api.trychroma.com").DeleteCollectionAsync("c", deleteRecordsFirst: true);
		Assert.That(server.Requests.Select(x => x.Request), Is.EqualTo(new[] { "DELETE tenants/default_tenant/databases/default_database/collections/c" }));
	}

	// A page that comes back after its delete would come back forever: the collection goes with it.
	[Test]
	public async Task RecordsThatStayStopTheDeletes()
	{
		var server = new FakeServer("1.0.0", $$"""{"id":"{{Id}}","name":"c"}""", """["a"]""", """["a"]""");
		await Client(server).DeleteCollectionAsync("c", deleteRecordsFirst: true);
		var records = $"tenants/default_tenant/databases/default_database/collections/{Id}";
		Assert.That(server.Requests.Select(x => x.Request), Is.EqualTo(new[]
		{
			"GET version",
			"GET tenants/default_tenant/databases/default_database/collections/c",
			"GET pre-flight-checks",
			$"POST {records}/get",
			$"POST {records}/delete",
			$"POST {records}/get",
			"DELETE tenants/default_tenant/databases/default_database/collections/c",
		}));
	}

	[Test]
	public async Task DeleteDatabaseDeletesTheRecordsOfEachCollection()
	{
		var server = new FakeServer("1.0.0", $$"""[{"id":"{{Id}}","name":"x","tenant":"t","database":"d"},{"id":"{{OtherId}}","name":"y","tenant":"t","database":"d"}]""",
			"""["a"]""", "[]", """["b"]""", "[]");
		await Client(server).DeleteDatabaseAsync("d", tenant: "t", deleteRecordsFirst: true);
		Assert.That(server.Requests.Select(x => x.Request), Is.EqualTo(new[]
		{
			"GET version",
			"GET tenants/t/databases/d/collections",
			"GET pre-flight-checks",
			$"POST tenants/t/databases/d/collections/{Id}/get",
			$"POST tenants/t/databases/d/collections/{Id}/delete",
			$"POST tenants/t/databases/d/collections/{Id}/get",
			$"POST tenants/t/databases/d/collections/{OtherId}/get",
			$"POST tenants/t/databases/d/collections/{OtherId}/delete",
			$"POST tenants/t/databases/d/collections/{OtherId}/get",
			"DELETE tenants/t/databases/d",
		}));
	}

	[Test]
	public async Task DeleteDatabaseOnChroma0()
	{
		var server = new FakeServer("0.6.3", "{}");
		await Client(server).DeleteDatabaseAsync("d", deleteRecordsFirst: true);
		Assert.That(server.Requests.Select(x => x.Request), Is.EqualTo(new[] { "GET version", "DELETE tenants/default_tenant/databases/d" }));
	}

	static ChromaClient Client(HttpMessageHandler handler, string uri = "http://localhost:8000")
		=> new(new ChromaConfigurationOptions(uri), new HttpClient(handler));

	static IEnumerable<string?> Ids(JsonElement body) => body.GetProperty("ids").EnumerateArray().Select(x => x.GetString());

	// Answers the version, the collection or the collections with found, pre-flight-checks with a batch size of 2, and each get with
	// the next page of ids; records the requests, without the prefix of the API.
	sealed class FakeServer(string version, string found, params string[] pages) : HttpMessageHandler
	{
		int _page;

		public List<(string Request, JsonElement Body)> Requests { get; } = [];

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var path = request.RequestUri!.AbsolutePath.Substring("/api/v2/".Length);
			var body = request.Content is null ? default : JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken)).RootElement.Clone();
			Requests.Add(($"{request.Method} {path}", body));
			var answer = path switch
			{
				"version" => $"\"{version}\"",
				"pre-flight-checks" => """{"max_batch_size":2}""",
				_ when path.EndsWith("/get") => $$"""{"ids":{{pages[_page++]}}}""",
				_ when path.EndsWith("/delete") => "{}",
				_ => found,
			};
			return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer) };
		}
	}
}
