using System.Net;
using System.Text.Json;
using ChromaDB.Client.Models;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

[TestFixture]
public class BatchesAndFiltersTests
{
	static readonly ReadOnlyMemory<float> Embedding = new([1f, 0f]);
	static readonly ChromaConfigurationOptions Options = new("http://localhost:8000");

	static ChromaRecords FiveRecords() => new(["a", "b", "c", "d", "e"])
	{
		Embeddings = Enumerable.Repeat(Embedding, 5).ToList(),
		Metadatas = Enumerable.Range(1, 5).Select(i => new Dictionary<string, object> { ["i"] = i }).ToList(),
		Documents = ["1", "2", "3", "4", "5"],
		Uris = ["u1", null, "u3", null, "u5"],
	};

	// Without the option the records go in one request, as before; pre-flight-checks is asked only for the base64 of the embeddings.
	[Test]
	public async Task OneRequestByDefault()
	{
		var handler = new Handler("""{"max_batch_size":2}""");
		await Client(Options, handler).Add(FiveRecords());
		Assert.That(handler.Paths.Select(Last), Is.EqualTo(new[] { "pre-flight-checks", "add" }));
		Assert.That(handler.Bodies.Single().GetProperty("ids").GetArrayLength(), Is.EqualTo(5));
	}

	[Test]
	public async Task BatchesOfTheMaxBatchSize()
	{
		var handler = new Handler("""{"max_batch_size":2}""");
		await Client(Options.WithBatchSplitting(), handler).Add(FiveRecords());
		Assert.That(handler.Paths.Select(Last), Is.EqualTo(new[] { "pre-flight-checks", "add", "add", "add" }));
		Assert.That(handler.Bodies.Select(b => Strings(b, "ids")), Is.EqualTo(new[] { new[] { "a", "b" }, new[] { "c", "d" }, new[] { "e" } }));
		Assert.That(handler.Bodies.Select(b => Strings(b, "documents")), Is.EqualTo(new[] { new[] { "1", "2" }, new[] { "3", "4" }, new[] { "5" } }));
		Assert.That(handler.Bodies.Select(b => Strings(b, "uris")), Is.EqualTo(new[] { new[] { "u1", null }, new[] { "u3", null }, new[] { "u5" } }));
		Assert.That(handler.Bodies.Select(b => b.GetProperty("metadatas").EnumerateArray().Select(m => m.GetProperty("i").GetInt32())), Is.EqualTo(new[] { new[] { 1, 2 }, new[] { 3, 4 }, new[] { 5 } }));
		Assert.That(handler.Bodies.Select(b => b.GetProperty("embeddings").GetArrayLength()), Is.EqualTo(new[] { 2, 2, 1 }));
	}

	// The limit is asked once for all the calls of a client.
	[Test]
	public async Task UpdateUpsertAndDeleteToo()
	{
		var handler = new Handler("""{"max_batch_size":2}""");
		var client = Client(Options.WithBatchSplitting(), handler);
		await client.Update(FiveRecords());
		await client.Upsert(FiveRecords());
		await client.Delete(["a", "b", "c"], where: ChromaWhereOperator.Equal("k", 1));
		Assert.That(handler.Paths.Select(Last), Is.EqualTo(new[] { "pre-flight-checks", "update", "update", "update", "upsert", "upsert", "upsert", "delete", "delete" }));
		Assert.That(handler.Bodies.Skip(6).Select(b => Strings(b, "ids")), Is.EqualTo(new[] { new[] { "a", "b" }, new[] { "c" } }));
		Assert.That(handler.Bodies.Skip(6).Select(b => b.GetProperty("where").GetRawText()), Is.All.EqualTo("""{"k":{"$eq":1}}"""));
	}

	[Test]
	public async Task OneRequestWithinTheLimit()
	{
		var handler = new Handler("""{"max_batch_size":5}""");
		await Client(Options.WithBatchSplitting(), handler).Add(FiveRecords());
		Assert.That(handler.Paths.Select(Last), Is.EqualTo(new[] { "pre-flight-checks", "add" }));
	}

