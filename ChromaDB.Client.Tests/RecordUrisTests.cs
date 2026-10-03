using System.Net;
using System.Text.Json;
using ChromaDB.Client.Models;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

[TestFixture]
public class RecordUrisTests
{
	static readonly ReadOnlyMemory<float> Embedding = new([1f, 0f]);

	[Test]
	public async Task AddSendsUris()
	{
		var handler = new RecordingHandler("true");
		await Client(handler).Add(new ChromaRecords(["a", "b"]) { Embeddings = [Embedding, Embedding], Uris = ["file://a", null] });
		Assert.That(handler.Body.GetProperty("uris").EnumerateArray().Select(x => x.GetString()), Is.EqualTo(new[] { "file://a", null }));
	}

	[Test]
	public async Task AddWithoutUrisDoesNotSendThem()
	{
		var handler = new RecordingHandler("true");
		await Client(handler).Add(["a"], embeddings: [Embedding]);
		Assert.That(handler.Body.TryGetProperty("uris", out _), Is.False);
	}

	[Test]
	public async Task UpdateAndUpsertSendUris()
	{
		var handler = new RecordingHandler("null");
		var client = Client(handler);
		await client.Update(new ChromaRecords(["a"]) { Uris = ["file://b"] });
		Assert.That(handler.Body.GetProperty("uris")[0].GetString(), Is.EqualTo("file://b"));
		await client.Upsert(new ChromaRecords(["a"]) { Embeddings = [Embedding], Uris = ["file://c"] });
		Assert.That(handler.Body.GetProperty("uris")[0].GetString(), Is.EqualTo("file://c"));
	}

	[Test]
	public async Task GetIncludesAndReadsUris()
	{
		var handler = new RecordingHandler("""{"ids":["a","b"],"uris":["file://a",null]}""");
		var result = await Client(handler).Get(include: ChromaGetInclude.Uris);
		Assert.That(handler.Body.GetProperty("include").EnumerateArray().Select(x => x.GetString()), Is.EqualTo(new[] { "uris" }));
		Assert.That(result.Select(x => x.Uri), Is.EqualTo(new[] { "file://a", null }));
	}

	[Test]
	public async Task QueryIncludesAndReadsUris()
	{
		var handler = new RecordingHandler("""{"ids":[["a"]],"uris":[["file://a"]]}""");
		var result = await Client(handler).Query(Embedding, include: ChromaQueryInclude.Uris);
		Assert.That(handler.Body.GetProperty("include").EnumerateArray().Select(x => x.GetString()), Is.EqualTo(new[] { "uris" }));
		Assert.That(result.Single().Uri, Is.EqualTo("file://a"));
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
