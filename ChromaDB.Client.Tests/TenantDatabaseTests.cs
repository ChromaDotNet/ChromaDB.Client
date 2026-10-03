using NUnit.Framework;

namespace ChromaDB.Client.Tests;

[TestFixture]
public class TenantDatabaseTests : ChromaTestsBase
{
	[SetUp]
	public void SetUp()
		=> Assume.That(TenantsSupported, Is.True, "Chroma 0.4.14 and earlier have no tenants and databases.");

	[Test]
	public async Task CreateTenant()
	{
		var name = $"tenant{Random.Shared.Next()}";

		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		await client.CreateTenant(name);
		var result = await client.GetTenant(name);
		Assert.That(result.Name, Is.EqualTo(name));
	}

	[Test]
	public async Task CreateTenantAlreadyExists()
	{
		var name = $"tenant{Random.Shared.Next()}";

		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		await client.CreateTenant(name);
		await Assert.ThatAsync(() => client.CreateTenant(name), Throws.InstanceOf<ChromaException>().With.Message.Contains("already exists"));
	}

	[Test]
	public async Task GetTenantNotExists()
	{
		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		await Assert.ThatAsync(() => client.GetTenant($"tenant{Random.Shared.Next()}"), Throws.InstanceOf<ChromaException>().With.Message.Contains("not found"));
	}

	[Test]
	public async Task CreateDatabase()
	{
		var tenant = $"tenant{Random.Shared.Next()}";
		var name = $"database{Random.Shared.Next()}";

		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		await client.CreateTenant(tenant);
		await client.CreateDatabase(name, tenant: tenant);
		var result = await client.GetDatabase(name, tenant: tenant);
		Assert.That(result.Name, Is.EqualTo(name));
		Assert.That(result.Tenant, Is.EqualTo(tenant));
	}

	[Test]
	public async Task CreateDatabaseInTenantOfOptions()
	{
		var name = $"database{Random.Shared.Next()}";

		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		await client.CreateDatabase(name);
		var result = await client.GetDatabase(name);
		Assert.That(result.Name, Is.EqualTo(name));
		Assert.That(result.Tenant, Is.EqualTo(BaseConfigurationOptions.Tenant ?? "default_tenant"));
	}

	[Test]
	public async Task CreateDatabaseAlreadyExists()
	{
		var name = $"database{Random.Shared.Next()}";

		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		await client.CreateDatabase(name);
		await Assert.ThatAsync(() => client.CreateDatabase(name), Throws.InstanceOf<ChromaException>().With.Message.Contains("already exists"));
	}

	[Test]
	public async Task GetDatabaseNotExists()
	{
		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		await Assert.ThatAsync(() => client.GetDatabase($"database{Random.Shared.Next()}"), Throws.InstanceOf<ChromaException>().With.Message.Contains("not found"));
	}

	[Test]
	public async Task CollectionInTenantAndDatabase()
	{
		Assume.That(RecordsInOtherTenantsSupported, Is.True, "Chroma 0.4.15 does not add records to the collections of other tenants and databases.");
		var tenant = $"tenant{Random.Shared.Next()}";
		var database = $"database{Random.Shared.Next()}";
		var name = $"collection{Random.Shared.Next()}";

		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		await client.CreateTenant(tenant);
		await client.CreateDatabase(database, tenant: tenant);
		var options = BaseConfigurationOptions.WithTenant(tenant).WithDatabase(database);
		var collection = await new ChromaClient(options, HttpClient).CreateCollection(name);
		Assert.That(collection.Tenant, Is.EqualTo(tenant));
		Assert.That(collection.Database, Is.EqualTo(database));

		var collectionClient = new ChromaCollectionClient(collection, options, HttpClient);
		await collectionClient.Add(["a", "b"], embeddings: [new([1f, 0f]), new([0f, 1f])], documents: ["first", "second"]);
		Assert.That((await collectionClient.Get("a"))?.Document, Is.EqualTo("first"));
		Assert.That((await collectionClient.Query(new ReadOnlyMemory<float>([1f, 0.1f]), nResults: 1)).Single().Id, Is.EqualTo("a"));
		await collectionClient.Delete(["a"]);
		Assert.That(await collectionClient.Count(), Is.EqualTo(1));

		Assert.That((await client.ListCollections()).Select(x => x.Name), Does.Not.Contain(name));
		Assert.That((await client.ListCollections(tenant: tenant, database: database)).Select(x => x.Name), Contains.Item(name));
	}
}
