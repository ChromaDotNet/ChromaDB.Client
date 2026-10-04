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

	// On Chroma Cloud the tenant and the database of the API key; a single server always answers with the default ones.
	[Test]
	public async Task TenantAndDatabaseFromIdentity()
	{
		Assume.That(ApiVersion, Is.EqualTo(ChromaApiVersion.V2), "The v1 API has no auth/identity.");
		var options = new ChromaConfigurationOptions(uri: BaseConfigurationOptions.Uri.ToString()).WithApiVersion(ApiVersion);
		options = BaseConfigurationOptions.ChromaToken is { } token ? options.WithChromaToken(token) : options;
		var client = await new ChromaClient(options, HttpClient).WithTenantAndDatabaseFromIdentity();
		Assert.That(client.Options.Tenant, Is.EqualTo(RunningServer ? BaseConfigurationOptions.Tenant ?? "default_tenant" : "default_tenant"));
		Assert.That(client.Options.Database, Is.EqualTo(RunningServer ? BaseConfigurationOptions.Database ?? "default_database" : "default_database"));
		Assert.That(await client.ListCollections(), Is.Not.Null);
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
