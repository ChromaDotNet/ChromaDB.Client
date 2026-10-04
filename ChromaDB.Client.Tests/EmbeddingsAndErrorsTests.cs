using ChromaDB.Client.Models;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

[TestFixture]
public class EmbeddingsAndErrorsTests : ChromaTestsBase
{
	// Base64 from Chroma 1.0.13, which declares it, numbers before: the server keeps the same float32 values either way.
	[Test]
	public async Task EmbeddingsReadBackBitForBit()
	{
		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		var collectionClient = client.GetCollectionClient(await client.CreateCollection($"collection{Random.Shared.Next()}"));
		ReadOnlyMemory<float>[] embeddings = [new([0.1f, -2.5f, 3.25f, 1e-7f]), new([123456.79f, -0.333333343f, 1f / 3f, 6.5e-3f])];
		await collectionClient.Add(new ChromaRecords(["a", "b"]) { Embeddings = [.. embeddings] });
		await collectionClient.Upsert(new ChromaRecords(["c"]) { Embeddings = [embeddings[1]] });
		await collectionClient.Update(new ChromaRecords(["a"]) { Embeddings = [embeddings[1]] });
		var result = (await collectionClient.Get(["a", "b", "c"], include: ChromaGetInclude.Embeddings)).ToDictionary(x => x.Id, x => x.Embeddings!.Value.ToArray());
		Assert.That(result["a"], Is.EqualTo(embeddings[1].ToArray()));
		Assert.That(result["b"], Is.EqualTo(embeddings[1].ToArray()));
		Assert.That(result["c"], Is.EqualTo(embeddings[1].ToArray()));
	}

	[Test]
	public async Task BatchesOfTheLimitOfTheCaller()
	{
		var options = BaseConfigurationOptions.WithBatchSplitting(maxBatchSize: 2);
		var client = new ChromaClient(options, HttpClient);
		var collectionClient = client.GetCollectionClient(await client.CreateCollection($"collection{Random.Shared.Next()}"));
		var ids = new List<string> { "a", "b", "c", "d", "e" };
		await collectionClient.Add(new ChromaRecords(ids) { Embeddings = Enumerable.Repeat(new ReadOnlyMemory<float>([1f, 0f]), 5).ToList() });
		Assert.That(await collectionClient.Count(), Is.EqualTo(5));
		await collectionClient.Delete(ids);
		Assert.That(await collectionClient.Count(), Is.EqualTo(0));
	}

	// The kind of error of a missing collection: ValueError from the v1 API of Chroma 0.4.10 to 0.5.5,
	// InvalidCollection from 0.5.6 to 0.6.3, NotFoundError from 1.x.
	[Test]
	public void ErrorTypeOfAMissingCollection()
	{
		var expected = IsChroma1 ? "NotFoundError" : ChromaDB.Client.Tests.TestContainer.ChromaImage.Version >= new Version(0, 5, 6) ? "InvalidCollection" : "ValueError";
		var ex = Assert.ThrowsAsync<ChromaException>(() => new ChromaClient(BaseConfigurationOptions, HttpClient).GetCollection($"collection{Random.Shared.Next()}"));
		Assert.That(ex!.ErrorType, Is.EqualTo(expected));
	}
}
