using System.Net;
using System.Text.Json;
using ChromaDB.Client.Models;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

// Get, query and delete take neither ids nor documents in their where clause: the client sends the conditions on #id, with Equal and
// In, and the ones of Document as the ids and where_document of the request, when they are joined to the others with &. The Search
// API takes them in its where clause as they are.
[TestFixture]
public class WhereSplitTests
{
	static readonly ReadOnlyMemory<float> Embedding = new([1f, 0f]);
	static readonly ChromaWhereOperator Pool = ChromaWhereOperator.Document(ChromaWhereDocumentOperator.Contains("pool"));

	[Test]
	public async Task Get()
	{
		var server = new FakeServer("""{"ids":[]}""");
		await Client(server).GetAsync(ids: ["a", "b", "c"], where: ChromaWhereOperator.In(ChromaSearchKeys.Id, "b", "c", "d") & Pool & ChromaWhereOperator.Equal("k", 1) & ChromaWhereOperator.Equal(ChromaSearchKeys.Id, "c"));
		var body = server.Body("get");
		Assert.That(body.GetProperty("ids").EnumerateArray().Select(x => x.GetString()), Is.EqualTo(new[] { "c" }));
		Assert.That(body.GetProperty("where").GetRawText(), Is.EqualTo("""{"k":{"$eq":1}}"""));
		Assert.That(body.GetProperty("where_document").GetRawText(), Is.EqualTo("""{"$contains":"pool"}"""));
	}

	// Filters on the documents combined with each other, and negated, go as one where_document.
	[Test]
	public async Task DocumentsCombined()
	{
		var server = new FakeServer("""{"ids":[]}""");
		var spa = ChromaWhereOperator.Document(ChromaWhereDocumentOperator.Contains("spa"));
		await Client(server).GetAsync(where: ChromaWhereOperator.Not(Pool | spa));
		var body = server.Body("get");
		Assert.That(body.TryGetProperty("where", out var where) && where.ValueKind != JsonValueKind.Null, Is.False);
		Assert.That(body.GetProperty("where_document").GetRawText(), Is.EqualTo("""{"$and":[{"$not_contains":"pool"},{"$not_contains":"spa"}]}"""));
	}

	// Ids that no record can have: no request.
	[Test]
	public async Task NoIdsLeft()
	{
		var server = new FakeServer("""{"ids":[]}""");
		var client = Client(server);
		Assert.That(await client.GetAsync(where: ChromaWhereOperator.Equal(ChromaSearchKeys.Id, "a") & ChromaWhereOperator.Equal(ChromaSearchKeys.Id, "b")), Is.Empty);
		Assert.That(await client.GetAsync(ids: ["a"], where: ChromaWhereOperator.In(ChromaSearchKeys.Id, "b")), Is.Empty);
		Assert.That(server.Paths, Is.Empty);
	}

	// What the where clause of get, query and delete cannot take.
	[Test]
	public void NotSupported()
	{
		var client = Client(new FakeServer("""{"ids":[]}"""));
		var key = ChromaWhereOperator.Equal("k", 1);
		Assert.That(() => client.GetAsync(where: ChromaWhereOperator.In(ChromaSearchKeys.Id, "a") | key), Throws.InstanceOf<NotSupportedException>());
		Assert.That(() => client.GetAsync(where: Pool | key), Throws.InstanceOf<NotSupportedException>());
		Assert.That(() => client.GetAsync(where: ChromaWhereOperator.NotIn(ChromaSearchKeys.Id, "a")), Throws.InstanceOf<NotSupportedException>());
		Assert.That(() => client.GetAsync(where: ChromaWhereOperator.Equal(ChromaSearchKeys.Id, 1)), Throws.ArgumentException);
		Assert.That(() => ChromaWhereOperator.Document(null!), Throws.ArgumentNullException);
	}

	[Test]
	public async Task Query()
	{
		var server = new FakeServer("""{"ids":[["b"]],"distances":[[0.1]]}""");
		await Client(server).QueryAsync(new ChromaQuery([Embedding]) { Ids = ["a", "b"], Where = ChromaWhereOperator.In(ChromaSearchKeys.Id, "b", "c") & Pool, Include = ChromaQueryInclude.Distances });
		var body = server.Body("query");
		Assert.That(body.GetProperty("ids").EnumerateArray().Select(x => x.GetString()), Is.EqualTo(new[] { "b" }));
		Assert.That(body.GetProperty("where_document").GetRawText(), Is.EqualTo("""{"$contains":"pool"}"""));
	}

