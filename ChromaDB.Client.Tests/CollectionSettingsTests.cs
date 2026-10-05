using System.Net;
using System.Text.Json;
using ChromaDB.Client.Models;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

// The settings of a new collection, where they go in the request, and what the client does when the server ignores them, against a
// fake server. The schemas are written as create_index and delete_index of the Python client of Chroma 1.5.9 write them.
[TestFixture]
public class CollectionSettingsTests
{
	const string Created = """{"id":"11111111-2222-3333-4444-555555555555","name":"c"}""";
	const string CreatedWithSchema = """{"id":"11111111-2222-3333-4444-555555555555","name":"c","schema":{"defaults":{},"keys":{}}}""";
	static readonly ChromaEmbeddingFunctionReference OpenAi = ChromaEmbeddingFunctionReference.Known("openai", new Dictionary<string, object> { ["model_name"] = "text-embedding-3-small" });
	const string OpenAiJson = """{"type":"known","name":"openai","config":{"model_name":"text-embedding-3-small"}}""";

	// The space and the HNSW settings go in the "hnsw:" metadata, which every tested Chroma applies.
	[Test]
	public async Task HnswSettingsInTheMetadata()
	{
		var server = Server();
		await Client(server).CreateCollectionAsync(new ChromaCollectionDefinition("c")
		{
			Metadata = new Dictionary<string, object> { ["topic"] = "x" },
			Configuration = new()
			{
				Space = ChromaSpace.Cosine,
				Hnsw = new() { EfConstruction = 150, EfSearch = 120, MaxNeighbors = 20, ResizeFactor = 1.5, SyncThreshold = 2000, BatchSize = 200, NumThreads = 2 },
			},
		});
		var body = Create(server);
		Assert.That(body.GetProperty("metadata").GetRawText(), Is.EqualTo("""
			{"topic":"x","hnsw:space":"cosine","hnsw:construction_ef":150,"hnsw:search_ef":120,"hnsw:M":20,"hnsw:resize_factor":1.5,
			"hnsw:sync_threshold":2000,"hnsw:batch_size":200,"hnsw:num_threads":2}
			""".Replace("\n", "").Replace("\t", "")));
		Assert.That(body.TryGetProperty("configuration", out _), Is.False);
		Assert.That(body.TryGetProperty("schema", out _), Is.False);
	}

	// The SPANN settings go in the configuration, with the space: Chroma ignores the hnsw:space metadata of a request with them.
	[Test]
	public async Task SpannSettingsInTheConfiguration()
	{
		var server = Server();
		await Client(server).CreateCollectionAsync(new ChromaCollectionDefinition("c")
		{
			Configuration = new()
			{
				Space = ChromaSpace.Cosine,
				Spann = new() { SearchNprobe = 32, WriteNprobe = 16, EfConstruction = 150, EfSearch = 120, MaxNeighbors = 20, SplitThreshold = 100, MergeThreshold = 30, ReassignNeighborCount = 32 },
			},
		});
		var body = Create(server);
		Assert.That(body.GetProperty("configuration").GetRawText(), Is.EqualTo("""
			{"spann":{"search_nprobe":32,"write_nprobe":16,"ef_construction":150,"ef_search":120,"max_neighbors":20,"split_threshold":100,
			"merge_threshold":30,"reassign_neighbor_count":32,"space":"cosine"}}
			""".Replace("\n", "").Replace("\t", "")));
		Assert.That(body.GetProperty("metadata").ValueKind, Is.EqualTo(JsonValueKind.Null));
	}

	// The embedding function goes in the configuration, the space still in the metadata, which Chroma applies with it.
	[Test]
	public async Task EmbeddingFunctionInTheConfiguration()
	{
		var server = Server();
		await Client(server).CreateCollectionAsync(new ChromaCollectionDefinition("c") { Configuration = new() { Space = ChromaSpace.InnerProduct, EmbeddingFunction = OpenAi } });
		var body = Create(server);
		Assert.That(body.GetProperty("configuration").GetRawText(), Is.EqualTo($$"""{"embedding_function":{{OpenAiJson}}}"""));
		Assert.That(body.GetProperty("metadata").GetRawText(), Is.EqualTo("""{"hnsw:space":"ip"}"""));
	}

