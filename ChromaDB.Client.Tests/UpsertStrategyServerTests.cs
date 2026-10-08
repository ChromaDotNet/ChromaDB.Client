using ChromaDB.Client.Models;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

// The upsert strategies on a server: what an upsert leaves is what Chroma would leave, and a query finds every record.
[TestFixture]
public class UpsertStrategyServerTests : ChromaTestsBase
{
	[TestCase(ChromaUpsertStrategy.SkipUnchangedEmbeddings)]
	public async Task UpsertLeavesWhatChromaWould(ChromaUpsertStrategy strategy)
	{
		Assume.That(IsChroma1, Is.True, "On Chroma 0.x, which has not the defect, the writes go as they are.");
		var options = BaseConfigurationOptions.WithMetadataValues(ChromaMetadataValues.Exact).WithUpsertStrategy(strategy);
		var collection = await new ChromaClient(options, HttpClient).CreateCollectionAsync($"collection{Random.Shared.Next()}");
		var client = new ChromaCollectionClient(collection, options, HttpClient);
		await client.AddAsync(new ChromaRecords(["a", "b"])
		{
			Embeddings = [new([1f, 0f]), new([0f, 1f])],
			Metadatas = [new Dictionary<string, object> { ["k"] = 1L, ["keep"] = "x" }, new Dictionary<string, object> { ["k"] = 1L, ["gone"] = 2L }],
			Documents = ["doc a", "doc b"],
		});
		await client.UpsertAsync(new ChromaRecords(["a", "b", "c"])
		{
			Embeddings = [new([1f, 0f]), new([1f, 1f]), new([0f, 2f])],
			Metadatas = [new Dictionary<string, object> { ["k"] = 2L }, new Dictionary<string, object> { ["k"] = 2L, ["gone"] = null! }, new Dictionary<string, object> { ["k"] = 3L }],
			Documents = [null, null, "doc c"],
		});
		var records = (await client.GetAsync(include: ChromaGetInclude.Embeddings | ChromaGetInclude.Metadatas | ChromaGetInclude.Documents)).ToDictionary(x => x.Id);
		Assert.That(records["a"].Metadata, Is.EquivalentTo(new Dictionary<string, object> { ["k"] = 2L, ["keep"] = "x" }));
		Assert.That(records["a"].Document, Is.EqualTo("doc a"));
		Assert.That(records["b"].Metadata, Is.EquivalentTo(new Dictionary<string, object> { ["k"] = 2L }));
		Assert.That(records["b"].Document, Is.EqualTo("doc b"));
		Assert.That(records["b"].Embedding!.Value.ToArray(), Is.EqualTo(new[] { 1f, 1f }));
		Assert.That(records["c"].Document, Is.EqualTo("doc c"));
		var found = await client.QueryAsync(new ChromaQuery([new([1f, 1f])]) { NResults = 3 });
		Assert.That(found[0].Select(x => x.Id), Is.EquivalentTo(new[] { "a", "b", "c" }));
	}

	// The case of KD-49, many times: 120 records with random vectors, an upsert of 20 of them with new vectors, then a query of all.
	// Counts the runs where the query misses a record, for each strategy: SkipUnchangedEmbeddings cannot help, since every embedding
	// written changes. Not in the CI: it takes minutes.
	[TestCase(ChromaUpsertStrategy.Server)]
	[TestCase(ChromaUpsertStrategy.SkipUnchangedEmbeddings)]
	[Explicit("A measurement of KD-49 on a single Chroma server.")]
	public async Task LostRecordsAfterUpserts(ChromaUpsertStrategy strategy)
	{
		// More runs with dotnet test -- TestRunParameters.Parameter(name=\"runs\", value=\"600\").
		var runs = int.Parse(TestContext.Parameters.Get("runs", "200"), System.Globalization.CultureInfo.InvariantCulture);
		const int records = 120;
		var options = BaseConfigurationOptions.WithUpsertStrategy(strategy);
		var random = new Random(1);
		var lost = 0;
		for (var run = 0; run < runs; run++)
		{
			var collection = await new ChromaClient(options, HttpClient).CreateCollectionAsync(new ChromaCollectionDefinition($"lost{Random.Shared.Next()}") { Configuration = new() { Space = ChromaSpace.Cosine } });
			var client = new ChromaCollectionClient(collection, options, HttpClient);
			var ids = Enumerable.Range(0, records).Select(i => $"r{i}").ToList();
			await client.UpsertAsync(ids, ids.Select(_ => Vector(random)).ToList());
			var rewritten = ids.OrderBy(_ => random.Next()).Take(20).ToList();
			await client.UpsertAsync(rewritten, rewritten.Select(_ => Vector(random)).ToList());
			var found = await client.QueryAsync(new ChromaQuery([Vector(random)]) { NResults = records });
			if (found[0].Count < records)
			{
				lost++;
			}
			await new ChromaClient(options, HttpClient).DeleteCollectionAsync(collection.Name);
		}
		TestContext.Out.WriteLine($"{strategy}: {lost} runs of {runs} miss a record.");
	}

