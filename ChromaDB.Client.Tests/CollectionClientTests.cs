using ChromaDB.Client.Models;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

[TestFixture]
public class CollectionClientTests : ChromaTestsBase
{
	[Test]
	public async Task CountEmptyCollection()
	{
		var client = await Init();
		var result = await client.Count();
		Assert.That(result, Is.EqualTo(0));
	}

	[Test]
	public async Task CountNonEmptyCollection()
	{
		var client = await Init();
		await client.Add([$"{Guid.NewGuid()}", $"{Guid.NewGuid()}", $"{Guid.NewGuid()}", $"{Guid.NewGuid()}", $"{Guid.NewGuid()}", $"{Guid.NewGuid()}"], embeddings: Embeddings(6));
		var result = await client.Count();
		Assert.That(result, Is.EqualTo(6));
	}

	[Test]
	public async Task PeekDefault()
	{
		var client = await Init();
		await client.Add([$"{Guid.NewGuid()}", $"{Guid.NewGuid()}", $"{Guid.NewGuid()}", $"{Guid.NewGuid()}", $"{Guid.NewGuid()}", $"{Guid.NewGuid()}", $"{Guid.NewGuid()}", $"{Guid.NewGuid()}", $"{Guid.NewGuid()}", $"{Guid.NewGuid()}", $"{Guid.NewGuid()}"], embeddings: Embeddings(11));
		var result = await client.Peek();
		Assert.That(result, Is.Not.Empty);
	}

	[Test]
	public async Task PeekExplicitLimit()
	{
		var client = await Init();
		await client.Add([$"{Guid.NewGuid()}", $"{Guid.NewGuid()}", $"{Guid.NewGuid()}", $"{Guid.NewGuid()}", $"{Guid.NewGuid()}", $"{Guid.NewGuid()}", $"{Guid.NewGuid()}", $"{Guid.NewGuid()}", $"{Guid.NewGuid()}", $"{Guid.NewGuid()}", $"{Guid.NewGuid()}"], embeddings: Embeddings(11));
		var result = await client.Peek(
			limit: 2);
		Assert.That(result, Has.Count.EqualTo(2));
	}

	[Test]
	public async Task ModifyCollectionName()
	{
		var client = await Init();
		await client.Modify(
			name: $"{client.Collection.Name}_modified");
		var result = await GetCollection($"{client.Collection.Name}_modified");
		Assert.That(result.Id, Is.EqualTo(client.Collection.Id));
	}

	[Test]
	public async Task ModifyCollectionMetadata()
	{
		var metadata = new Dictionary<string, object>()
		{
			{ "test", "foo" },
			{ "test2", 10 },
		};

		var client = await Init();
		await client.Modify(
			metadata: metadata);
		var result = await GetCollection(client.Collection.Name);
		Assert.That(result.Metadata, Is.Not.Null);
		Assert.That(result.Metadata["test"], Is.EqualTo(metadata["test"]));
		Assert.That(result.Metadata["test2"], Is.EqualTo(metadata["test2"]));
	}

	[Test]
	public async Task ModifyCollectionAll()
	{
		var metadata = new Dictionary<string, object>()
		{
			{ "test", 10 },
			{ "test3", "bar" },
		};

		var client = await Init();
		await client.Modify(
			name: $"{client.Collection.Name}_modified",
			metadata: metadata);
		var result = await GetCollection($"{client.Collection.Name}_modified");
		Assert.That(result.Id, Is.EqualTo(client.Collection.Id));
		Assert.That(result.Metadata, Is.Not.Null);
		Assert.That(result.Metadata["test"], Is.EqualTo(metadata["test"]));
		Assert.That(result.Metadata["test3"], Is.EqualTo(metadata["test3"]));
	}

	async Task<ChromaCollectionClient> Init()
	{
		var name = $"collection{Random.Shared.Next()}";
		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		var collection = await client.CreateCollection(name);
		return new ChromaCollectionClient(collection, BaseConfigurationOptions, HttpClient);
	}

	Task<ChromaCollection> GetCollection(string name)
		=> new ChromaClient(BaseConfigurationOptions, HttpClient).GetCollection(name);
}