	// Chroma 0.4.10 has no pre-flight-checks: the records go in one request.
	[Test]
	public async Task OneRequestWithoutPreFlightChecks()
	{
		var handler = new Handler(null);
		await Client(Options.WithBatchSplitting(), handler).Add(FiveRecords());
		Assert.That(handler.Paths.Select(Last), Is.EqualTo(new[] { "pre-flight-checks", "add" }));
		Assert.That(handler.Bodies.Single().GetProperty("ids").GetArrayLength(), Is.EqualTo(5));
	}

	// A failed batch stops the others; the earlier batches stay written.
	[Test]
	public async Task FailedBatchStops()
	{
		var handler = new Handler("""{"max_batch_size":2}""") { FailAt = 2 };
		Assert.ThrowsAsync<ChromaException>(() => Client(Options.WithBatchSplitting(), handler).Add(FiveRecords()));
		Assert.That(handler.Paths.Select(Last), Is.EqualTo(new[] { "pre-flight-checks", "add", "add" }));
	}

	[Test]
	public void OtherOptionsKeepBatchSplitting()
		=> Assert.That(Options.WithBatchSplitting().WithTenant("t").WithMetadataValues(ChromaMetadataValues.Exact).BatchSplitting, Is.True);

	[Test]
	public async Task FiltersAsTheJsonTheClientSends()
	{
		var where = ChromaWhereOperator.Equal("k", 1) & (ChromaWhereOperator.In("t", "a", "b") | ChromaWhereOperator.Contains("tags", "x"));
		var whereDocument = ChromaWhereDocumentOperator.Contains("foo") | ChromaWhereDocumentOperator.NotContains("bar");
		Assert.That(where.ToString(), Is.EqualTo("""{"$and":[{"k":{"$eq":1}},{"$or":[{"t":{"$in":["a","b"]}},{"tags":{"$contains":"x"}}]}]}"""));
		Assert.That(whereDocument.ToString(), Is.EqualTo("""{"$or":[{"$contains":"foo"},{"$not_contains":"bar"}]}"""));

		var handler = new Handler("""{"ids":[]}""");
		await Client(Options, handler).Get(where: where, whereDocument: whereDocument);
		Assert.That(handler.Bodies.Single().GetProperty("where").GetRawText(), Is.EqualTo(where.ToString()));
		Assert.That(handler.Bodies.Single().GetProperty("where_document").GetRawText(), Is.EqualTo(whereDocument.ToString()));
	}

	// Every tested Chroma rejects $in and $nin without values.
	[Test]
	public void InAndNotInNeedValues()
	{
		Assert.Throws<ArgumentException>(() => ChromaWhereOperator.In("k"));
		Assert.Throws<ArgumentException>(() => ChromaWhereOperator.NotIn("k", []));
		Assert.That(ChromaWhereOperator.In("k", 1).ToString(), Is.EqualTo("""{"k":{"$in":[1]}}"""));
		Assert.That(ChromaWhereOperator.NotIn("k", "a").ToString(), Is.EqualTo("""{"k":{"$nin":["a"]}}"""));
	}

	static string Last(string path) => path.Substring(path.LastIndexOf('/') + 1);

	static string?[] Strings(JsonElement body, string property)
		=> body.GetProperty(property).EnumerateArray().Select(x => x.GetString()).ToArray();

	static ChromaCollectionClient Client(ChromaConfigurationOptions options, HttpMessageHandler handler)
		=> new(new ChromaCollection("collection") { Id = Guid.NewGuid() }, options, new HttpClient(handler));

	// Answers pre-flight-checks with the given body, or 404 when it is null, and the other requests with true.
	sealed class Handler(string? preFlightChecks) : HttpMessageHandler
	{
		public List<string> Paths { get; } = [];
		public List<JsonElement> Bodies { get; } = [];
		public int FailAt { get; init; } = -1;
		int _records;

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
			if (++_records == FailAt)
			{
				return new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("""{"error":"InternalError","message":"failed"}""") };
			}
			return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(request.RequestUri.AbsolutePath.EndsWith("/get") ? """{"ids":[]}""" : "true") };
		}
	}
}
