using System.Text.Json;
using ChromaDB.Client.Models;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

// The settings of a new collection on the server: applied where the server applies them, an error where it would ignore them.
[TestFixture]
public class CollectionSettingsServerTests : ChromaTestsBase
{
	static readonly ChromaEmbeddingFunctionReference OpenAi = ChromaEmbeddingFunctionReference.Known("openai", new Dictionary<string, object> { ["model_name"] = "text-embedding-3-small", ["api_key_env_var"] = "OPENAI_API_KEY" });

	// Chroma 1.0.6 and later report the configuration; the earlier versions take the "hnsw:" metadata without telling.
	[Test]
	public async Task HnswSettings()
	{
		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		var name = $"collection{Random.Shared.Next()}";
		var definition = new ChromaCollectionDefinition(name)
		{
			Configuration = new() { Space = ChromaSpace.Cosine, Hnsw = new() { EfConstruction = 150, EfSearch = 120, MaxNeighbors = 20, ResizeFactor = 1.5, SyncThreshold = 2000 } },
		};
		if (ChromaCloud)
		{
			await Assert.ThatAsync(() => client.CreateCollectionAsync(definition), Throws.InstanceOf<ChromaException>().With.Message.Contains("SPANN"));
			Assert.That(await client.CollectionExistsAsync(name), Is.False);
			return;
		}
		var collection = await client.CreateCollectionAsync(definition);
		Assert.That(collection.Space, Is.EqualTo(ChromaSpace.Cosine));
		if (ConfigurationSpaceReported)
		{
			var hnsw = collection.ConfigurationJson!.Value.GetProperty("hnsw");
			Assert.That((hnsw.GetProperty("ef_construction").GetInt32(), hnsw.GetProperty("ef_search").GetInt32(), hnsw.GetProperty("max_neighbors").GetInt32(),
				hnsw.GetProperty("resize_factor").GetDouble(), hnsw.GetProperty("sync_threshold").GetInt32()), Is.EqualTo((150, 120, 20, 1.5, 2000)));
		}
	}

	// Chroma rejects 100.0 for the HNSW settings that are integers: a whole double goes as an integer.
	[Test]
	public async Task WholeDoublesInTheHnswMetadata()
	{
		if (ChromaCloud)
		{
			Assert.Ignore("Chroma Cloud has no HNSW index.");
		}
		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		var collection = await client.CreateCollectionAsync(new ChromaCollectionDefinition($"collection{Random.Shared.Next()}")
		{
			Metadata = new Dictionary<string, object> { ["hnsw:construction_ef"] = 100.0, ["hnsw:M"] = 16.0, ["hnsw:resize_factor"] = 2.0 },
		});
		Assert.That(Convert.ToInt64(collection.Metadata!["hnsw:construction_ef"]), Is.EqualTo(100));
		if (ConfigurationSpaceReported)
		{
			var hnsw = collection.ConfigurationJson!.Value.GetProperty("hnsw");
			Assert.That((hnsw.GetProperty("ef_construction").GetInt32(), hnsw.GetProperty("max_neighbors").GetInt32(), hnsw.GetProperty("resize_factor").GetDouble()), Is.EqualTo((100, 16, 2.0)));
		}
	}

	[Test]
	public async Task SpannSettings()
	{
		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		var name = $"collection{Random.Shared.Next()}";
		var definition = new ChromaCollectionDefinition(name) { Configuration = new() { Space = ChromaSpace.Cosine, Spann = new() { SearchNprobe = 32, WriteNprobe = 16 } } };
		if (ChromaCloud)
		{
			var spann = (await client.CreateCollectionAsync(definition)).ConfigurationJson!.Value.GetProperty("spann");
			Assert.That((spann.GetProperty("space").GetString(), spann.GetProperty("search_nprobe").GetInt32(), spann.GetProperty("write_nprobe").GetInt32()), Is.EqualTo(("cosine", 32, 16)));
			return;
		}
		Assume.That(IsChroma0 || NewConfigurationApplied, "Chroma 1.0.0 to 1.0.5 report no configuration: the client cannot tell that they ignore the SPANN settings.");
		await Assert.ThatAsync(() => client.CreateCollectionAsync(definition), Throws.InstanceOf<ChromaException>().With.Message.Contains(IsChroma0 ? "1.0.0" : "HNSW"));
		Assert.That(await client.CollectionExistsAsync(name), Is.False);
	}

