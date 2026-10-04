using System.Net;
using ChromaDB.Client.Models;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

// The operations added to cover the v2 API, against each version of Chroma, and against Chroma Cloud with CHROMA_TEST_URI.
[TestFixture]
public class ApiV2CompleteTests : ChromaTestsBase
{
	[Test]
	public async Task Healthcheck()
	{
		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		if (!HealthcheckSupported)
		{
			await Assert.ThatAsync(() => client.Healthcheck(), Throws.InstanceOf<ChromaException>().With.Property(nameof(ChromaException.StatusCode)).EqualTo(HttpStatusCode.NotFound));
			return;
		}
		var result = await client.Healthcheck();
		Assert.That(result.IsExecutorReady, Is.True);
	}

	[Test]
	public async Task RegexAndNotRegex()
	{
		var collection = await Init();
		if (!RegexSupported)
		{
			await Assert.ThatAsync(() => collection.Get(whereDocument: ChromaWhereDocumentOperator.Regex("^apple")), Throws.InstanceOf<ChromaException>());
			return;
		}
		Assert.That((await collection.Get(whereDocument: ChromaWhereDocumentOperator.Regex("^apple"))).Select(x => x.Id), Is.EquivalentTo(new[] { "a", "d" }));
		Assert.That((await collection.Get(whereDocument: ChromaWhereDocumentOperator.NotRegex("^apple"))).Select(x => x.Id), Is.EquivalentTo(new[] { "b", "c" }));
	}

	// On the servers that ignore the limit, nothing is deleted: without the check they would delete both apples.
	[Test]
	public async Task DeleteWithLimit()
	{
		var collection = await Init();
		var delete = new ChromaDelete { WhereDocument = ChromaWhereDocumentOperator.Contains("apple"), Limit = 1 };
		if (!DeleteLimitSupported)
		{
			await Assert.ThatAsync(() => collection.Delete(delete), Throws.InstanceOf<ChromaException>().With.Message.Contains("Chroma 1.5.3"));
			Assert.That(await collection.Count(), Is.EqualTo(4));
			return;
		}
		Assert.That(await collection.Delete(delete), Is.EqualTo(1));
		Assert.That(await collection.Count(), Is.EqualTo(3));
	}

	[Test]
	public async Task DeleteByFilterOnly()
	{
		var collection = await Init();
		var deleted = await collection.Delete(new ChromaDelete { WhereDocument = ChromaWhereDocumentOperator.Contains("apple") });
		Assert.That(deleted, DeleteLimitSupported ? Is.EqualTo(2) : Is.Null);
		Assert.That((await collection.Get()).Select(x => x.Id), Is.EquivalentTo(new[] { "b", "c" }));
	}

	// A single server indexes at once; Chroma Cloud may leave out of IndexOnly the records not indexed yet.
	[Test]
	public async Task CountAtAReadLevel()
	{
		var collection = await Init();
		Assert.That(await collection.Count(ChromaReadLevel.IndexAndWal), Is.EqualTo(4));
		Assert.That(await collection.Count(ChromaReadLevel.IndexOnly), ChromaCloud ? Is.InRange(0, 4) : Is.EqualTo(4));
	}

	// A single server has an HNSW index, Chroma Cloud a SPANN one.
	[Test]
	public async Task ModifyConfiguration()
	{
		var collection = await Init();
		var update = ChromaCloud
			? new ChromaCollectionConfigurationUpdate { Spann = new() { SearchNprobe = 32 } }
			: new ChromaCollectionConfigurationUpdate { Hnsw = new() { EfSearch = 200 } };
		if (!NewConfigurationApplied)
		{
			await Assert.ThatAsync(() => collection.ModifyConfiguration(update), Throws.InstanceOf<ChromaException>().With.Message.Contains("Chroma 1.0.6"));
			return;
		}
		await collection.ModifyConfiguration(update);
		var configuration = (await new ChromaClient(BaseConfigurationOptions, HttpClient).GetCollection(collection.Collection.Name)).ConfigurationJson!.Value;
		Assert.That(ChromaCloud
			? configuration.GetProperty("spann").GetProperty("search_nprobe").GetInt32()
			: configuration.GetProperty("hnsw").GetProperty("ef_search").GetInt32(), Is.EqualTo(ChromaCloud ? 32 : 200));
	}

	// Fork, its count and the indexing status exist only on Chroma Cloud: a single server answers with an error.
	[Test]
	public async Task ForkAndIndexingStatus()
	{
		var collection = await Init();
		if (!ChromaCloud)
		{
			await Assert.ThatAsync(() => collection.Fork($"collection{Random.Shared.Next()}"), Throws.InstanceOf<ChromaException>());
			await Assert.ThatAsync(() => collection.ForkCount(), Throws.InstanceOf<ChromaException>());
			await Assert.ThatAsync(() => collection.GetIndexingStatus(), Throws.InstanceOf<ChromaException>());
			return;
		}
		var fork = await collection.Fork($"collection{Random.Shared.Next()}");
		Assert.That(await new ChromaClient(BaseConfigurationOptions, HttpClient).GetCollectionClient(fork).Count(), Is.EqualTo(4));
		Assert.That(await collection.ForkCount(), Is.EqualTo(1));
		Assert.That((await collection.GetIndexingStatus()).TotalOps, Is.GreaterThanOrEqualTo(4));
	}

