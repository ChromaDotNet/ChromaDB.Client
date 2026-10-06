using System.Net;
using System.Text.Json;
using ChromaDB.Client.Models;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

// A BM25 index on the text of a key: the client names its key, finds it, and keeps the key for the vectors it computes.
[TestFixture]
public class Bm25IndexTests
{
	static readonly ReadOnlyMemory<float> Embedding = new([1f, 0f]);

	[Test]
	public void KeysNamedAfterTheSource()
	{
		var indexes = new ChromaCollectionSchema().WithBm25Index("title").WithBm25Index(ChromaSearchKeys.Document).ToString();
		Assert.That(indexes, Does.Contain("\"title_bm25\":").And.Contain("\"document_bm25\":"));
		Assert.That(indexes, Does.Contain("\"source_key\":\"title\"").And.Contain("\"source_key\":\"#document\""));
		Assert.That(() => new ChromaCollectionSchema().WithBm25Index(null!), Throws.ArgumentNullException);
	}

	// The index of the document copy key can be the one on the documents.
	[Test]
	public async Task FindBm25Index()
	{
		var client = Client(new FakeServer(), Bm25Index("document_bm25", "#document") + "," + Bm25Index("title_bm25", "title"));
		Assert.That((await client.FindBm25IndexAsync("title"))?.Key, Is.EqualTo("title_bm25"));
		Assert.That(await client.FindBm25IndexAsync("text"), Is.Null);
		Assert.That((await client.WithDocumentCopyKey("text").FindBm25IndexAsync("text"))?.Key, Is.EqualTo("document_bm25"));
	}

	// A write gives the key of the index a sparse vector, or null to delete it, and nothing else.
	[Test]
	public async Task KeyOfTheIndex()
	{
		var client = Client(new FakeServer(), Bm25Index("title_bm25", "title"));
		var records = new ChromaRecords(["a"]) { Embeddings = [Embedding], Metadatas = [new Dictionary<string, object> { ["title"] = "apple", ["title_bm25"] = "mine" }] };
		Assert.That(() => client.AddAsync(records), Throws.ArgumentException.With.Message.Contains("title_bm25"));
		await client.AddAsync(new ChromaRecords(["a"]) { Embeddings = [Embedding], Metadatas = [new Dictionary<string, object> { ["title"] = "apple", ["title_bm25"] = new ChromaSparseVector([1], [1f]) }] });
	}

	// An index added with ifSupported goes to Chroma Cloud only: a single server rejects sparse vector indexes, and gets the schema
	// without it, or no schema when nothing else is in it.
	[TestCase("https://api.trychroma.com", "title_bm25", "title,title_bm25", "title_bm25", "title_bm25", "title_bm25")]
	[TestCase("http://localhost:8000", null, "title", "title_bm25", "", "")]
	public async Task IndexIfSupported(string uri, string? alone, string? withAnotherIndex, string? addedAgain, string? withDefaults, string? withCmek)
	{
		var server = new FakeServer();
		var client = new ChromaClient(new ChromaConfigurationOptions(uri), new HttpClient(server));
		var schema = new ChromaCollectionSchema().WithBm25Index("title", ifSupported: true);
		foreach (var other in new[] { schema, schema.WithIndex(ChromaSchemaIndex.StringInverted, "title"), schema.WithBm25Index("title"),
			schema.WithIndex(ChromaSchemaIndex.StringInverted), schema.WithGcpCmek("projects/p/locations/l/keyRings/r/cryptoKeys/k") })
		{
			await client.GetOrCreateCollectionAsync(new ChromaCollectionDefinition("c") { Schema = other });
		}
		await client.CreateCollectionAsync(new ChromaCollectionDefinition("c") { Schema = schema });
		Assert.That(server.Schemas, Is.EqualTo(new[] { alone, withAnotherIndex, addedAgain, withDefaults, withCmek, alone }));
	}

	static ChromaCollectionClient Client(HttpMessageHandler handler, string indexes)
		=> new(new ChromaCollection("c") { Id = Guid.NewGuid(), SchemaJson = JsonDocument.Parse("{\"keys\":{" + indexes + "}}").RootElement.Clone() },
			new ChromaConfigurationOptions("http://localhost:8000"), new HttpClient(handler));

	// A sparse vector index with chroma_bm25 as the schema of a collection gives it.
	static string Bm25Index(string key, string sourceKey)
		=> "\"" + key + "\":{\"sparse_vector\":{\"sparse_vector_index\":{\"enabled\":true,\"config\":{\"source_key\":\"" + sourceKey + "\",\"bm25\":true,"
			+ "\"embedding_function\":{\"type\":\"known\",\"name\":\"chroma_bm25\",\"config\":{\"k\":1.2,\"b\":0.75,\"avg_doc_length\":256,\"token_max_length\":40}}}}}}";

	// Answers the version, pre-flight-checks with a batch size, a new collection with an empty schema, and the writes with nothing;
	// records the keys of the schema of each new collection, or null without a schema.
	sealed class FakeServer : HttpMessageHandler
	{
		public List<string?> Schemas { get; } = [];

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var path = request.RequestUri!.AbsolutePath;
			if (path.EndsWith("/collections"))
			{
				var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement;
				Schemas.Add(body.TryGetProperty("schema", out var schema) && schema.ValueKind == JsonValueKind.Object
					? string.Join(",", schema.GetProperty("keys").EnumerateObject().Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal))
					: null);
			}
			var answer = path.EndsWith("/version") ? "\"1.5.9\""
				: path.EndsWith("/pre-flight-checks") ? """{"max_batch_size":100,"supports_base64_encoding":false}"""
				: path.EndsWith("/collections") ? """{"id":"11111111-2222-3333-4444-555555555555","name":"c","schema":{"defaults":{},"keys":{}}}"""
				: "{}";
			return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer) };
		}
	}
}
