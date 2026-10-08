using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using ChromaDB.Client.Models;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

// The upsert strategies, against a fake server: Chroma 1.0.21 to 1.5.9 may lose a record from the vector index after an upsert or an
// update with embeddings of records that exist (KD-49).
[TestFixture]
public class UpsertStrategyTests
{
	static readonly ReadOnlyMemory<float> Same = new([1f, 0f]);
	static readonly ReadOnlyMemory<float> Old = new([0f, 1f]);
	static readonly ReadOnlyMemory<float> New = new([1f, 1f]);

	[Test]
	public void TheOption()
	{
		var options = new ChromaConfigurationOptions("http://localhost:8000");
		Assert.That(options.UpsertStrategy, Is.EqualTo(ChromaUpsertStrategy.Server));
		Assert.That(options.WithUpsertStrategy(ChromaUpsertStrategy.Server).WithDatabase("d").UpsertStrategy, Is.EqualTo(ChromaUpsertStrategy.Server));
	}

	// The default: the upsert and the update go as they are, without reading the records.
	[Test]
	public async Task ServerByDefault()
	{
		var server = new Server();
		server.Store("a", Old, """{"k":1}""", "doc a");
		var collection = Collection(server, null);
		await collection.UpsertAsync(["a"], [New]);
		await collection.UpdateAsync(["a"], [Same]);
		Assert.That(server.Writes(), Is.EqualTo(new[] { "upsert a", "update a" }));
	}

	// SkipUnchangedEmbeddings: a keeps its embedding and is updated without it, which does not touch the vector index; b changes it
	// and c is new, and they go by the upsert of the server. The fields go as given: without documents, the update sends none.
	[Test]
	public async Task SkipUnchangedEmbeddingsInAnUpsert()
	{
		var server = new Server();
		server.Store("a", Same, """{"k":1}""", "doc a");
		server.Store("b", Old, """{"k":1}""", "doc b");
		await Collection(server, ChromaUpsertStrategy.SkipUnchangedEmbeddings).UpsertAsync(new ChromaRecords(["a", "b", "c"])
		{
			Embeddings = [Same, New, New],
			Metadatas = [new Dictionary<string, object> { ["k"] = 2L }, new Dictionary<string, object> { ["k"] = 2L }, new Dictionary<string, object> { ["k"] = 3L }],
		});
		Assert.That(server.Writes(), Is.EqualTo(new[] { "get a,b,c", "update a", "upsert b,c" }));
		var update = server.Body("update");
		Assert.That(update.TryGetProperty("embeddings", out var embeddings) && embeddings.ValueKind != JsonValueKind.Null, Is.False);
		Assert.That(update.GetProperty("metadatas")[0].ToString(), Is.EqualTo("""{"k":2}"""));
		Assert.That(update.TryGetProperty("documents", out var documents) && documents.ValueKind != JsonValueKind.Null, Is.False);
		Assert.That(server.Body("upsert").GetProperty("embeddings")[0].EnumerateArray().Select(x => x.GetSingle()), Is.EqualTo(new[] { 1f, 1f }));
	}

	[Test]
	public async Task SkipUnchangedEmbeddingsInAnUpdate()
	{
		var server = new Server();
		server.Store("a", Same, """{"k":1}""", "doc a");
		await Collection(server, ChromaUpsertStrategy.SkipUnchangedEmbeddings).UpdateAsync(["a", "z"], [Same, New]);
		Assert.That(server.Writes(), Is.EqualTo(new[] { "get a,z", "update a", "update z" }));
	}

	// The defect is of Chroma 1.x: on Chroma 0.x the writes go as they are, whatever the strategy.
	[Test]
	public async Task OnChroma0TheWritesGoAsTheyAre()
	{
		var server = new Server { Version = "0.6.3" };
		server.Store("a", Same, """{"k":1}""", "doc a");
		await Collection(server, ChromaUpsertStrategy.SkipUnchangedEmbeddings).UpsertAsync(["a"], [Same]);
		Assert.That(server.Writes(), Is.EqualTo(new[] { "upsert a" }));
	}

	// Without embeddings an update or an upsert does not touch the vector index: it goes as it is.
	[Test]
	public async Task WithoutEmbeddings()
	{
		var server = new Server();
		server.Store("a", Old, """{"k":1}""", "doc a");
		await Collection(server, ChromaUpsertStrategy.SkipUnchangedEmbeddings).UpdateAsync(["a"], metadatas: [new Dictionary<string, object> { ["k"] = 2L }]);
		Assert.That(server.Writes(), Is.EqualTo(new[] { "update a" }));
	}

	// In a cosine collection Chroma 1.x gives an embedding back 1 or 2 ulp off (KD-8): the same embedding sent again is unchanged, and
	// one that differs by more is not.
	[Test]
	public async Task CosineEmbeddingsReadBackOff()
	{
		var server = new Server();
		server.Store("a", new([0.31594023f, 0.19201598f, 0.9178214f, -0.39999998f]), """{"k":1}""", "doc a");
		var collection = new ChromaCollection("c") { Id = Guid.Empty, Metadata = new Dictionary<string, object> { ["hnsw:space"] = "cosine" } };
		var client = new ChromaCollectionClient(collection, new ChromaConfigurationOptions("http://localhost:8000").WithUpsertStrategy(ChromaUpsertStrategy.SkipUnchangedEmbeddings), new HttpClient(server));
		await client.UpsertAsync(["a"], [new([0.31594023f, 0.19201598f, 0.91782147f, -0.4f])]);
		Assert.That(server.Writes(), Is.EqualTo(new[] { "get a", "update a" }));
		server.Bodies.Clear();
		await client.UpsertAsync(["a"], [new([0.31594023f, 0.19201598f, 0.9178215f, -0.40001f])]);
		Assert.That(server.Writes(), Is.EqualTo(new[] { "get a", "upsert a" }));
	}