	// On Chroma Cloud the tenant and the database of the API key; a single server, also one already running, always answers
	// with the default ones.
	[Test]
	public async Task TenantAndDatabaseFromIdentity()
	{
		Assume.That(ApiVersion, Is.EqualTo(ChromaApiVersion.V2), "The v1 API has no auth/identity.");
		var options = new ChromaConfigurationOptions(uri: BaseConfigurationOptions.Uri.ToString()).WithApiVersion(ApiVersion);
		options = BaseConfigurationOptions.ChromaToken is { } token ? options.WithChromaToken(token) : options;
		var client = await new ChromaClient(options, HttpClient).WithTenantAndDatabaseFromIdentity();
		Assert.That(client.Options.Tenant, Is.EqualTo(ChromaCloud ? BaseConfigurationOptions.Tenant ?? "default_tenant" : "default_tenant"));
		Assert.That(client.Options.Database, Is.EqualTo(ChromaCloud ? BaseConfigurationOptions.Database ?? "default_database" : "default_database"));
		Assert.That(await client.ListCollections(), Is.Not.Null);
	}

	// The Search API exists only on Chroma Cloud: a single server answers with an error (501 from Chroma 1.x).
	[Test]
	public async Task Search()
	{
		var collection = await Init();
		var search = new ChromaSearch
		{
			WhereDocument = ChromaWhereDocumentOperator.Contains("apple"),
			Rank = ChromaRank.Knn(new([1f, 0f])),
			Limit = 3,
			Select = [ChromaSearchKeys.Document, ChromaSearchKeys.Score],
		};
		if (!ChromaCloud)
		{
			await Assert.ThatAsync(() => collection.Search(search), Throws.InstanceOf<ChromaException>());
			return;
		}
		var apples = await collection.Search(search);
		Assert.That(apples.Select(x => (x.Id, x.Document, x.Score)), Is.EqualTo(new[] { ("a", "apple pie", (float?)0f), ("d", "apple juice", (float?)0.5f) }));
		var rrf = await collection.Search(new ChromaSearch
		{
			Rank = ChromaRank.Rrf([ChromaRank.Knn(new([1f, 0f]), returnRank: true), ChromaRank.Knn(new([0f, 1f]), returnRank: true)]),
			Limit = 4,
			Select = [ChromaSearchKeys.Score],
		});
		Assert.That(rrf.Select(x => x.Score), Is.Ordered.Ascending);
		Assert.That(rrf, Has.Count.EqualTo(4));
		var two = await collection.Search([new ChromaSearch { Ids = ["b", "c"], Select = [ChromaSearchKeys.Document] }, new ChromaSearch { Rank = ChromaRank.Knn(new([0f, 1f])), Limit = 1 }]);
		Assert.That(two[0].Select(x => x.Document), Is.EquivalentTo(new[] { "banana split", "cherry tart" }));
		Assert.That(two[1].Single().Id, Is.EqualTo("b"));
	}

	// Sparse vector indexes and sparse vectors in metadata exist only on Chroma Cloud. A single server rejects the schema from Chroma 1.3.0,
	// and the earlier ones create the collection without it: then the client deletes it. Sparse vectors in metadata fail on a single
	// server; Chroma 0.x would drop them, so the client rejects them first.
	[Test]
	public async Task SchemaAndSparseVectors()
	{
		var client = new ChromaClient(BaseConfigurationOptions.WithMetadataValues(ChromaMetadataValues.Exact), HttpClient);
		var name = $"collection{Random.Shared.Next()}";
		var definition = new ChromaCollectionDefinition(name)
		{
			Schema = new ChromaCollectionSchema().WithSparseVectorIndex("doc_bm25", ChromaSearchKeys.Document, bm25: true, ChromaEmbeddingFunctionReference.ChromaBm25()),
		};
		var records = new ChromaRecords(["a", "b"])
		{
			Embeddings = [new([1f, 0f]), new([0f, 1f])],
			Documents = ["apple pie", "banana split"],
			Metadatas = [new() { ["doc_bm25"] = new ChromaSparseVector([1, 5], [0.5f, 0.7f]) }, new() { ["doc_bm25"] = new ChromaSparseVector([2], [0.9f]) }],
		};
		if (!ChromaCloud)
		{
			await Assert.ThatAsync(() => client.CreateCollection(definition), Throws.InstanceOf<ChromaException>());
			Assert.That(await client.CollectionExists(name), Is.False);
			var plain = client.GetCollectionClient(await client.CreateCollection($"collection{Random.Shared.Next()}"));
			await Assert.ThatAsync(() => plain.Add(records), Throws.InstanceOf<ChromaException>());
			return;
		}
		var collection = await client.CreateCollection(definition);
		var index = collection.SparseVectorIndexes.Single();
		Assert.That((index.Key, index.SourceKey, index.Bm25, index.EmbeddingFunction), Is.EqualTo(("doc_bm25", "#document", true, "chroma_bm25")));
		var collectionClient = client.GetCollectionClient(collection);
		await collectionClient.Add(records);
		var stored = (ChromaSparseVector)(await collectionClient.Get("a", include: ChromaGetInclude.Metadatas))!.Metadata!["doc_bm25"];
		Assert.That((stored.Indices, stored.Values), Is.EqualTo(((IReadOnlyList<int>)[1, 5], (IReadOnlyList<float>)[0.5f, 0.7f])));
		var found = await collectionClient.Search(new ChromaSearch { Rank = ChromaRank.SparseKnn(new ChromaSparseVector([1, 5], [1f, 1f]), "doc_bm25"), Limit = 1 });
		Assert.That(found.Single().Id, Is.EqualTo("a"));
	}

	async Task<ChromaCollectionClient> Init()
	{
		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		var collection = client.GetCollectionClient(await client.CreateCollection($"collection{Random.Shared.Next()}"));
		await collection.Add(["a", "b", "c", "d"], embeddings: [new([1f, 0f]), new([0f, 1f]), new([1f, 1f]), new([0.5f, 0.5f])],
			documents: ["apple pie", "banana split", "cherry tart", "apple juice"]);
		return collection;
	}
}
