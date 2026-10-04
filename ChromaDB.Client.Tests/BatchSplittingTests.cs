using ChromaDB.Client.Models;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

[TestFixture]
public class BatchSplittingTests : ChromaTestsBase
{
	// One record beyond the max_batch_size of the server: up to Chroma 1.0.13 a single request fails, in batches it works.
	[Test]
	public async Task AddAndDeleteBeyondTheMaxBatchSize()
	{
		Assume.That(PreFlightChecksSupported, Is.True, "Chroma 0.4.10 has no pre-flight-checks, so the client does not know its limit.");
		var options = TestMaxBatchSize is { } limit ? BaseConfigurationOptions.WithBatchSplitting(limit) : BaseConfigurationOptions.WithBatchSplitting();
		var client = new ChromaClient(options, HttpClient);
		var count = (await client.GetPreFlightChecks()).MaxBatchSize + 1;
		var collectionClient = client.GetCollectionClient(await client.CreateCollection($"collection{Random.Shared.Next()}"));
		var ids = Enumerable.Range(0, count).Select(i => $"r{i}").ToList();

		await collectionClient.Add(new ChromaRecords(ids) { Embeddings = Enumerable.Repeat(new ReadOnlyMemory<float>([0.5f, 0.5f]), count).ToList() });
		Assert.That(await collectionClient.Count(), Is.EqualTo(count));
		await collectionClient.Delete(ids);
		Assert.That(await collectionClient.Count(), Is.EqualTo(0));
	}
}
