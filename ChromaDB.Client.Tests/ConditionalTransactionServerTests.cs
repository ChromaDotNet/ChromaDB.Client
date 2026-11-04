using ChromaDB.Client.Models;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

// The conditional transactions on the server: on Chroma Cloud and on the servers that declare them; any other server answers with an
// error.
[TestFixture]
public class ConditionalTransactionServerTests : ChromaTestsBase
{
	// A commit writes all its records: an update, a delete and an add, each after the read it needs.
	[Test]
	public async Task ACommitWritesItsRecords()
	{
		var collection = await Init();
		if (!ConditionalTransactionsSupported)
		{
			await Assert.ThatAsync(() => collection.BeginConditionalTransaction().GetAsync(["a"]), Throws.InstanceOf<ChromaException>());
			return;
		}
		var transaction = collection.BeginConditionalTransaction();
		Assert.That((await transaction.GetAsync(["a", "b", "c"])).Select(entry => entry.Id), Is.EquivalentTo(new[] { "a", "b" }));
		await transaction.UpdateAsync(["a"], documents: ["apple tart"]);
		await transaction.DeleteAsync(["b"]);
		await transaction.AddAsync(["c"], embeddings: [new([0.5f, 0.5f])], documents: ["cherry pie"]);
		var result = await transaction.CommitAsync();
		Assert.That(result.RecordCount, Is.EqualTo(3));
		Assert.That(result.FirstInsertedRecordOffset, Is.Not.Null);
		var entries = await collection.GetAsync(include: ChromaGetInclude.Documents);
		Assert.That(entries.Select(entry => (entry.Id, entry.Document)), Is.EquivalentTo(new[] { ("a", "apple tart"), ("c", "cherry pie") }));
	}

	// A record that changed after the read of the transaction: the commit writes nothing.
	[Test]
	public async Task AConflict()
	{
		var collection = await Init();
		Assume.That(ConditionalTransactionsSupported, Is.True, "The server has no conditional transactions.");
		var transaction = collection.BeginConditionalTransaction();
		await transaction.GetAsync(["a"]);
		await collection.UpdateAsync(["a"], documents: ["changed outside"]);
		await transaction.UpdateAsync(["a"], documents: ["changed inside"]);
		await transaction.UpsertAsync(["d"], embeddings: [new([0.2f, 0.8f])], documents: ["date cake"]);
		await Assert.ThatAsync(() => transaction.CommitAsync(), Throws.InstanceOf<ChromaConditionalWriteConflictException>());
		Assert.That((await collection.GetAsync("a"))!.Document, Is.EqualTo("changed outside"));
		Assert.That(await collection.GetAsync("d"), Is.Null);
	}

	// After a conflict the run goes again, with a new transaction that reads the record as the other writer left it.
	[Test]
	public async Task ARunGoesAgainAfterAConflict()
	{
		var collection = await Init();
		Assume.That(ConditionalTransactionsSupported, Is.True, "The server has no conditional transactions.");
		await collection.UpsertAsync(["counter"], embeddings: [new([1f, 0f])], documents: ["0"]);
		var runs = 0;
		var counted = await collection.RunConditionalTransactionAsync(async (transaction, cancellationToken) =>
		{
			runs++;
			var value = int.Parse((await transaction.GetAsync("counter", include: ChromaGetInclude.Documents, cancellationToken: cancellationToken))!.Document!);
			if (runs == 1)
			{
				await collection.UpdateAsync(["counter"], documents: [$"{value + 10}"], cancellationToken: cancellationToken);
			}
			await transaction.UpdateAsync(["counter"], documents: [$"{value + 1}"], cancellationToken: cancellationToken);
			return value + 1;
		});
		Assert.That((runs, counted), Is.EqualTo((2, 11)));
		Assert.That((await collection.GetAsync("counter"))!.Document, Is.EqualTo("11"));
	}

	// A later read of a record that changed after the first read: Chroma Cloud answers it as it was, a single server refuses the read.
	[Test]
	public async Task ALaterReadOfARecordThatChanged()
	{
		var collection = await Init();
		Assume.That(ConditionalTransactionsSupported, Is.True, "The server has no conditional transactions.");
		var transaction = collection.BeginConditionalTransaction();
		await transaction.GetAsync(["a"]);
		await collection.UpdateAsync(["a"], documents: ["changed outside"]);
		if (ChromaCloud)
		{
			Assert.That((await transaction.GetAsync("a"))!.Document, Is.EqualTo("apple pie"));
			return;
		}
		await Assert.ThatAsync(() => transaction.GetAsync(["a"]), Throws.InstanceOf<ChromaStaleReadException>());
	}

	async Task<ChromaCollectionClient> Init()
	{
		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		var collection = client.GetCollectionClient(await client.CreateCollectionAsync($"collection{Random.Shared.Next()}"));
		await collection.AddAsync(["a", "b"], embeddings: [new([1f, 0f]), new([0f, 1f])], documents: ["apple pie", "banana split"]);
		return collection;
	}
}