	[Test]
	public async Task EmbeddingFunction()
	{
		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		var definition = new ChromaCollectionDefinition($"collection{Random.Shared.Next()}") { Configuration = new() { Space = ChromaSpace.InnerProduct, EmbeddingFunction = OpenAi } };
		if (IsChroma0)
		{
			await Assert.ThatAsync(() => client.CreateCollectionAsync(definition), Throws.InstanceOf<ChromaException>().With.Message.Contains("1.0.0"));
			return;
		}
		var collection = await client.CreateCollectionAsync(definition);
		Assert.That(collection.Space, Is.EqualTo(ChromaSpace.InnerProduct));
		if (ChromaCloud || ConfigurationSpaceReported)
		{
			Assert.That(collection.ConfigurationJson!.Value.GetProperty("embedding_function").GetProperty("name").GetString(), Is.EqualTo("openai"));
		}
	}

	[Test]
	public async Task ModifyTheEmbeddingFunction()
	{
		Assume.That(ChromaCloud || NewConfigurationApplied, "Chroma 1.0.6 and later apply a new configuration.");
		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		var collection = await client.CreateCollectionAsync($"collection{Random.Shared.Next()}");
		await client.GetCollectionClient(collection).ModifyConfigurationAsync(new ChromaCollectionConfigurationUpdate { EmbeddingFunction = OpenAi });
		var configuration = (await client.GetCollectionAsync(collection.Name)).ConfigurationJson!.Value;
		Assert.That(configuration.GetProperty("embedding_function").GetProperty("name").GetString(), Is.EqualTo("openai"));
	}

	// A BM25 index added with ifSupported: Chroma Cloud creates the collection with it, every other server without it, as it rejects
	// sparse vector indexes.
	[Test]
	public async Task Bm25IndexIfSupported()
	{
		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		var created = await client.CreateCollectionAsync(new ChromaCollectionDefinition($"collection{Random.Shared.Next()}")
		{
			Configuration = new() { Space = ChromaSpace.Cosine },
			Schema = new ChromaCollectionSchema().WithBm25Index(ChromaSearchKeys.Document, ifSupported: true),
		});
		Assert.That(created.FindBm25Index(ChromaSearchKeys.Document), ChromaCloud ? Is.Not.Null : Is.Null);
		var collection = client.GetCollectionClient(created);
		await collection.AddAsync(new ChromaRecords(["a"]) { Embeddings = [new([1f, 0f])], Documents = ["apple pie"] });
		Assert.That((await collection.GetAsync(include: ChromaGetInclude.Documents)).Single().Document, Is.EqualTo("apple pie"));
	}

	// Chroma 1.3.0 and later apply the indexes of a schema.
	[Test]
	public async Task IndexesOfTheSchema()
	{
		Assume.That(SchemaApplied, "Chroma 1.3.0 and later apply a schema.");
		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		var schema = new ChromaCollectionSchema()
			.WithoutIndex(ChromaSchemaIndex.StringInverted, "title")
			.WithoutIndex(ChromaSchemaIndex.IntInverted)
			.WithoutIndex(ChromaSchemaIndex.FullTextSearch);
		var read = (await client.CreateCollectionAsync(new ChromaCollectionDefinition($"collection{Random.Shared.Next()}") { Schema = schema })).SchemaJson!.Value;
		Assert.That(Enabled(read.GetProperty("keys").GetProperty("title").GetProperty("string").GetProperty("string_inverted_index")), Is.False);
		Assert.That(Enabled(read.GetProperty("defaults").GetProperty("int").GetProperty("int_inverted_index")), Is.False);
		Assert.That(Enabled(read.GetProperty("keys").GetProperty("#document").GetProperty("string").GetProperty("fts_index")), Is.False);
		Assert.That(Enabled(read.GetProperty("defaults").GetProperty("float").GetProperty("float_inverted_index")), Is.True);
	}

	// A filter on a key without its index is rejected; the full-text search index turned off is kept by Chroma 1.3.0 to 1.5.0.
	[Test]
	public async Task FiltersWithoutTheirIndex()
	{
		Assume.That(SchemaApplied, "Chroma 1.3.0 and later apply a schema.");
		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		var schema = new ChromaCollectionSchema().WithoutIndex(ChromaSchemaIndex.StringInverted, "title").WithoutIndex(ChromaSchemaIndex.IntInverted).WithoutIndex(ChromaSchemaIndex.FullTextSearch);
		var collection = client.GetCollectionClient(await client.CreateCollectionAsync(new ChromaCollectionDefinition($"collection{Random.Shared.Next()}") { Schema = schema }));
		await collection.AddAsync(new ChromaRecords(["a", "b"])
		{
			Embeddings = [new([1f, 0f]), new([0f, 1f])],
			Documents = ["apple pie", "banana split"],
			Metadatas = [new Dictionary<string, object> { ["title"] = "x", ["n"] = 1, ["other"] = "x" }, new Dictionary<string, object> { ["title"] = "y", ["n"] = 2, ["other"] = "y" }],
		});
		Assert.That((await collection.GetAsync(where: ChromaWhereOperator.Equal("other", "x"))).Select(x => x.Id), Is.EqualTo(new[] { "a" }));
		await Assert.ThatAsync(() => collection.GetAsync(where: ChromaWhereOperator.Equal("title", "x")), Throws.InstanceOf<ChromaException>().With.Message.Contains("indexing is disabled"));
		await Assert.ThatAsync(() => collection.GetAsync(where: ChromaWhereOperator.Equal("n", 1)), Throws.InstanceOf<ChromaException>().With.Message.Contains("indexing is disabled"));
		if (FullTextSearchOffApplied)
		{
			await Assert.ThatAsync(() => collection.GetAsync(whereDocument: ChromaWhereDocumentOperator.Contains("apple")), Throws.InstanceOf<ChromaException>().With.Message.Contains("FTS indexing is disabled"));
		}
		else
		{
			Assert.That((await collection.GetAsync(whereDocument: ChromaWhereDocumentOperator.Contains("apple"))).Select(x => x.Id), Is.EqualTo(new[] { "a" }));
		}
	}

