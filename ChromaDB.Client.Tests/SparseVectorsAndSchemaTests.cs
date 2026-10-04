using System.Net;
using System.Text.Json;
using ChromaDB.Client.Models;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

// Sparse vectors and the schema of a collection, as the Python client of Chroma 1.5.9 writes them and Chroma Cloud answers, against a fake server.
[TestFixture]
public class SparseVectorsAndSchemaTests
{
	const string CollectionsPath = "/api/v2/tenants/default_tenant/databases/default_database/collections";
	const string Bm25Schema = """{"defaults":{},"keys":{"doc_bm25":{"sparse_vector":{"sparse_vector_index":{"enabled":true,"config":{"embedding_function":{"type":"known","name":"chroma_bm25","config":{"k":1.2,"b":0.75,"avg_doc_length":256,"token_max_length":40,"include_tokens":false}},"source_key":"#document","bm25":true}}}}}}""";

	[Test]
	public void SparseVectorJson()
	{
		Assert.That(new ChromaSparseVector([1, 5], [0.5f, 0.7f]).ToString(), Is.EqualTo("""{"#type":"sparse_vector","indices":[1,5],"values":[0.5,0.7]}"""));
		Assert.That(new ChromaSparseVector([2], [1f], ["apple"]).ToString(), Is.EqualTo("""{"#type":"sparse_vector","indices":[2],"values":[1],"tokens":["apple"]}"""));
	}

	// The rules of Chroma and of its Python client: Chroma Cloud rejects indices out of order with 400.
	[Test]
	public void SparseVectorThatChromaRejects()
	{
		Assert.That(() => new ChromaSparseVector([1, 2], [1f]), Throws.ArgumentException);
		Assert.That(() => new ChromaSparseVector([-1], [1f]), Throws.ArgumentException);
		Assert.That(() => new ChromaSparseVector([3, 1], [1f, 2f]), Throws.ArgumentException);
		Assert.That(() => new ChromaSparseVector([1, 1], [1f, 2f]), Throws.ArgumentException);
		Assert.That(() => new ChromaSparseVector([1], [1f], ["a", "b"]), Throws.ArgumentException);
	}

	// The checked values cannot change afterwards, not even through a cast.
	[Test]
	public void SparseVectorIsReadOnly()
	{
		var vector = new ChromaSparseVector([1, 5], [0.5f, 0.7f], ["a", "b"]);
		Assert.That(vector.Indices, Is.Not.InstanceOf<int[]>());
		Assert.That(vector.Values, Is.Not.InstanceOf<float[]>());
		Assert.That(vector.Tokens, Is.Not.InstanceOf<string[]>());
	}

	[Test]
	public void KnnWithASparseVector()
	{
		Assert.That(ChromaRank.SparseKnn(new ChromaSparseVector([1, 5], [1f, 1f]), "doc_bm25", returnRank: true).ToString(),
			Is.EqualTo("""{"$knn":{"query":{"#type":"sparse_vector","indices":[1,5],"values":[1,1]},"key":"doc_bm25","limit":16,"return_rank":true}}"""));
	}

	[Test]
	public async Task AddSparseVectorsInMetadata()
	{
		var server = new FakeServer(r => r.Path switch
		{
			var path when path.EndsWith("/version") => (HttpStatusCode.OK, "\"1.0.0\""),
			var path when path.EndsWith("/add") => (HttpStatusCode.Created, "{}"),
			_ => (HttpStatusCode.NotFound, ""),
		});
		await CollectionClient(server).Add(new ChromaRecords(["a"]) { Metadatas = [new() { ["doc_bm25"] = new ChromaSparseVector([1], [0.5f]), ["x"] = 1 }] });
		var add = server.Requests.Single(x => x.Path.EndsWith("/add"));
		Assert.That(add.Body.GetProperty("metadatas")[0].GetRawText(), Is.EqualTo("""{"doc_bm25":{"#type":"sparse_vector","indices":[1],"values":[0.5]},"x":1}"""));
	}

	// Chroma 0.6.3 accepts sparse vectors in metadata and stores the metadata as null: nothing is sent, also for a sparse vector read
	// with the default metadata values, which is a JsonElement.
	[TestCase(false)]
	[TestCase(true)]
	public async Task SparseVectorsInMetadataOnChroma0(bool asJsonElement)
	{
		var server = new FakeServer(r => r.Path.EndsWith("/version") ? (HttpStatusCode.OK, "\"0.6.3\"") : (HttpStatusCode.OK, "true"));
		object vector = asJsonElement
			? JsonDocument.Parse("""{"#type":"sparse_vector","indices":[1],"values":[0.5],"tokens":null}""").RootElement.Clone()
			: new ChromaSparseVector([1], [0.5f]);
		await Assert.ThatAsync(() => CollectionClient(server).Add(new ChromaRecords(["a"]) { Embeddings = [new([1f])], Metadatas = [new() { ["v"] = vector }] }),
			Throws.InstanceOf<ChromaException>().With.Message.Contains("sparse vectors"));
		Assert.That(server.Requests.Any(x => x.Path.EndsWith("/add")), Is.False);
	}