	// An id given more than once goes to the server in the order given, as with Server: Chroma applies the writes in order, and the last
	// one stays.
	[Test]
	public async Task RepeatedIdsGoInTheOrderGiven()
	{
		var server = new Server();
		server.Store("a", Same, """{"k":1}""", "doc a");
		await Collection(server, null).UpsertAsync(["a", "a"], [New, Same]);
		var expected = server.Body("upsert").ToString();
		server.Bodies.Clear();
		await Collection(server, ChromaUpsertStrategy.SkipUnchangedEmbeddings).UpsertAsync(["a", "a"], [New, Same]);
		Assert.That(server.Writes(), Is.EqualTo(new[] { "get a", "upsert a,a" }));
		Assert.That(server.Body("upsert").ToString(), Is.EqualTo(expected));
	}

	static ChromaCollectionClient Collection(HttpMessageHandler handler, ChromaUpsertStrategy? strategy, int? maxBatchSize = null, ChromaConfigurationOptions? options = null)
	{
		options ??= new ChromaConfigurationOptions("http://localhost:8000");
		options = maxBatchSize is { } size ? options.WithBatchSplitting(size) : options;
		return new(Guid.Empty, "c", strategy is { } chosen ? options.WithUpsertStrategy(chosen) : options, new HttpClient(handler));
	}

	// Keeps the records it is given, answers a get of ids with them, and records the writes and their bodies. The first FailAdds adds
	// answer 500, and so does the delete number FailDelete. The collection c is read by name, with its id; with GoneOnAdd an add finds
	// it gone, and the name has another id from then on.
	internal sealed class Server : HttpMessageHandler
	{
		readonly Dictionary<string, (float[] Embedding, string Metadata, string? Document)> _stored = [];
		int _deletes;
		string _id = "11111111-1111-1111-1111-111111111111";
		string? _gone;
		public int FailAdds { get; set; }
		public int FailDelete { get; set; }
		public string Version { get; set; } = "1.0.0";
		public bool GoneOnAdd { get; set; }
		public List<(string Path, JsonElement Body)> Bodies { get; } = [];

		public void Store(string id, ReadOnlyMemory<float> embedding, string metadata, string? document) => _stored[id] = (embedding.ToArray(), metadata, document);

		public IEnumerable<string> Writes()
			=> Bodies.Select(x => $"{x.Path} {string.Join(",", x.Body.GetProperty("ids").EnumerateArray().Select(id => id.GetString()))}");

		public JsonElement Body(string path) => Bodies.Single(x => x.Path == path).Body;

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var path = request.RequestUri!.AbsolutePath.Split('/').Last();
			if (path == "pre-flight-checks")
			{
				return Answer(HttpStatusCode.OK, """{"max_batch_size":100,"supports_base64_encoding":false}""");
			}
			if (path == "version")
			{
				return Answer(HttpStatusCode.OK, $"\"{Version}\"");
			}
			if (path == "c")
			{
				return Answer(HttpStatusCode.OK, "{\"id\":\"" + _id + "\",\"name\":\"c\",\"configuration_json\":{\"hnsw\":{\"space\":\"l2\"}}}");
			}
			if (_gone is { } gone && request.RequestUri.AbsolutePath.Contains(gone))
			{
				return Answer(HttpStatusCode.NotFound, """{"error":"NotFoundError","message":"Collection does not exist"}""");
			}
			if (path == "count")
			{
				return Answer(HttpStatusCode.OK, "2");
			}
			var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement.Clone();
			Bodies.Add((path, body));
			if (path == "add" && GoneOnAdd)
			{
				(_gone, _id, GoneOnAdd) = (_id, "22222222-2222-2222-2222-222222222222", false);
				return Answer(HttpStatusCode.NotFound, """{"error":"NotFoundError","message":"Collection does not exist"}""");
			}
			if (path == "add" && FailAdds-- > 0)
			{
				return Answer(HttpStatusCode.InternalServerError, """{"error":"InternalError","message":"add failed"}""");
			}
			if (path == "delete" && ++_deletes == FailDelete)
			{
				return Answer(HttpStatusCode.InternalServerError, """{"error":"InternalError","message":"delete failed"}""");
			}
			if (path != "get")
			{
				return Answer(HttpStatusCode.OK, "{}");
			}
			var ids = body.GetProperty("ids").EnumerateArray().Select(x => x.GetString()!).Where(_stored.ContainsKey).ToList();
			var answer = new JsonObject
			{
				["ids"] = new JsonArray(ids.Select(id => (JsonNode)JsonValue.Create(id)!).ToArray()),
				["embeddings"] = new JsonArray(ids.Select(id => (JsonNode)new JsonArray(_stored[id].Embedding.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray())).ToArray()),
				["metadatas"] = new JsonArray(ids.Select(id => JsonNode.Parse(_stored[id].Metadata)).ToArray()),
				["documents"] = new JsonArray(ids.Select(id => (JsonNode?)JsonValue.Create(_stored[id].Document)).ToArray()),
				["uris"] = new JsonArray(ids.Select(_ => (JsonNode?)null).ToArray()),
			};
			return Answer(HttpStatusCode.OK, answer.ToJsonString());
		}

		static HttpResponseMessage Answer(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body) };
	}
}
