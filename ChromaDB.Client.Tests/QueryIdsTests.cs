using System.Net;
using System.Text.Json;
using ChromaDB.Client.Models;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

[TestFixture]
public class QueryIdsTests
{
	static readonly ReadOnlyMemory<float> Embedding = new([1f, 0f]);

	[Test]
	public async Task QuerySendsIds()
	{
		var handler = new RecordingHandler("""{"ids":[["a"]]}""");
		await Client(handler).QueryAsync(new ChromaQuery([Embedding]) { Ids = ["a", "c"] });
		Assert.That(handler.Body.GetProperty("ids").EnumerateArray().Select(x => x.GetString()), Is.EqualTo(new[] { "a", "c" }));
	}

	[Test]
	public async Task QueryWithoutIdsDoesNotSendThem()
	{
		var handler = new RecordingHandler("""{"ids":[["a"]]}""");
		await Client(handler).QueryAsync(Embedding);
		Assert.That(handler.Body.TryGetProperty("ids", out _), Is.False);
	}

	[Test]
	public async Task QuerySendsTheOtherFields()
	{
		var handler = new RecordingHandler("""{"ids":[["a"]]}""");
		await Client(handler).QueryAsync(new ChromaQuery([Embedding]) { NResults = 3, Where = ChromaWhereOperator.Equal("k", 1), WhereDocument = ChromaWhereDocumentOperator.Contains("x"), Include = ChromaQueryInclude.Documents });
		Assert.That(handler.Body.GetProperty("n_results").GetInt32(), Is.EqualTo(3));
		Assert.That(handler.Body.GetProperty("where").GetProperty("k").GetProperty("$eq").GetInt32(), Is.EqualTo(1));
		Assert.That(handler.Body.GetProperty("where_document").GetProperty("$contains").GetString(), Is.EqualTo("x"));
		Assert.That(handler.Body.GetProperty("include").EnumerateArray().Select(x => x.GetString()), Is.EqualTo(new[] { "documents" }));
	}

	// Chroma 0.x ignores the ids: a result outside them must not reach the caller.
	[Test]
	public async Task ResultOutsideTheIdsThrows()
	{
		var handler = new RecordingHandler("""{"ids":[["a"],["a","b"]]}""");
		await Assert.ThatAsync(() => Client(handler).QueryAsync(new ChromaQuery([Embedding, Embedding]) { Ids = ["a", "c"] }), Throws.InstanceOf<ChromaException>().With.Message.Contains("outside the ids"));
	}

	[Test]
	public async Task ResultsAmongTheIdsAreReturned()
	{
		var handler = new RecordingHandler("""{"ids":[["c","a"]]}""");
		var result = await Client(handler).QueryAsync(new ChromaQuery([Embedding]) { Ids = ["a", "c"] });
		Assert.That(result.Single().Select(x => x.Id), Is.EqualTo(new[] { "c", "a" }));
	}

	// An empty list keeps no record: any result is outside it.
	[Test]
	public async Task EmptyIdsWithResultsThrows()
	{
		var handler = new RecordingHandler("""{"ids":[["a"]]}""");
		await Assert.ThatAsync(() => Client(handler).QueryAsync(new ChromaQuery([Embedding]) { Ids = [] }), Throws.InstanceOf<ChromaException>());
	}

	static ChromaCollectionClient Client(HttpMessageHandler handler)
	{
		var collection = new ChromaCollection("collection") { Id = Guid.NewGuid() };
		return new ChromaCollectionClient(collection, new ChromaConfigurationOptions("http://localhost:8000"), new HttpClient(handler));
	}

	sealed class RecordingHandler(string response) : HttpMessageHandler
	{
		public JsonElement Body { get; private set; }

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement.Clone();
			return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response) };
		}
	}
}