	// With a schema every setting goes in it, on the vector index of the defaults and of #embedding: Chroma rejects a configuration
	// together with a schema.
	[Test]
	public async Task SettingsInTheSchema()
	{
		var server = Server(answer: CreatedWithSchema);
		await Client(server).CreateCollectionAsync(new ChromaCollectionDefinition("c")
		{
			Configuration = new() { Space = ChromaSpace.InnerProduct, Hnsw = new() { EfConstruction = 150 }, EmbeddingFunction = OpenAi },
			Schema = new ChromaCollectionSchema().WithoutIndex(ChromaSchemaIndex.StringInverted, "title"),
		});
		var body = Create(server);
		var vector = """{"space":"ip","hnsw":{"ef_construction":150},"embedding_function":""" + OpenAiJson + "}";
		Assert.That(body.GetProperty("schema").GetRawText(), Is.EqualTo(
			"""{"defaults":{"float_list":{"vector_index":{"enabled":false,"config":""" + vector + "}}},"
			+ "\"keys\":{\"title\":{\"string\":{\"string_inverted_index\":{\"enabled\":false,\"config\":{}}}},"
			+ "\"#embedding\":{\"float_list\":{\"vector_index\":{\"enabled\":true,\"config\":" + vector + "}}}}}"));
		Assert.That(body.GetProperty("metadata").ValueKind, Is.EqualTo(JsonValueKind.Null));
		Assert.That(body.TryGetProperty("configuration", out _), Is.False);
	}

	// The SPANN settings that only a schema takes put all the settings in a schema, also without one in the definition.
	[Test]
	public async Task SpannSettingsOfTheSchemaOnly()
	{
		var server = Server(answer: CreatedWithSchema);
		await Client(server).CreateCollectionAsync(new ChromaCollectionDefinition("c")
		{
			Configuration = new() { Space = ChromaSpace.Cosine, Spann = new() { SearchNprobe = 32, NreplicaCount = 4, SearchRngEpsilon = 8, WriteRngEpsilon = 6, NumSamplesKmeans = 500, NumCentersToMergeTo = 6, CenterDriftThreshold = 0.25 } },
		});
		var body = Create(server);
		Assert.That(body.TryGetProperty("configuration", out _), Is.False);
		var config = body.GetProperty("schema").GetProperty("keys").GetProperty("#embedding").GetProperty("float_list").GetProperty("vector_index").GetProperty("config");
		Assert.That(config.GetRawText(), Is.EqualTo("""
			{"space":"cosine","spann":{"search_nprobe":32,"search_rng_epsilon":8.0,"write_rng_epsilon":6.0,"nreplica_count":4,"num_samples_kmeans":500,
			"num_centers_to_merge_to":6,"center_drift_threshold":0.25}}
			""".Replace("\n", "").Replace("\t", "")));
	}

	// Chroma rejects the two indexes together ("Multiple vector index configurations provided"): nothing is sent.
	[Test]
	public void HnswAndSpannTogether()
	{
		var server = Server();
		var definition = new ChromaCollectionDefinition("c") { Configuration = new() { Hnsw = new() { EfSearch = 1 }, Spann = new() { SearchNprobe = 1 } } };
		Assert.That(() => Client(server).CreateCollectionAsync(definition), Throws.ArgumentException);
		Assert.That(() => Client(server).GetOrCreateCollectionAsync(definition), Throws.ArgumentException);
		Assert.That(server.Requests, Is.Empty);
	}

