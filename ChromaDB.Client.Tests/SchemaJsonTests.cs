using System.Net;
using System.Text.Json;
using ChromaDB.Client.Models;
using Microsoft.Extensions.AI;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

// A collection created with the schema of another as JSON, as a restore creates it: the request carries the schema as it is, also
// the fields that the client does not know, against a fake server.
[TestFixture]
public class SchemaJsonRequestTests
{
	const string Schema = """{"defaults":{"float_list":{"vector_index":{"enabled":true,"config":{"space":"cosine","hnsw":{"ef_construction":120,"max_neighbors":20,"ef_search":110}}}}},"keys":{"x":{"string":{"string_inverted_index":{"enabled":false,"config":{}}}}},"field_of_a_later_chroma":{"kept":[1,2.0]}}""";
	const string Collection = """{"id":"11111111-2222-3333-4444-555555555555","name":"c","schema":""" + Schema + "}";

	[Test]
	public async Task TheSchemaGoesAsItIs()
	{
		var server = new RecordingServer(Collection);
		await Client(server).CreateCollectionAsync(new ChromaCollectionDefinition("c") { SchemaJson = Json(Schema), Metadata = new Dictionary<string, object> { ["a"] = 1 } });
		var body = Json(server.Bodies.Single());
		Assert.That(body.GetProperty("schema").GetRawText(), Is.EqualTo(Schema));
		Assert.That(body.TryGetProperty("configuration", out _), Is.False);
		Assert.That(body.GetProperty("metadata").GetRawText(), Is.EqualTo("""{"a":1}"""));
	}

	// The schema keeps the embedding function it has: the one that the generator declares is not added.
	[Test]
	public async Task TheSchemaKeepsItsEmbeddingFunction()
	{
		var server = new RecordingServer(Collection);
		await Client(server).GetOrCreateCollectionAsync(new ChromaCollectionDefinition("c") { SchemaJson = Json(Schema), EmbeddingGenerator = new ChromaDefaultGenerator() });
		var body = Json(server.Bodies.Single());
		Assert.That(body.GetProperty("schema").GetRawText(), Is.EqualTo(Schema));
		Assert.That(body.TryGetProperty("configuration", out _), Is.False);
	}

	[Test]
	public void SchemaJsonIsTheWholeSchema()
	{
		var server = new RecordingServer(Collection);
		var client = Client(server);
		Assert.That(() => client.CreateCollectionAsync(new ChromaCollectionDefinition("c") { SchemaJson = Json(Schema), Schema = new ChromaCollectionSchema() }), Throws.ArgumentException.With.Message.Contains("SchemaJson"));
		Assert.That(() => client.CreateCollectionAsync(new ChromaCollectionDefinition("c") { SchemaJson = Json(Schema), Configuration = new() { Space = ChromaSpace.Cosine } }), Throws.ArgumentException.With.Message.Contains("SchemaJson"));
		Assert.That(() => client.GetOrCreateCollectionAsync(new ChromaCollectionDefinition("c") { SchemaJson = Json("[]") }), Throws.ArgumentException.With.Message.Contains("SchemaJson"));
		Assert.That(server.Bodies, Is.Empty);
	}

	// As a schema built with ChromaCollectionSchema: Chroma Cloud creates the collection with a key beyond its quota of bytes, and then
	// rejects every write with the key.
	[TestCase(36, true)]
	[TestCase(37, false)]
	public async Task KeysOfSchemaJsonOnChromaCloud(int bytes, bool sent)
	{
		var server = new RecordingServer(Collection);
		var definition = new ChromaCollectionDefinition("c") { SchemaJson = Json("""{"defaults":{},"keys":{""" + JsonSerializer.Serialize(new string('k', bytes)) + """:{}}}""") };
		var client = Client(server, "https://api.trychroma.com");
		if (sent)
		{
			await client.CreateCollectionAsync(definition);
			Assert.That(server.Bodies, Has.Count.EqualTo(1));
			return;
		}
		Assert.That(() => client.CreateCollectionAsync(definition), Throws.ArgumentException.With.Message.Contains("more than 36 bytes"));
		Assert.That(server.Bodies, Is.Empty);
	}

	static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

	static ChromaClient Client(HttpMessageHandler handler, string uri = "http://localhost:8000")
		=> new(new ChromaConfigurationOptions(uri), new HttpClient(handler));

	// Answers the version as a 1.x server, and every other request with the collection; records the bodies of the posts.
	sealed class RecordingServer(string collection) : HttpMessageHandler
	{
		public List<string> Bodies { get; } = [];

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			if (request.RequestUri!.AbsolutePath.EndsWith("/version"))
			{
				return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("\"1.5.9\"") };
			}
			if (request.Method == HttpMethod.Post)
			{
				Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
			}
			return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(collection) };
		}
	}

	// A generator with the metadata of the embedding function of Chroma, which a new collection declares as "default".
	sealed class ChromaDefaultGenerator : IEmbeddingGenerator<string, Embedding<float>>
	{
		public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
			=> Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(values.Select(_ => new Embedding<float>(new float[] { 1f }))));

		public object? GetService(Type serviceType, object? serviceKey = null)
			=> serviceType == typeof(EmbeddingGeneratorMetadata) ? new EmbeddingGeneratorMetadata("chroma", null, "all-MiniLM-L6-v2") : null;

		public void Dispose() { }
	}
}

// A collection created on a server with the schema that another collection has there: the schema read back is the same.
[TestFixture]
public class SchemaJsonServerTests : ChromaTestsBase
{
	[Test]
	public async Task ACollectionWithTheSchemaOfAnother()
	{
		Assume.That(SchemaApplied, Is.True, "Chroma 1.3.0 and later apply the schema.");
		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		var first = await client.CreateCollectionAsync(new ChromaCollectionDefinition($"collection{Random.Shared.Next()}")
		{
			Configuration = new() { Space = ChromaSpace.Cosine },
			Schema = new ChromaCollectionSchema().WithoutIndex(ChromaSchemaIndex.StringInverted, "k"),
		});
		var schema = (await client.GetCollectionAsync(first.Name)).SchemaJson!.Value;
		var second = await client.CreateCollectionAsync(new ChromaCollectionDefinition($"collection{Random.Shared.Next()}") { SchemaJson = schema });
		var read = (await client.GetCollectionAsync(second.Name)).SchemaJson!.Value;
		Assert.That(JsonElement.DeepEquals(read, schema), Is.True, $"{read.GetRawText()} is not {schema.GetRawText()}");
		Assert.That(second.Space, Is.EqualTo(ChromaSpace.Cosine));
	}
}
