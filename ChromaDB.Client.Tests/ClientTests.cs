using NUnit.Framework;

namespace ChromaDB.Client.Tests;

[TestFixture]
public class ClientTests : ChromaTestsBase
{
	[Test]
	public async Task HeartbeatSimple()
	{
		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		var result = await client.Heartbeat();
		Assert.That(result.NanosecondHeartbeat, Is.GreaterThan(0));
	}

	[Test]
	public async Task GetVersionSimple()
	{
		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		var result = await client.GetVersion();
		Assert.That(result, Does.Match(@"\d+\.\d+"));
	}

	[Test]
	public async Task GetUserIdentitySimple()
	{
		Assume.That(ApiVersion, Is.EqualTo(ChromaApiVersion.V2), "The v1 API has no auth/identity.");
		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		var result = await client.GetUserIdentity();
		Assert.That(result.Tenant, Is.EqualTo("default_tenant"));
		Assert.That(result.Databases, Contains.Item("default_database"));
	}

	[Test]
	public async Task GetPreFlightChecksSimple()
	{
		Assume.That(PreFlightChecksSupported, Is.True, "Chroma 0.4.10 has no pre-flight-checks.");
		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		var result = await client.GetPreFlightChecks();
		Assert.That(result.MaxBatchSize, Is.GreaterThan(0));
		Assert.That(result.SupportsBase64Encoding, Base64EncodingReported ? Is.Not.Null : Is.Null);
	}

	[Test]
	public async Task SharedHttpClientIsNotModified()
	{
		using var httpClient = new HttpClient();
		var client = new ChromaClient(BaseConfigurationOptions.WithChromaToken("token"), httpClient);
		await client.Heartbeat();
		Assert.That(httpClient.BaseAddress, Is.Null);
		Assert.That(httpClient.DefaultRequestHeaders.Contains("X-Chroma-Token"), Is.False);
	}

	[Test]
	public async Task SharedHttpClientWithAnotherUri()
	{
		using var httpClient = new HttpClient();
		var client = new ChromaClient(BaseConfigurationOptions, httpClient);
		await client.Heartbeat();
		_ = new ChromaClient(BaseConfigurationOptions.WithUri("http://localhost:1/api/v2/"), httpClient);
		await Assert.ThatAsync(() => client.Heartbeat(), Throws.Nothing);
	}

	[Test]
	public async Task SharedHttpClientWithAnotherUriForCollection()
	{
		using var httpClient = new HttpClient();
		var client = new ChromaClient(BaseConfigurationOptions, httpClient);
		var collection = await client.CreateCollection($"collection{Random.Shared.Next()}");
		Assert.That(() => new ChromaCollectionClient(collection, BaseConfigurationOptions.WithUri("http://localhost:1/api/v2/"), httpClient), Throws.Nothing);
		await Assert.ThatAsync(() => new ChromaCollectionClient(collection, BaseConfigurationOptions, httpClient).Count(), Throws.Nothing);
	}

	[Test]
	public async Task UriWithHostAndPortOnly()
	{
		var options = BaseConfigurationOptions.WithUri(BaseConfigurationOptions.Uri.GetLeftPart(UriPartial.Authority));
		var client = new ChromaClient(options, HttpClient);
		var collection = await client.GetOrCreateCollection($"collection{Random.Shared.Next()}");
		var result = await new ChromaCollectionClient(collection, options, HttpClient).Count();
		Assert.That(result, Is.EqualTo(0));
	}

	[Test]
	[Ignore("Failing because of bug on Chroma's side.", Until = "2025-04-21")]
	public async Task GetCollectionSimple()
	{
		var name = $"collection{Random.Shared.Next()}";

		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		await client.CreateCollection(name);
		var result = await client.GetCollection(name);
		Assert.That(result, Is.Not.Null);
		Assert.That(result.Name, Is.EqualTo(name));
	}