	// Chroma 0.5.4 to 0.6.3 fail on the configuration, 0.4.10 to 0.5.3 ignore it: the client throws before sending it.
	[TestCase("spann")]
	[TestCase("embedding function")]
	public void ConfigurationOnChroma0(string setting)
	{
		var server = Server("0.6.3");
		var configuration = setting == "spann" ? new ChromaCollectionConfiguration { Spann = new() { SearchNprobe = 32 } } : new ChromaCollectionConfiguration { EmbeddingFunction = OpenAi };
		Assert.That(() => Client(server).CreateCollectionAsync(new ChromaCollectionDefinition("c") { Configuration = configuration }),
			Throws.InstanceOf<ChromaException>().With.Message.Contains("1.0.0"));
		Assert.That(server.Requests.Select(x => x.Path), Has.None.EndsWith("/collections"));
	}

	// The HNSW settings stay in the metadata on Chroma 0.x, which applies them from there.
	[Test]
	public async Task HnswSettingsOnChroma0()
	{
		var server = Server("0.6.3");
		await Client(server).CreateCollectionAsync(new ChromaCollectionDefinition("c") { Configuration = new() { Hnsw = new() { MaxNeighbors = 20 } } });
		Assert.That(Create(server).GetProperty("metadata").GetRawText(), Is.EqualTo("""{"hnsw:M":20}"""));
	}

	// A collection whose index ignores the settings: CreateCollectionAsync deletes it and throws, GetOrCreateCollectionAsync throws and
	// keeps it, since it may have existed before.
	[TestCase("hnsw", """{"hnsw":null,"spann":{"search_nprobe":64}}""", "SPANN")]
	[TestCase("spann", """{"hnsw":{"ef_search":100},"spann":null}""", "HNSW")]
	public async Task SettingsOfTheOtherIndex(string setting, string configuration, string index)
	{
		var settings = setting == "hnsw" ? new ChromaCollectionConfiguration { Hnsw = new() { EfSearch = 120 } } : new ChromaCollectionConfiguration { Spann = new() { SearchNprobe = 32 } };
		var answer = $$"""{"id":"11111111-2222-3333-4444-555555555555","name":"c","configuration_json":{{configuration}}}""";
		var server = Server(answer: answer);
		await Assert.ThatAsync(() => Client(server).CreateCollectionAsync(new ChromaCollectionDefinition("c") { Configuration = settings }),
			Throws.InstanceOf<ChromaException>().With.Message.Contains($"has an {index}").Or.Message.Contains($"has a {index}"));
		Assert.That(server.Requests.Last().Method, Is.EqualTo("DELETE"));
		server = Server(answer: answer);
		await Assert.ThatAsync(() => Client(server).GetOrCreateCollectionAsync(new ChromaCollectionDefinition("c") { Configuration = settings }), Throws.InstanceOf<ChromaException>());
		Assert.That(server.Requests.Select(x => x.Method), Has.None.EqualTo("DELETE"));
	}

	// The settings that the server applies, as it reports them: no error.
	[Test]
	public async Task SettingsOfTheSameIndex()
	{
		var server = Server(answer: """{"id":"11111111-2222-3333-4444-555555555555","name":"c","configuration_json":{"hnsw":{"ef_search":120},"spann":null}}""");
		await Client(server).CreateCollectionAsync(new ChromaCollectionDefinition("c") { Configuration = new() { Hnsw = new() { EfSearch = 120 } } });
		Assert.That(server.Requests.Select(x => x.Method), Has.None.EqualTo("DELETE"));
	}

	// Chroma ignores the hnsw:space metadata next to SPANN settings, while ChromaCollection.Space would read it back: the client rejects it.
	[Test]
	public void SpaceMetadataNextToSpannSettings()
	{
		var server = Server();
		var definition = new ChromaCollectionDefinition("c")
		{
			Metadata = new Dictionary<string, object> { ["hnsw:space"] = "l2" },
			Configuration = new() { Space = ChromaSpace.Cosine, Spann = new() { SearchNprobe = 32 } },
		};
		Assert.That(() => Client(server).CreateCollectionAsync(definition), Throws.ArgumentException.With.Message.Contains("Configuration.Space"));
		Assert.That(() => Client(server).GetOrCreateCollectionAsync(definition), Throws.ArgumentException);
		Assert.That(server.Requests, Is.Empty);
	}

