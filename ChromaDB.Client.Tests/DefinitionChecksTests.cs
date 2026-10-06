using System.Net;
using ChromaDB.Client.Models;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

// What a collection definition cannot get: the space it asks for, from a collection that exists with another one, and on Chroma Cloud
// a key of the schema beyond its quota of bytes, which it takes and then rejects on every write.
[TestFixture]
public class DefinitionChecksTests
{
	static readonly ChromaCollectionDefinition Cosine = new("c") { Configuration = new() { Space = ChromaSpace.Cosine } };

	[Test]
	public async Task ACollectionThatExistsWithAnotherSpace()
	{
		var server = new VersionedServer("1.0.0", """{"id":"11111111-2222-3333-4444-555555555555","name":"c","metadata":{"hnsw:space":"l2"}}""");
		await Assert.ThatAsync(() => Client(server).GetOrCreateCollectionAsync(Cosine),
			Throws.InstanceOf<ChromaException>().With.Message.EqualTo("The collection has the space l2, not cosine: a collection that exists keeps its space."));
	}

	[Test]
	public async Task ACollectionWithTheSpaceOrWithoutOneReported()
	{
		var server = new VersionedServer("1.0.0", """{"id":"11111111-2222-3333-4444-555555555555","name":"c","metadata":{"hnsw:space":"cosine"}}""");
		Assert.That((await Client(server).GetOrCreateCollectionAsync(Cosine)).Space, Is.EqualTo(ChromaSpace.Cosine));
		server = new VersionedServer("1.0.0", """{"id":"11111111-2222-3333-4444-555555555555","name":"c"}""");
		Assert.That((await Client(server).GetOrCreateCollectionAsync(Cosine)).Space, Is.Null);
	}

	// Chroma 0.4 writes the space of the definition into the metadata of a collection that exists, whose index keeps its own: on the
	// 0.x servers the client reads the collection first, and sends no get or create when it has another space.
	[Test]
	public async Task OnChroma0TheCollectionIsReadFirst()
	{
		var server = new VersionedServer("0.6.3", """{"id":"11111111-2222-3333-4444-555555555555","name":"c","metadata":{"hnsw:space":"l2"}}""");
		await Assert.ThatAsync(() => Client(server).GetOrCreateCollectionAsync(Cosine), Throws.InstanceOf<ChromaException>().With.Message.Contains("not cosine"));
		Assert.That(server.Requests, Is.EqualTo(new[] { "GET version", "GET c" }));

		server = new VersionedServer("0.6.3", null);
		await Client(server).GetOrCreateCollectionAsync(Cosine);
		Assert.That(server.Requests, Is.EqualTo(new[] { "GET version", "GET c", "POST collections" }));

		server = new VersionedServer("0.6.3", """{"id":"11111111-2222-3333-4444-555555555555","name":"c","metadata":{"hnsw:space":"cosine"}}""");
		await Client(server).GetOrCreateCollectionAsync(Cosine);
		Assert.That(server.Requests, Is.EqualTo(new[] { "GET version", "GET c", "POST collections" }));
	}

	// Chroma 1.x keeps the metadata, so the answer tells: the client reads nothing first, and nothing for a definition without a space.
	[Test]
	public async Task OnChroma1OrWithoutASpaceNothingIsReadFirst()
	{
		var server = new VersionedServer("1.0.0", """{"id":"11111111-2222-3333-4444-555555555555","name":"c","metadata":{"hnsw:space":"cosine"}}""");
		await Client(server).GetOrCreateCollectionAsync(Cosine);
		await Client(server).GetOrCreateCollectionAsync(new ChromaCollectionDefinition("c"));
		Assert.That(server.Requests, Is.EqualTo(new[] { "GET version", "POST collections", "POST collections" }));
	}

	[TestCase(36, "https://api.trychroma.com", true)]
	[TestCase(37, "https://api.trychroma.com", false)]
	[TestCase(37, "http://localhost:8000", true)]
	public async Task KeysOfTheSchemaOnChromaCloud(int bytes, string uri, bool sent)
	{
		var key = new string('k', bytes);
		var definition = new ChromaCollectionDefinition("c") { Schema = new ChromaCollectionSchema().WithIndex(ChromaSchemaIndex.StringInverted, key) };
		var server = new FakeServer("""{"id":"11111111-2222-3333-4444-555555555555","name":"c","schema":{"defaults":{},"keys":{}}}""");
		var client = Client(server, uri);
		if (!sent)
		{
			Assert.That(() => client.CreateCollectionAsync(definition), Throws.ArgumentException.With.Message.Contains("more than 36 bytes"));
			Assert.That(() => client.GetOrCreateCollectionAsync(definition), Throws.ArgumentException);
			Assert.That(server.Requests, Is.Empty);
			return;
		}
		await client.GetOrCreateCollectionAsync(definition);
		Assert.That(server.Requests, Is.EqualTo(new[] { "POST" }));
	}

	// A definition without a schema has no keys to check.
	[Test]
	public async Task NoSchemaOnChromaCloud()
	{
		var server = new FakeServer("""{"id":"11111111-2222-3333-4444-555555555555","name":"c"}""");
		await Client(server, "https://api.trychroma.com").CreateCollectionAsync(new ChromaCollectionDefinition("c"));
		Assert.That(server.Requests, Is.EqualTo(new[] { "POST" }));
	}

	// A key counts in bytes of UTF-8: 19 characters of two bytes each are 38 bytes.
	[Test]
	public void BytesNotCharacters()
	{
		var definition = new ChromaCollectionDefinition("c") { Schema = new ChromaCollectionSchema().WithIndex(ChromaSchemaIndex.StringInverted, new string('é', 19)) };
		Assert.That(() => Client(new FakeServer("{}"), "https://api.trychroma.com").CreateCollectionAsync(definition), Throws.ArgumentException);
	}

	static ChromaClient Client(HttpMessageHandler handler, string uri = "http://localhost:8000")
		=> new(new ChromaConfigurationOptions(uri), new HttpClient(handler));

	// Answers the version, the collection c, or 404 when it is null, and the get or create with it; records the requests.
	sealed class VersionedServer(string version, string? collection) : HttpMessageHandler
	{
		public List<string> Requests { get; } = [];

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var path = request.RequestUri!.AbsolutePath.Split('/').Last();
			Requests.Add($"{request.Method.Method} {path}");
			var (status, body) = path == "version" ? (HttpStatusCode.OK, "\"" + version + "\"")
				: collection is null && request.Method == HttpMethod.Get ? (HttpStatusCode.NotFound, """{"error":"NotFoundError","message":"Collection [c] does not exist"}""")
				: (HttpStatusCode.OK, collection ?? """{"id":"11111111-2222-3333-4444-555555555555","name":"c","metadata":{"hnsw:space":"cosine"}}""");
			return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
		}
	}

	// Answers every request with the collection; records the methods.
	sealed class FakeServer(string collection) : HttpMessageHandler
	{
		public List<string> Requests { get; } = [];

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Requests.Add(request.Method.Method);
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(collection) });
		}
	}
}