	[Test]
	public async Task Delete()
	{
		var server = new FakeServer("""{"ids":[]}""");
		var client = Client(server);
		await client.DeleteAsync(["a", "b"], ChromaWhereOperator.In(ChromaSearchKeys.Id, "b") & Pool);
		var body = server.Body("delete");
		Assert.That(body.GetProperty("ids").EnumerateArray().Select(x => x.GetString()), Is.EqualTo(new[] { "b" }));
		Assert.That(body.GetProperty("where_document").GetRawText(), Is.EqualTo("""{"$contains":"pool"}"""));

		await client.DeleteAsync(new ChromaDelete { Where = ChromaWhereOperator.Equal(ChromaSearchKeys.Id, "c") });
		Assert.That(server.Body("delete").GetProperty("ids").EnumerateArray().Select(x => x.GetString()), Is.EqualTo(new[] { "c" }));
		await client.DeleteAsync(["a"], ChromaWhereOperator.In(ChromaSearchKeys.Id, "b"));
		Assert.That(await client.DeleteAsync(new ChromaDelete { Ids = ["a"], Where = ChromaWhereOperator.In(ChromaSearchKeys.Id, "b") }), Is.EqualTo(0));
		Assert.That(server.Paths.Count(x => x == "delete"), Is.EqualTo(2));
	}

	// The Search API takes them in its where clause.
	[Test]
	public async Task Search()
	{
		var server = new FakeServer("""{"ids":[[]],"documents":[null],"embeddings":[null],"metadatas":[null],"scores":[null],"select":[[]]}""");
		await Client(server).SearchAsync(new ChromaSearch { Where = ChromaWhereOperator.In(ChromaSearchKeys.Id, "a") | Pool });
		Assert.That(server.Body("search").GetProperty("searches")[0].GetProperty("filter").GetRawText(),
			Is.EqualTo("""{"$or":[{"#id":{"$in":["a"]}},{"#document":{"$contains":"pool"}}]}"""));
	}

	// A query that expects another space than the one of the collection stops before the request; a collection whose space the
	// server does not report is taken.
	[TestCase("l2", true)]
	[TestCase("cosine", false)]
	[TestCase(null, false)]
	public async Task ExpectedSpace(string? space, bool throws)
	{
		var server = new FakeServer("""{"ids":[[]],"distances":[[]]}""");
		var collection = new ChromaCollection("c") { Id = Guid.NewGuid(), Metadata = space is null ? null : new Dictionary<string, object> { ["hnsw:space"] = space } };
		var client = new ChromaCollectionClient(collection, new ChromaConfigurationOptions("http://localhost:8000"), new HttpClient(server));
		var query = new ChromaQuery([Embedding]) { ExpectedSpace = ChromaSpace.Cosine };
		if (throws)
		{
			await Assert.ThatAsync(() => client.QueryAsync(query), Throws.InvalidOperationException.With.Message.Contains("space l2, not cosine"));
			Assert.That(server.Paths, Does.Not.Contain("query"));
			return;
		}
		await client.QueryAsync(query);
		Assert.That(server.Paths, Does.Contain("query"));
	}

	static ChromaCollectionClient Client(HttpMessageHandler handler)
		=> new(new ChromaCollection("c") { Id = Guid.NewGuid() }, new ChromaConfigurationOptions("http://localhost:8000"), new HttpClient(handler));

	// Answers the version, pre-flight-checks with a batch size, the reads with the records, and the writes with nothing; records the
	// requests.
	sealed class FakeServer(string records) : HttpMessageHandler
	{
		public List<string> Paths { get; } = [];
		public List<JsonElement> Bodies { get; } = [];

		// The body of the last request to the path.
		public JsonElement Body(string path) => Bodies[Paths.LastIndexOf(path)];

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var path = request.RequestUri!.AbsolutePath;
			var answer = path.EndsWith("/version") ? "\"1.5.9\""
				: path.EndsWith("/pre-flight-checks") ? """{"max_batch_size":100,"supports_base64_encoding":false}"""
				: path.EndsWith("/get") || path.EndsWith("/query") || path.EndsWith("/search") ? records
				: "{}";
			if (!path.EndsWith("/version") && !path.EndsWith("/pre-flight-checks"))
			{
				Paths.Add(path.Substring(path.LastIndexOf('/') + 1));
				Bodies.Add(request.Content is null ? default : JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken)).RootElement.Clone());
			}
			return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer) };
		}
	}
}