	// Chroma 1.5.9 crashes on the first write with 0 neighbors and misses the nearest records with 1: nothing is sent.
	[TestCase(0)]
	[TestCase(1)]
	[TestCase(-3)]
	public async Task FewerThanTwoNeighbors(int maxNeighbors)
	{
		var server = Server();
		Assert.That(() => Client(server).CreateCollectionAsync(new ChromaCollectionDefinition("c") { Configuration = new() { Hnsw = new() { MaxNeighbors = maxNeighbors } } }),
			Throws.ArgumentException.With.Message.Contains("at least 2"));
		Assert.That(() => Client(server).GetOrCreateCollectionAsync(new ChromaCollectionDefinition("c") { Metadata = new Dictionary<string, object> { ["hnsw:M"] = (long)maxNeighbors } }),
			Throws.ArgumentException);
		Assert.That(() => Client(server).CreateCollectionAsync(new ChromaCollectionDefinition("c") { Metadata = new Dictionary<string, object> { ["hnsw:M"] = JsonDocument.Parse(maxNeighbors.ToString(System.Globalization.CultureInfo.InvariantCulture)).RootElement } }),
			Throws.ArgumentException.With.Message.Contains("at least 2"));
		Assert.That(() => Client(server).CreateCollectionAsync(new ChromaCollectionDefinition("c")
		{
			Configuration = new() { Hnsw = new() { MaxNeighbors = maxNeighbors } },
			Schema = new ChromaCollectionSchema(),
		}), Throws.ArgumentException);
		var collection = new ChromaCollectionClient(Guid.Parse("11111111-2222-3333-4444-555555555555"), "c", new ChromaConfigurationOptions("http://localhost:8000"), new HttpClient(server));
		await Assert.ThatAsync(() => collection.ModifyConfigurationAsync(new ChromaCollectionConfigurationUpdate { Hnsw = new() { MaxNeighbors = maxNeighbors } }), Throws.ArgumentException);
		Assert.That(server.Requests, Is.Empty);
		Assert.That(() => Client(server).CreateCollectionAsync(new ChromaCollectionDefinition("c") { Configuration = new() { Hnsw = new() { MaxNeighbors = 2 } } }), Throws.Nothing);
	}

	[Test]
	public void MetadataThatDisagreesWithTheSettings()
	{
		var server = Server();
		Assert.That(() => Client(server).CreateCollectionAsync(new ChromaCollectionDefinition("c")
		{
			Metadata = new Dictionary<string, object> { ["hnsw:M"] = 10 },
			Configuration = new() { Hnsw = new() { MaxNeighbors = 20 } },
		}), Throws.ArgumentException);
		Assert.That(() => Client(server).CreateCollectionAsync(new ChromaCollectionDefinition("c")
		{
			Metadata = new Dictionary<string, object> { ["hnsw:M"] = 20L, ["hnsw:resize_factor"] = 1.5f },
			Configuration = new() { Hnsw = new() { MaxNeighbors = 20, ResizeFactor = 1.5 } },
		}), Throws.Nothing);
		Assert.That(() => Client(server).CreateCollectionAsync(new ChromaCollectionDefinition("c")
		{
			Metadata = new Dictionary<string, object> { ["hnsw:M"] = JsonDocument.Parse("20").RootElement, ["hnsw:space"] = JsonDocument.Parse("\"cosine\"").RootElement },
			Configuration = new() { Space = ChromaSpace.Cosine, Hnsw = new() { MaxNeighbors = 20 } },
		}), Throws.Nothing);
		Assert.That(() => Client(server).CreateCollectionAsync(new ChromaCollectionDefinition("c")
		{
			Metadata = new Dictionary<string, object> { ["hnsw:space"] = JsonDocument.Parse("\"l2\"").RootElement },
			Configuration = new() { Space = ChromaSpace.Cosine },
		}), Throws.ArgumentException);
		Assert.That(() => Client(server).CreateCollectionAsync(new ChromaCollectionDefinition("c")
		{
			Metadata = new Dictionary<string, object> { ["hnsw:M"] = JsonDocument.Parse("10").RootElement },
			Configuration = new() { Hnsw = new() { MaxNeighbors = 20 } },
		}), Throws.ArgumentException);
	}