	[Test]
	public async Task CollectionExists()
	{
		var name = $"collection{Random.Shared.Next()}";

		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		Assert.That(await client.CollectionExists(name), Is.False);
		await client.CreateCollection(name);
		Assert.That(await client.CollectionExists(name), Is.True);
		await client.DeleteCollection(name);
		Assert.That(await client.CollectionExists(name), Is.False);
	}

	[Test]
	public async Task GetCollectionById()
	{
		Assume.That(CollectionByIdSupported, Is.True, "Only the v2 API of Chroma 1.5.7 and later gets a collection by its id.");
		var name = $"collection{Random.Shared.Next()}";

		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		var collection = await client.CreateCollection(name, metadata: new() { ["key"] = "value" });
		var result = await client.GetCollectionById(collection.Id);
		Assert.That(result.Id, Is.EqualTo(collection.Id));
		Assert.That(result.Name, Is.EqualTo(name));
		Assert.That(result.Metadata?["key"], Is.EqualTo("value"));
		Assert.That(result.Tenant, Is.EqualTo(collection.Tenant));
		Assert.That(result.Database, Is.EqualTo(collection.Database));
	}

	[Test]
	public async Task GetCollectionByIdNotExists()
	{
		Assume.That(CollectionByIdSupported, Is.True, "Only the v2 API of Chroma 1.5.7 and later gets a collection by its id.");

		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		await Assert.ThatAsync(() => client.GetCollectionById(Guid.NewGuid()), Throws.InstanceOf<ChromaException>().With.Message.Contains("does not exist"));
	}

	// The id is looked up in the tenant and database of the request only.
	[Test]
	public async Task GetCollectionByIdInAnotherDatabase()
	{
		Assume.That(CollectionByIdSupported, Is.True, "Only the v2 API of Chroma 1.5.7 and later gets a collection by its id.");
		var database = $"database{Random.Shared.Next()}";

		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		var collection = await client.CreateCollection($"collection{Random.Shared.Next()}");
		await client.CreateDatabase(database);
		Assert.That((await client.GetCollectionById(collection.Id)).Id, Is.EqualTo(collection.Id));
		await Assert.ThatAsync(() => client.GetCollectionById(collection.Id, database: database), Throws.InstanceOf<ChromaException>().With.Message.Contains("does not exist"));
	}

	// The older servers have no such path: the client reports it with a ChromaException that names the request,
	// "Not Found" from the Python servers, "NotFound" from Chroma 1.x, which answers with an empty body.
	[Test]
	public async Task GetCollectionByIdNotSupported()
	{
		Assume.That(CollectionByIdSupported, Is.False, "This server gets a collection by its id.");

		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		var collection = await client.CreateCollection($"collection{Random.Shared.Next()}");
		await Assert.ThatAsync(() => client.GetCollectionById(collection.Id), Throws.InstanceOf<ChromaException>().With.Message.Match($"^Not ?Found: GET /api/v[12]/.*collections/by-id/{collection.Id}$"));
	}

	[Test]
	[Ignore("Failing because of bug on Chroma's side.", Until = "2025-04-21")]
	public async Task ListCollectionsSimple()
	{
		var names = new[] { $"collection{Random.Shared.Next()}", $"collection{Random.Shared.Next()}" };

		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		await client.CreateCollection(names[0]);
		await client.CreateCollection(names[1]);
		var result = await client.ListCollections();
		Assert.That(result, Is.Not.Null);
		Assert.That(result, Has.Count.GreaterThanOrEqualTo(2));
		Assert.That(result.Select(x => x.Name), Contains.Item(names[0]));
		Assert.That(result.Select(x => x.Name), Contains.Item(names[1]));
	}

