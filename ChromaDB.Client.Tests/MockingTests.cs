using System.Net;
using System.Text.Json;
using ChromaDB.Client.Models;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

// The clients can be mocked, as in the Azure SDKs: a subclass made with the protected constructor overrides what a test needs.
[TestFixture]
public class MockingTests
{
	[Test]
	public async Task MockedClients()
	{
		ChromaClient client = new MockClient();
		var collection = await client.GetCollectionAsync("articles");
		var results = await client.GetCollectionClient(collection).QueryAsync(new ReadOnlyMemory<float>([1f, 0f]), ids: ["a"]);
		Assert.That((collection.Name, results.Single().Id), Is.EqualTo(("articles", "a")));
	}

	// The ids of QueryAsync with one or more embeddings go in the request, as with a ChromaQuery.
	[Test]
	public async Task QueryWithIds()
	{
		var bodies = new List<JsonElement>();
		var handler = new Handler(bodies, """{"ids":[["a"]],"distances":[[0.1]]}""");
		var collectionClient = new ChromaCollectionClient(Guid.Empty, "c", new ChromaConfigurationOptions("http://localhost:8000"), new HttpClient(handler));
		await collectionClient.QueryAsync(new ReadOnlyMemory<float>([1f, 0f]), ids: ["a", "b"]);
		await collectionClient.QueryAsync([new ReadOnlyMemory<float>([1f, 0f])], ids: ["a", "b"]);
		Assert.That(bodies.Select(x => x.GetProperty("ids").GetRawText()), Is.EqualTo(new[] { """["a","b"]""", """["a","b"]""" }));
	}

	sealed class MockClient : ChromaClient
	{
		public override Task<ChromaCollection> GetCollectionAsync(string name, string? tenant = null, string? database = null, CancellationToken cancellationToken = default)
			=> Task.FromResult(new ChromaCollection(name));

		public override ChromaCollectionClient GetCollectionClient(ChromaCollection collection)
			=> new MockCollectionClient();
	}

	sealed class MockCollectionClient : ChromaCollectionClient
	{
		public override Task<IReadOnlyList<ChromaCollectionQueryEntry>> QueryAsync(ReadOnlyMemory<float> queryEmbeddings, int nResults = 10, ChromaWhereOperator? where = null, ChromaWhereDocumentOperator? whereDocument = null, ChromaQueryInclude? include = null, IReadOnlyList<string>? ids = null, CancellationToken cancellationToken = default)
			=> Task.FromResult<IReadOnlyList<ChromaCollectionQueryEntry>>(ids!.Select(id => new ChromaCollectionQueryEntry(id)).ToList());
	}

	sealed class Handler(List<JsonElement> bodies, string answer) : HttpMessageHandler
	{
		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			bodies.Add(JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement.Clone());
			return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer) };
		}
	}
}