	[Test]
	public void IndexesOfTheSchema()
	{
		var schema = new ChromaCollectionSchema()
			.WithIndex(ChromaSchemaIndex.FullTextSearch)
			.WithoutIndex(ChromaSchemaIndex.IntInverted)
			.WithoutIndex(ChromaSchemaIndex.FloatInverted)
			.WithoutIndex(ChromaSchemaIndex.BoolInverted)
			.WithoutIndex(ChromaSchemaIndex.StringInverted, "title")
			.WithSparseVectorIndex("title", ChromaSparseIndexAlgorithm.MaxScore, bm25: true);
		Assert.That(schema.ToString(), Is.EqualTo("""
			{"defaults":{"int":{"int_inverted_index":{"enabled":false,"config":{}}},"float":{"float_inverted_index":{"enabled":false,"config":{}}},
			"bool":{"bool_inverted_index":{"enabled":false,"config":{}}}},
			"keys":{"#document":{"string":{"fts_index":{"enabled":true,"config":{}}}},
			"title":{"string":{"string_inverted_index":{"enabled":false,"config":{}}},"sparse_vector":{"sparse_vector_index":{"enabled":true,"config":{"bm25":true,"algorithm":"max_score"}}}}}}
			""".Replace("\n", "").Replace("\t", "")));
	}

	// As the Python client: the full-text search index is on #document only.
	[Test]
	public void FullTextSearchOnTheDocumentsOnly()
	{
		Assert.That(() => new ChromaCollectionSchema().WithIndex(ChromaSchemaIndex.FullTextSearch, "title"), Throws.ArgumentException);
		Assert.That(new ChromaCollectionSchema().WithoutIndex(ChromaSchemaIndex.FullTextSearch, ChromaSearchKeys.Document).ToString(),
			Is.EqualTo("""{"defaults":{},"keys":{"#document":{"string":{"fts_index":{"enabled":false,"config":{}}}}}}"""));
	}

	// The default algorithm is left out, as the Python client writes it; the schema read back gives it.
	[Test]
	public async Task SparseIndexAlgorithm()
	{
		Assert.That(new ChromaCollectionSchema().WithSparseVectorIndex("v", ChromaSparseIndexAlgorithm.Wand).ToString(), Does.Not.Contain("algorithm"));
		var server = Server(answer: """
			{"id":"11111111-2222-3333-4444-555555555555","name":"c","schema":{"defaults":{},"keys":{
			"a":{"sparse_vector":{"sparse_vector_index":{"enabled":true,"config":{"bm25":true,"algorithm":"max_score"}}}},
			"b":{"sparse_vector":{"sparse_vector_index":{"enabled":true,"config":{"bm25":true}}}}}}}
			""");
		var collection = await Client(server).CreateCollectionAsync(new ChromaCollectionDefinition("c") { Schema = new ChromaCollectionSchema().WithSparseVectorIndex("a", ChromaSparseIndexAlgorithm.MaxScore, bm25: true) });
		Assert.That(collection.SparseVectorIndexes.Select(x => (x.Key, x.Algorithm)), Is.EqualTo(new[] { ("a", ChromaSparseIndexAlgorithm.MaxScore), ("b", ChromaSparseIndexAlgorithm.Wand) }));
	}

