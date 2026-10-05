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
		Assume.That(OtherTenantsAndDatabasesTested, Is.True, "A server already running may not let the tests create or look up other tenants and databases.");
		var name = $"tenant{Random.Shared.Next()}";

		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		await client.CreateTenantAsync(name);
		var result = await client.GetTenantAsync(name);
		Assert.That(result.Name, Is.EqualTo(name));
	}

	[Test]
	public async Task CreateTenantAlreadyExists()
	{
		Assume.That(OtherTenantsAndDatabasesTested, Is.True, "A server already running may not let the tests create or look up other tenants and databases.");
		var name = $"tenant{Random.Shared.Next()}";

		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		await client.CreateTenantAsync(name);
		await Assert.ThatAsync(() => client.CreateTenantAsync(name), Throws.InstanceOf<ChromaException>().With.Message.Contains("already exists"));
	}

	[Test]
	public async Task GetTenantNotExists()
	{
		Assume.That(OtherTenantsAndDatabasesTested, Is.True, "A server already running may not let the tests create or look up other tenants and databases.");
		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		await Assert.ThatAsync(() => client.GetTenantAsync($"tenant{Random.Shared.Next()}"), Throws.InstanceOf<ChromaException>().With.Message.Contains("not found"));
	}

	[Test]
	public async Task CreateDatabase()
	{
		Assume.That(OtherTenantsAndDatabasesTested, Is.True, "A server already running may not let the tests create or look up other tenants and databases.");
		var tenant = $"tenant{Random.Shared.Next()}";
		var name = $"database{Random.Shared.Next()}";

		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		await client.CreateTenantAsync(tenant);
		await client.CreateDatabaseAsync(name, tenant: tenant);
		var result = await client.GetDatabaseAsync(name, tenant: tenant);
		Assert.That(result.Name, Is.EqualTo(name));
		Assert.That(result.Tenant, Is.EqualTo(tenant));
	}

	[Test]
	public async Task CreateDatabaseInTenantOfOptions()
	{
		Assume.That(OtherTenantsAndDatabasesTested, Is.True, "A server already running may not let the tests create or look up other tenants and databases.");
		var name = $"database{Random.Shared.Next()}";

		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		await client.CreateDatabaseAsync(name);
		var result = await client.GetDatabaseAsync(name);
		Assert.That(result.Name, Is.EqualTo(name));
		Assert.That(result.Tenant, Is.EqualTo(BaseConfigurationOptions.Tenant ?? "default_tenant"));
	}

	[Test]
	public async Task CreateDatabaseAlreadyExists()
	{
		Assume.That(OtherTenantsAndDatabasesTested, Is.True, "A server already running may not let the tests create or look up other tenants and databases.");
		var name = $"database{Random.Shared.Next()}";

		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		await client.CreateDatabaseAsync(name);
		await Assert.ThatAsync(() => client.CreateDatabaseAsync(name), Throws.InstanceOf<ChromaException>().With.Message.Contains("already exists"));
	}

	[Test]
	public async Task GetDatabaseNotExists()
	{
		Assume.That(OtherTenantsAndDatabasesTested, Is.True, "A server already running may not let the tests create or look up other tenants and databases.");
		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		await Assert.ThatAsync(() => client.GetDatabaseAsync($"database{Random.Shared.Next()}"), Throws.InstanceOf<ChromaException>().With.Message.Contains("not found"));
	}

	[Test]
	public async Task ListDatabases()
	{
		Assume.That(OtherTenantsAndDatabasesTested, Is.True, "A server already running may not let the tests create or look up other tenants and databases.");
		Assume.That(DatabaseListingSupported, Is.True, "Only the v2 API of Chroma 0.6.3 and later lists databases.");
		var tenant = $"tenant{Random.Shared.Next()}";

		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		await client.CreateTenantAsync(tenant);
		foreach (var name in new[] { "db_b", "db_a", "db_c" })
		{
			await client.CreateDatabaseAsync(name, tenant: tenant);
		}
		var result = await client.ListDatabasesAsync(tenant: tenant);
		Assert.That(result.Select(x => x.Name), Is.EqualTo(new[] { "db_a", "db_b", "db_c" }));
		Assert.That(result.Select(x => x.Tenant), Is.All.EqualTo(tenant));
	}

	[Test]
	public async Task ListDatabasesInTenantOfOptions()
	{
		Assume.That(OtherTenantsAndDatabasesTested, Is.True, "A server already running may not let the tests create or look up other tenants and databases.");
		Assume.That(DatabaseListingSupported, Is.True, "Only the v2 API of Chroma 0.6.3 and later lists databases.");
		var name = $"database{Random.Shared.Next()}";

		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		await client.CreateDatabaseAsync(name);
		var result = await client.ListDatabasesAsync();
		Assert.That(result.Select(x => x.Name), Contains.Item(name));
		Assert.That(result.Select(x => x.Tenant), Is.All.EqualTo(BaseConfigurationOptions.Tenant ?? "default_tenant"));
	}

	[Test]
	public async Task ListDatabasesPage()
	{
		Assume.That(OtherTenantsAndDatabasesTested, Is.True, "A server already running may not let the tests create or look up other tenants and databases.");
		Assume.That(DatabaseListingSupported, Is.True, "Only the v2 API of Chroma 0.6.3 and later lists databases.");
		var tenant = $"tenant{Random.Shared.Next()}";

		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		await client.CreateTenantAsync(tenant);
		foreach (var name in new[] { "db_a", "db_b", "db_c" })
		{
			await client.CreateDatabaseAsync(name, tenant: tenant);
		}
		Assert.That((await client.ListDatabasesAsync(limit: 2, tenant: tenant)).Select(x => x.Name), Is.EqualTo(new[] { "db_a", "db_b" }));
		Assert.That((await client.ListDatabasesAsync(limit: 2, offset: 2, tenant: tenant)).Select(x => x.Name), Is.EqualTo(new[] { "db_c" }));
	}

	[Test]
	public async Task DeleteDatabase()
	{
		Assume.That(OtherTenantsAndDatabasesTested, Is.True, "A server already running may not let the tests create or look up other tenants and databases.");
		Assume.That(DatabaseListingSupported, Is.True, "Only the v2 API of Chroma 0.6.3 and later deletes databases.");
		var tenant = $"tenant{Random.Shared.Next()}";

		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		await client.CreateTenantAsync(tenant);
		await client.CreateDatabaseAsync("db_a", tenant: tenant);
		await client.CreateDatabaseAsync("db_b", tenant: tenant);
		await client.DeleteDatabaseAsync("db_a", tenant: tenant);
		Assert.That((await client.ListDatabasesAsync(tenant: tenant)).Select(x => x.Name), Is.EqualTo(new[] { "db_b" }));
		await Assert.ThatAsync(() => client.GetDatabaseAsync("db_a", tenant: tenant), Throws.InstanceOf<ChromaException>().With.Message.Contains("not found"));
	}

	[Test]
	public async Task DeleteDatabaseNotExists()
	{
		Assume.That(OtherTenantsAndDatabasesTested, Is.True, "A server already running may not let the tests create or look up other tenants and databases.");
		Assume.That(DatabaseListingSupported, Is.True, "Only the v2 API of Chroma 0.6.3 and later deletes databases.");

		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		await Assert.ThatAsync(() => client.DeleteDatabaseAsync($"database{Random.Shared.Next()}"), Throws.InstanceOf<ChromaException>().With.Message.Contains("not found"));
	}

	// The older servers answer 405 Method Not Allowed: the client reports it with a ChromaException.
	[Test]
	public async Task ListAndDeleteDatabasesNotSupported()
	{
		Assume.That(DatabaseListingSupported, Is.False, "This server lists and deletes databases.");
		var name = $"database{Random.Shared.Next()}";

		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		await client.CreateDatabaseAsync(name);
		await Assert.ThatAsync(() => client.ListDatabasesAsync(), Throws.InstanceOf<ChromaException>().With.Message.StartsWith("Method Not Allowed: GET "));
		await Assert.ThatAsync(() => client.DeleteDatabaseAsync(name), Throws.InstanceOf<ChromaException>().With.Message.StartsWith("Method Not Allowed: DELETE "));
		Assert.That((await client.GetDatabaseAsync(name)).Name, Is.EqualTo(name));
	}

	[Test]
	public async Task CollectionInTenantAndDatabase()
	{
		Assume.That(OtherTenantsAndDatabasesTested, Is.True, "A server already running may not let the tests create or look up other tenants and databases.");
		Assume.That(RecordsInOtherTenantsSupported, Is.True, "Chroma 0.4.15 does not add records to the collections of other tenants and databases.");
		var tenant = $"tenant{Random.Shared.Next()}";
		var database = $"database{Random.Shared.Next()}";
		var name = $"collection{Random.Shared.Next()}";

		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		await client.CreateTenantAsync(tenant);
		await client.CreateDatabaseAsync(database, tenant: tenant);
		var options = BaseConfigurationOptions.WithTenant(tenant).WithDatabase(database);
		var collection = await new ChromaClient(options, HttpClient).CreateCollectionAsync(name);
		Assert.That(collection.Tenant, Is.EqualTo(tenant));
		Assert.That(collection.Database, Is.EqualTo(database));

		var collectionClient = new ChromaCollectionClient(collection, options, HttpClient);
		await collectionClient.AddAsync(["a", "b"], embeddings: [new([1f, 0f]), new([0f, 1f])], documents: ["first", "second"]);
		Assert.That((await collectionClient.GetAsync("a"))?.Document, Is.EqualTo("first"));
		Assert.That((await collectionClient.QueryAsync(new ReadOnlyMemory<float>([1f, 0.1f]), nResults: 1)).Single().Id, Is.EqualTo("a"));
		await collectionClient.DeleteAsync(["a"]);
		Assert.That(await collectionClient.CountAsync(), Is.EqualTo(1));

		Assert.That((await client.ListCollectionsAsync()).Select(x => x.Name), Does.Not.Contain(name));
		Assert.That((await client.ListCollectionsAsync(tenant: tenant, database: database)).Select(x => x.Name), Contains.Item(name));
	}
}
