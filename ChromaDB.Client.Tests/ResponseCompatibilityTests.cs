using System.Net;
using ChromaDB.Client.Models;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

// Chroma 0.4.10 to 0.4.15 answer get and query without "uris", "data" and "included".
[TestFixture]
public class ResponseCompatibilityTests
{
	[Test]
	public async Task GetWithoutUris()
	{
		var client = Client("""{"ids":["a"],"embeddings":null,"metadatas":[null],"documents":["first"]}""");
		var result = await client.Get(["a"]);
		Assert.That(result.Single().Id, Is.EqualTo("a"));
		Assert.That(result.Single().Document, Is.EqualTo("first"));
	}

	[Test]
	public async Task QueryWithoutUris()
	{
		var client = Client("""{"ids":[["a"]],"embeddings":null,"metadatas":[[null]],"documents":[["first"]],"distances":[[0.5]]}""");
		var result = await client.Query(new ReadOnlyMemory<float>([1f, 0f]), nResults: 1);
		Assert.That(result.Single().Id, Is.EqualTo("a"));
		Assert.That(result.Single().Document, Is.EqualTo("first"));
		Assert.That(result.Single().Distance, Is.EqualTo(0.5f));
	}

	[Test]
	public async Task GetWithIdsOnly()
	{
		var client = Client("""{"ids":["a","b"]}""");
		var result = await client.Get();
		Assert.That(result.Select(x => x.Id), Is.EqualTo(new[] { "a", "b" }));
	}

	[Test]
	public async Task QueryWithIdsOnly()
	{
		var client = Client("""{"ids":[["a","b"]]}""");
		var result = await client.Query(new ReadOnlyMemory<float>([1f, 0f]), nResults: 2);
		Assert.That(result.Select(x => x.Id), Is.EqualTo(new[] { "a", "b" }));
		Assert.That(result.Select(x => x.Distance), Has.All.Null);
	}

	static ChromaCollectionClient Client(string response)
	{
		var httpClient = new HttpClient(new FixedResponseHandler(response));
		var collection = new ChromaCollection("collection") { Id = Guid.NewGuid() };
		return new ChromaCollectionClient(collection, new ChromaConfigurationOptions("http://localhost:8000"), httpClient);
	}

	sealed class FixedResponseHandler(string response) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			=> Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response) });
	}
}