	[Test]
	public void CustomerManagedKey()
	{
		const string key = "projects/p/locations/us/keyRings/r/cryptoKeys/k";
		Assert.That(new ChromaCollectionSchema().WithGcpCmek(key).ToString(), Is.EqualTo("{\"defaults\":{},\"keys\":{},\"cmek\":{\"gcp\":\"" + key + "\"}}"));
		Assert.That(() => new ChromaCollectionSchema().WithGcpCmek("my-key"), Throws.ArgumentException);
		Assert.That(() => new ChromaCollectionSchema().WithGcpCmek(key + "/cryptoKeyVersions/1"), Throws.ArgumentException);
		Assert.That(() => new ChromaCollectionSchema().WithGcpCmek(key + "\n"), Throws.ArgumentException);
		Assert.That(() => new ChromaCollectionSchema().WithGcpCmek("projects/p q/locations/us/keyRings/r/cryptoKeys/k"), Throws.ArgumentException);
		Assert.That(() => new ChromaCollectionSchema().WithGcpCmek(" " + key), Throws.ArgumentException);
	}

	// new_configuration with the embedding function, as Chroma writes it.
	[Test]
	public async Task ModifyTheEmbeddingFunction()
	{
		var server = new FakeServer(r => r.Method == "GET"
			? (HttpStatusCode.OK, """{"id":"11111111-2222-3333-4444-555555555555","name":"c","configuration_json":{"hnsw":{"ef_search":100},"spann":null}}""")
			: (HttpStatusCode.OK, "{}"));
		var collection = new ChromaCollectionClient(Guid.Parse("11111111-2222-3333-4444-555555555555"), "c", new ChromaConfigurationOptions("http://localhost:8000"), new HttpClient(server));
		await collection.ModifyConfigurationAsync(new ChromaCollectionConfigurationUpdate { EmbeddingFunction = OpenAi, Hnsw = new() { EfSearch = 150 } });
		Assert.That(server.Requests.Single(x => x.Method == "PUT").Body.GetRawText(),
			Is.EqualTo("""{"new_name":null,"new_metadata":null,"new_configuration":{"hnsw":{"ef_search":150},"embedding_function":""" + OpenAiJson + "}}"));
	}

	[TestCase("""{"id":"11111111-2222-3333-4444-555555555555","name":"c","dimension":3,"version":2,"log_position":7}""", 3, 2, 7L)]
	[TestCase("""{"id":"11111111-2222-3333-4444-555555555555","name":"c","dimension":null,"version":0}""", null, 0, null)]
	[TestCase("""{"id":"11111111-2222-3333-4444-555555555555","name":"c"}""", null, null, null)]
	public async Task DimensionVersionAndLogPosition(string answer, int? dimension, int? version, long? logPosition)
	{
		var collection = await Client(Server(answer: answer)).GetCollectionAsync("c");
		Assert.That((collection.Dimension, collection.Version, collection.LogPosition), Is.EqualTo((dimension, version, logPosition)));
	}

	static JsonElement Create(FakeServer server)
		=> server.Requests.Single(x => x.Method == "POST" && x.Path.EndsWith("/collections")).Body;

	static FakeServer Server(string version = "1.5.9", string answer = Created)
		=> new(r => r.Path.EndsWith("/version") ? (HttpStatusCode.OK, $"\"{version}\"") : (HttpStatusCode.OK, answer));

	static ChromaClient Client(HttpMessageHandler handler)
		=> new(new ChromaConfigurationOptions("http://localhost:8000"), new HttpClient(handler));

	sealed record Request(string Method, string Path, JsonElement Body);

	sealed class FakeServer(Func<Request, (HttpStatusCode Status, string Body)> answer) : HttpMessageHandler
	{
		public List<Request> Requests { get; } = [];

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var text = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
			var recorded = new Request(request.Method.Method, request.RequestUri!.AbsolutePath, text is { Length: > 0 } ? JsonDocument.Parse(text).RootElement.Clone() : default);
			Requests.Add(recorded);
			var (status, response) = answer(recorded);
			return new HttpResponseMessage(status) { Content = new StringContent(response) };
		}
	}
}