	[Test]
	public async Task ListCollectionsPages()
	{
		Assume.That(ListCollectionsPagingSupported, Is.True, "Chroma 0.4.15 and earlier ignore the limit and the offset of the list of the collections.");
		var database = $"database{Random.Shared.Next()}";

		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		await client.CreateDatabase(database);
		var names = new[] { $"collection{Random.Shared.Next()}", $"collection{Random.Shared.Next()}", $"collection{Random.Shared.Next()}" };
		foreach (var name in names)
		{
			await client.CreateCollection(name, database: database);
		}
		var first = await client.ListCollections(limit: 2, database: database);
		var second = await client.ListCollections(limit: 2, offset: 2, database: database);
		Assert.That(first, Has.Count.EqualTo(2));
		Assert.That(second, Has.Count.EqualTo(1));
		Assert.That(first.Concat(second).Select(x => x.Name), Is.EquivalentTo(names));
	}

	[Test]
	public async Task CreateCollectionWithoutMetadata()
	{
		var name = $"collection{Random.Shared.Next()}";

		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		var result = await client.CreateCollection(name);
		Assert.That(result, Is.Not.Null);
		Assert.That(result.Name, Is.EqualTo(name));
	}

	[Test]
	public async Task CreateCollectionWithMetadata()
	{
		var name = $"collection{Random.Shared.Next()}";
		var metadata = new Dictionary<string, object>()
		{
			{ "test", "foo" },
			{ "test2", 10 },
		};

		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		var result = await client.CreateCollection(name,
			metadata: metadata);
		Assert.That(result, Is.Not.Null);
		Assert.That(result.Name, Is.EqualTo(name));
		Assert.That(result.Metadata, Is.Not.Null);
		Assert.That(result.Metadata["test"], Is.EqualTo(metadata["test"]));
		Assert.That(result.Metadata["test2"], Is.EqualTo(metadata["test2"]));
	}

	[Test]
	public async Task CreateCollectionAlreadyExists()
	{
		var name = $"collection{Random.Shared.Next()}";

		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		await client.CreateCollection(name);
		await Assert.ThatAsync(async () => await client.CreateCollection(name), Throws.InstanceOf<ChromaException>().With.Message.Matches($@"^Collection \[?{name}\]? already exists"));
	}

	[Test]
	[Ignore("Failing because of bug on Chroma's side.", Until = "2025-04-21")]
	public async Task DeleteCollection()
	{
		var name = $"collection{Random.Shared.Next()}";

		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		await client.CreateCollection(name);
		await client.DeleteCollection(name);
	}

	[Test]
	public async Task DeleteCollectionNotExists()
	{
		var name = $"collection{Random.Shared.Next()}";

		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		await Assert.ThatAsync(async () => await client.DeleteCollection(name), Throws.InstanceOf<ChromaException>().With.Message.Matches($@"^Collection \[?{name}\]? does not exist"));
	}

	[Test]
	public async Task GetOrCreateCollectionDoesNotExist()
	{
		var name = $"collection{Random.Shared.Next()}";

		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		var result = await client.GetOrCreateCollection(name);
		Assert.That(result, Is.Not.Null);
		Assert.That(result.Name, Is.EqualTo(name));
	}

	[Test]
	public async Task GetOrCreateCollectionDoesExist()
	{
		var name = $"collection{Random.Shared.Next()}";

		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		var result1 = await client.GetOrCreateCollection(name);
		var result2 = await client.GetOrCreateCollection(name);
		Assert.That(result1, Is.Not.Null);
		Assert.That(result1.Name, Is.EqualTo(name));
		Assert.That(result2, Is.Not.Null);
		Assert.That(result2.Name, Is.EqualTo(name));
		Assert.That(result1.Id, Is.EqualTo(result2.Id));
	}

	[Test]
	[Ignore("Failing because of bug on Chroma's side.", Until = "2025-04-21")]
	public async Task CountCollections()
	{
		Assume.That(CountCollectionsAndNotContainsSupported, Is.True, "Chroma 0.4.15 has no count_collections, no $not_contains filter and no tenant and database in the collections.");
		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		var list = await client.ListCollections();
		var result = await client.CountCollections();
		Assert.That(result, Is.EqualTo(list.Count));
	}
}