	// The case that SkipUnchangedEmbeddings protects, many times: 60 records with random vectors, an upsert of 20 of them with the same
	// vectors and new metadata, then a query of all. Without deletes, Chroma searches on the graph. Not in the CI: it takes minutes.
	[TestCase(ChromaUpsertStrategy.Server)]
	[TestCase(ChromaUpsertStrategy.SkipUnchangedEmbeddings)]
	[Explicit("A measurement of KD-49 on a single Chroma server.")]
	public async Task LostRecordsAfterUpsertsWithTheSameEmbeddings(ChromaUpsertStrategy strategy)
	{
		const int runs = 300;
		const int records = 60;
		var options = BaseConfigurationOptions.WithUpsertStrategy(strategy);
		var random = new Random(1);
		var lost = 0;
		for (var run = 0; run < runs; run++)
		{
			var collection = await new ChromaClient(options, HttpClient).CreateCollectionAsync(new ChromaCollectionDefinition($"same{Random.Shared.Next()}") { Configuration = new() { Space = ChromaSpace.Cosine } });
			var client = new ChromaCollectionClient(collection, options, HttpClient);
			var ids = Enumerable.Range(0, records).Select(i => $"r{i}").ToList();
			var vectors = ids.Select(_ => Vector(random)).ToList();
			await client.UpsertAsync(ids, vectors);
			var rewritten = Enumerable.Range(0, records).OrderBy(_ => random.Next()).Take(20).ToList();
			await client.UpsertAsync(rewritten.Select(i => ids[i]).ToList(), rewritten.Select(i => vectors[i]).ToList(), rewritten.Select(_ => (IReadOnlyDictionary<string, object>)new Dictionary<string, object> { ["run"] = (long)run }).ToList());
			var found = await client.QueryAsync(new ChromaQuery([Vector(random)]) { NResults = records });
			if (found[0].Count < records)
			{
				lost++;
			}
			await new ChromaClient(options, HttpClient).DeleteCollectionAsync(collection.Name);
		}
		TestContext.Out.WriteLine($"{strategy}, same embeddings: {lost} runs of {runs} miss a record.");
		if (strategy == ChromaUpsertStrategy.SkipUnchangedEmbeddings)
		{
			Assert.That(lost, Is.EqualTo(0));
		}
	}

	// What one thread on the server costs: an add of 5,000 records of 384 dimensions, an upsert of the same records with new vectors,
	// and 100 queries of 10 results, 3 times. Not in the CI: it takes minutes.
	[Test]
	[Explicit("A measurement of the cost of RAYON_NUM_THREADS=1 on a single Chroma server.")]
	public async Task WriteAndQueryTimes()
	{
		const int records = 5000;
		var random = new Random(1);
		ReadOnlyMemory<float> Large() => Enumerable.Range(0, 384).Select(_ => (float)random.NextDouble()).ToArray();
		for (var round = 0; round < 3; round++)
		{
			var collection = await new ChromaClient(BaseConfigurationOptions, HttpClient).CreateCollectionAsync(new ChromaCollectionDefinition($"times{Random.Shared.Next()}") { Configuration = new() { Space = ChromaSpace.Cosine } });
			var client = new ChromaCollectionClient(collection, BaseConfigurationOptions, HttpClient);
			var ids = Enumerable.Range(0, records).Select(i => $"r{i}").ToList();
			var watch = System.Diagnostics.Stopwatch.StartNew();
			await client.AddAsync(ids, ids.Select(_ => Large()).ToList());
			var add = watch.Elapsed;
			watch.Restart();
			await client.UpsertAsync(ids, ids.Select(_ => Large()).ToList());
			var upsert = watch.Elapsed;
			watch.Restart();
			for (var query = 0; query < 100; query++)
			{
				await client.QueryAsync(new ChromaQuery([Large()]) { NResults = 10 });
			}
			var queries = watch.Elapsed;
			TestContext.Out.WriteLine($"Round {round}: add {add.TotalSeconds:F2} s, upsert {upsert.TotalSeconds:F2} s, 100 queries {queries.TotalSeconds:F2} s.");
			await new ChromaClient(BaseConfigurationOptions, HttpClient).DeleteCollectionAsync(collection.Name);
		}
	}

	static ReadOnlyMemory<float> Vector(Random random) => Enumerable.Range(0, 4).Select(_ => (float)random.NextDouble()).ToArray();
}
