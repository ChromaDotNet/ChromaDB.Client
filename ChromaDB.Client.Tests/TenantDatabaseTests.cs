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
		Assume.That(TenantCreationTested, Is.True, "A server already running may not let the tests create tenants.");
		var name = $"tenant{Random.Shared.Next()}";

		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		await client.CreateTenant(name);
		var result = await client.GetTenant(name);
		Assert.That(result.Name, Is.EqualTo(name));
	}

	[Test]
	public async Task CreateTenantAlreadyExists()
	{
		Assume.That(TenantCreationTested, Is.True, "A server already running may not let the tests create tenants.");
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
		Assume.That(TenantCreationTested, Is.True, "A server already running may not let the tests create tenants.");
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
	public async Task ListDatabases()
	{
		Assume.That(TenantCreationTested, Is.True, "A server already running may not let the tests create tenants.");
		Assume.That(DatabaseListingSupported, Is.True, "Only the v2 API of Chroma 0.6.3 and later lists databases.");
		var tenant = $"tenant{Random.Shared.Next()}";

		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		await client.CreateTenant(tenant);
		foreach (var name in new[] { "db_b", "db_a", "db_c" })
		{
			await client.CreateDatabase(name, tenant: tenant);
		}
		var result = await client.ListDatabases(tenant: tenant);
		Assert.That(result.Select(x => x.Name), Is.EqualTo(new[] { "db_a", "db_b", "db_c" }));
		Assert.That(result.Select(x => x.Tenant), Is.All.EqualTo(tenant));
	}

	[Test]
	public async Task ListDatabasesInTenantOfOptions()
	{
		Assume.That(DatabaseListingSupported, Is.True, "Only the v2 API of Chroma 0.6.3 and later lists databases.");
		var name = $"database{Random.Shared.Next()}";

		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		await client.CreateDatabase(name);
		var result = await client.ListDatabases();
		Assert.That(result.Select(x => x.Name), Contains.Item(name));
		Assert.That(result.Select(x => x.Tenant), Is.All.EqualTo(BaseConfigurationOptions.Tenant ?? "default_tenant"));
	}

	[Test]
	public async Task ListDatabasesPage()
	{
		Assume.That(TenantCreationTested, Is.True, "A server already running may not let the tests create tenants.");
		Assume.That(DatabaseListingSupported, Is.True, "Only the v2 API of Chroma 0.6.3 and later lists databases.");
		var tenant = $"tenant{Random.Shared.Next()}";

		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		await client.CreateTenant(tenant);
		foreach (var name in new[] { "db_a", "db_b", "db_c" })
		{
			await client.CreateDatabase(name, tenant: tenant);
		}
		Assert.That((await client.ListDatabases(limit: 2, tenant: tenant)).Select(x => x.Name), Is.EqualTo(new[] { "db_a", "db_b" }));
		Assert.That((await client.ListDatabases(limit: 2, offset: 2, tenant: tenant)).Select(x => x.Name), Is.EqualTo(new[] { "db_c" }));
	}

	[Test]
	public async Task DeleteDatabase()
	{
		Assume.That(TenantCreationTested, Is.True, "A server already running may not let the tests create tenants.");
		Assume.That(DatabaseListingSupported, Is.True, "Only the v2 API of Chroma 0.6.3 and later deletes databases.");
		var tenant = $"tenant{Random.Shared.Next()}";

		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		await client.CreateTenant(tenant);
		await client.CreateDatabase("db_a", tenant: tenant);
		await client.CreateDatabase("db_b", tenant: tenant);
		await client.DeleteDatabase("db_a", tenant: tenant);
		Assert.That((await client.ListDatabases(tenant: tenant)).Select(x => x.Name), Is.EqualTo(new[] { "db_b" }));
		await Assert.ThatAsync(() => client.GetDatabase("db_a", tenant: tenant), Throws.InstanceOf<ChromaException>().With.Message.Contains("not found"));
	}

	[Test]
	public async Task DeleteDatabaseNotExists()
	{
		Assume.That(DatabaseListingSupported, Is.True, "Only the v2 API of Chroma 0.6.3 and later deletes databases.");

		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		await Assert.ThatAsync(() => client.DeleteDatabase($"database{Random.Shared.Next()}"), Throws.InstanceOf<ChromaException>().With.Message.Contains("not found"));
	}

	// The older servers answer 405 Method Not Allowed: the client reports it with a ChromaException.
	[Test]
	public async Task ListAndDeleteDatabasesNotSupported()
	{
		Assume.That(DatabaseListingSupported, Is.False, "This server lists and deletes databases.");
		var name = $"database{Random.Shared.Next()}";

		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		await client.CreateDatabase(name);
		await Assert.ThatAsync(() => client.ListDatabases(), Throws.InstanceOf<ChromaException>().With.Message.StartsWith("Method Not Allowed: GET "));
		await Assert.ThatAsync(() => client.DeleteDatabase(name), Throws.InstanceOf<ChromaException>().With.Message.StartsWith("Method Not Allowed: DELETE "));
		Assert.That((await client.GetDatabase(name)).Name, Is.EqualTo(name));
	}

	[Test]
	public async Task CollectionInTenantAndDatabase()
	{
		Assume.That(TenantCreationTested, Is.True, "A server already running may not let the tests create tenants.");
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