	// Only Chroma Cloud has sparse vector indexes, SPANN and customer-managed keys. A key of Google Cloud KMS that does not exist is kept.
	[Test]
	public async Task SettingsOfChromaCloud()
	{
		Assume.That(ChromaCloud, Is.True, "Only Chroma Cloud has them.");
		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		const string key = "projects/p/locations/us/keyRings/r/cryptoKeys/k";
		var collection = await client.CreateCollectionAsync(new ChromaCollectionDefinition($"collection{Random.Shared.Next()}")
		{
			Configuration = new() { Space = ChromaSpace.Cosine, Spann = new() { SearchNprobe = 32, NreplicaCount = 4, SearchRngEpsilon = 8, NumSamplesKmeans = 500 } },
			Schema = new ChromaCollectionSchema().WithSparseVectorIndex("doc_bm25", bm25: true).WithGcpCmek(key),
		});
		Assert.That(collection.SparseVectorIndexes.Single().Algorithm, Is.EqualTo(ChromaSparseIndexAlgorithm.Wand));
		var schema = collection.SchemaJson!.Value;
		Assert.That(schema.GetProperty("cmek").GetProperty("gcp").GetString(), Is.EqualTo(key));
		var spann = schema.GetProperty("keys").GetProperty("#embedding").GetProperty("float_list").GetProperty("vector_index").GetProperty("config").GetProperty("spann");
		Assert.That((spann.GetProperty("search_nprobe").GetInt32(), spann.GetProperty("nreplica_count").GetInt32(), spann.GetProperty("search_rng_epsilon").GetDouble(), spann.GetProperty("num_samples_kmeans").GetInt32()),
			Is.EqualTo((32, 4, 8.0, 500)));
		Assert.That(collection.Space, Is.EqualTo(ChromaSpace.Cosine));
	}

	// MaxScore is only for the tenants of Chroma Cloud that have it: on the others the test is inconclusive.
	[Test]
	public async Task MaxScoreSparseIndex()
	{
		Assume.That(ChromaCloud, Is.True, "Only Chroma Cloud has sparse vector indexes.");
		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		var definition = new ChromaCollectionDefinition($"collection{Random.Shared.Next()}") { Schema = new ChromaCollectionSchema().WithSparseVectorIndex("doc_bm25", ChromaSparseIndexAlgorithm.MaxScore, bm25: true) };
		ChromaCollection collection;
		try
		{
			collection = await client.CreateCollectionAsync(definition);
		}
		catch (ChromaException ex) when (ex.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Forbidden)
		{
			Assert.Inconclusive($"The tenant does not have MaxScore: {ex.Message}");
			return;
		}
		Assert.That(collection.SparseVectorIndexes.Single().Algorithm, Is.EqualTo(ChromaSparseIndexAlgorithm.MaxScore));
	}

	// Chroma 0.5.1 and later send the dimension, set by the first write.
	[Test]
	public async Task Dimension()
	{
		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		var collection = await client.CreateCollectionAsync($"collection{Random.Shared.Next()}");
		Assert.That(collection.Dimension, Is.Null);
		await client.GetCollectionClient(collection).AddAsync(["a"], embeddings: [new([1f, 0f, 0.5f])]);
		var read = await client.GetCollectionAsync(collection.Name);
		Assert.That(read.Dimension, CollectionDimensionReported ? Is.EqualTo(3) : Is.Null);
		Assert.That(read.Version, CollectionDimensionReported ? Is.Not.Null : Is.Null);
	}

	static bool Enabled(JsonElement index) => index.GetProperty("enabled").GetBoolean();
}