	// Chroma Cloud sends "#type" and "tokens": null. Exact metadata values give a ChromaSparseVector, the default ones a JsonElement, as before.
	[TestCase(ChromaMetadataValues.Exact)]
	[TestCase(ChromaMetadataValues.Inferred)]
	public async Task ReadSparseVectorsInMetadata(ChromaMetadataValues metadataValues)
	{
		var server = new FakeServer(_ => (HttpStatusCode.OK, """{"ids":["a"],"metadatas":[{"doc_bm25":{"#type":"sparse_vector","indices":[1,5],"values":[0.5,0.7],"tokens":null},"x":1}]}"""));
		var options = new ChromaConfigurationOptions("http://localhost:8000").WithMetadataValues(metadataValues);
		var value = (await CollectionClient(server, options).Get("a", include: ChromaGetInclude.Metadatas))!.Metadata!["doc_bm25"];
		if (metadataValues == ChromaMetadataValues.Exact)
		{
			var vector = (ChromaSparseVector)value;
			Assert.That((vector.Indices, vector.Values, vector.Tokens), Is.EqualTo(((IReadOnlyList<int>)[1, 5], (IReadOnlyList<float>)[0.5f, 0.7f], (IReadOnlyList<string>?)null)));
		}
		else
		{
			Assert.That(((JsonElement)value).GetProperty("#type").GetString(), Is.EqualTo("sparse_vector"));
		}
	}

	[Test]
	public async Task CreateCollectionWithASchema()
	{
		var server = new FakeServer(_ => (HttpStatusCode.OK, $$$"""{"id":"11111111-2222-3333-4444-555555555555","name":"c","schema":{{{Bm25Schema}}}}"""));
		var definition = new ChromaCollectionDefinition("c") { Schema = new ChromaCollectionSchema().WithSparseVectorIndex("doc_bm25", ChromaSearchKeys.Document, bm25: true, ChromaEmbeddingFunctionReference.ChromaBm25()) };
		var collection = await Client(server).CreateCollection(definition);
		Assert.That(server.Requests.Single().Body.GetProperty("schema").GetRawText(), Is.EqualTo(Bm25Schema));
		var index = collection.SparseVectorIndexes.Single();
		Assert.That((index.Key, index.SourceKey, index.Bm25, index.EmbeddingFunction), Is.EqualTo(("doc_bm25", "#document", true, "chroma_bm25")));
	}

	// Without a schema the request stays as before.
	[Test]
	public async Task CreateCollectionWithoutASchema()
	{
		var server = new FakeServer(_ => (HttpStatusCode.OK, """{"id":"11111111-2222-3333-4444-555555555555","name":"c"}"""));
		var collection = await Client(server).CreateCollection("c");
		Assert.That(server.Requests.Single().Body.TryGetProperty("schema", out _), Is.False);
		Assert.That(collection.SparseVectorIndexes, Is.Empty);
	}

	// Chroma 1.0.0 to 1.2.2 create the collection without the schema: CreateCollection deletes it, GetOrCreateCollection keeps it.
	[TestCase(false)]
	[TestCase(true)]
	public async Task SchemaThatTheServerIgnores(bool getOrCreate)
	{
		var server = new FakeServer(r => r.Method == "DELETE" ? (HttpStatusCode.OK, "{}") : (HttpStatusCode.OK, """{"id":"11111111-2222-3333-4444-555555555555","name":"c","schema":null}"""));
		var definition = new ChromaCollectionDefinition("c") { Schema = new ChromaCollectionSchema().WithSparseVectorIndex("v") };
		var client = Client(server);
		await Assert.ThatAsync(() => getOrCreate ? client.GetOrCreateCollection(definition) : client.CreateCollection(definition),
			Throws.InstanceOf<ChromaException>().With.Message.Contains("Chroma 1.3.0"));
		Assert.That(server.Requests.Select(x => x.Line), Is.EqualTo(getOrCreate
			? new[] { $"POST {CollectionsPath}" }
			: new[] { $"POST {CollectionsPath}", $"DELETE {CollectionsPath}/c" }));
	}

	// Chroma Cloud answers 400 "If source_key is provided then embedding_function must also be provided".
	[Test]
	public void SourceKeyWithoutEmbeddingFunction()
	{
		Assert.That(() => new ChromaCollectionSchema().WithSparseVectorIndex("doc_bm25", ChromaSearchKeys.Document), Throws.ArgumentException);
		Assert.That(() => new ChromaCollectionSchema().WithSparseVectorIndex("doc_bm25", bm25: true), Throws.Nothing);
	}

	static ChromaClient Client(HttpMessageHandler handler)
		=> new(new ChromaConfigurationOptions("http://localhost:8000"), new HttpClient(handler));

	static ChromaCollectionClient CollectionClient(HttpMessageHandler handler, ChromaConfigurationOptions? options = null)
		=> new(new ChromaCollection("c") { Id = Guid.Parse("11111111-2222-3333-4444-555555555555") }, options ?? new ChromaConfigurationOptions("http://localhost:8000"), new HttpClient(handler));

	sealed record Request(string Method, string Path, string Line, JsonElement Body);

	sealed class FakeServer(Func<Request, (HttpStatusCode Status, string Body)> answer) : HttpMessageHandler
	{
		public List<Request> Requests { get; } = [];

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var text = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
			var recorded = new Request(request.Method.Method, request.RequestUri!.AbsolutePath, $"{request.Method.Method} {request.RequestUri.PathAndQuery}",
				text is { Length: > 0 } ? JsonDocument.Parse(text).RootElement.Clone() : default);
			Requests.Add(recorded);
			var (status, response) = answer(recorded);
			return new HttpResponseMessage(status) { Content = new StringContent(response) };
		}
	}
}
