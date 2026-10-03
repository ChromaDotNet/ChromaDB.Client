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
		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		var list = await client.ListCollections();
		var result = await client.CountCollections();
		Assert.That(result, Is.EqualTo(list.Count));
	}
}
