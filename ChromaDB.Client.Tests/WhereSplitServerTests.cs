using ChromaDB.Client.Models;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

// The conditions on the ids and on the documents in the where clause, on every tested server: get, query and delete send them as the
// ids and where_document of the request, the Search API of Chroma Cloud as they are.
[TestFixture]
public class WhereSplitServerTests : ChromaTestsBase
{
	static readonly ReadOnlyMemory<float> Embedding1 = new([1f, 0f]);
	static readonly ReadOnlyMemory<float> Embedding2 = new([0f, 1f]);
	static readonly ChromaWhereOperator Pool = ChromaWhereOperator.Document(ChromaWhereDocumentOperator.Contains("pool"));

	[Test]
	public async Task GetQueryAndDelete()
	{
		var collection = await Init();
		var where = ChromaWhereOperator.In(ChromaSearchKeys.Id, "a", "b", "c") & Pool & ChromaWhereOperator.Equal("k", 1);
		Assert.That((await collection.GetAsync(where: where, include: ChromaGetInclude.None)).Select(x => x.Id), Is.EqualTo(new[] { "a" }));
		var query = await collection.QueryAsync(new ChromaQuery([Embedding1]) { NResults = 3, Where = ChromaWhereOperator.Not(ChromaWhereOperator.Document(ChromaWhereDocumentOperator.Contains("spa"))), Include = ChromaQueryInclude.Distances });
		Assert.That(query[0].Select(x => x.Id), Is.EquivalentTo(new[] { "b", "c" }));
		await collection.DeleteAsync(["a", "b", "c"], ChromaWhereOperator.Equal(ChromaSearchKeys.Id, "c"));
		Assert.That((await collection.GetAsync(include: ChromaGetInclude.None)).Select(x => x.Id), Is.EquivalentTo(new[] { "a", "b" }));
	}

	[Test]
	public async Task SearchOnChromaCloud()
	{
		Assume.That(ChromaCloud, Is.True, "Only Chroma Cloud has the Search API.");
		var collection = await Init();
		var found = await collection.SearchAsync(new ChromaSearch { Where = ChromaWhereOperator.In(ChromaSearchKeys.Id, "b") | Pool, Select = [ChromaSearchKeys.Document] });
		Assert.That(found.Select(x => x.Id), Is.EquivalentTo(new[] { "a", "b", "c" }));
	}

	// A query that expects another space than the one of the collection throws, before the request.
	[Test]
	public async Task ExpectedSpace()
	{
		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		var created = await client.CreateCollectionAsync(new ChromaCollectionDefinition($"collection{Random.Shared.Next()}") { Configuration = new() { Space = ChromaSpace.L2 } });
		Assume.That(created.Space, Is.Not.Null, "The server does not report the space of the collection.");
		var collection = client.GetCollectionClient(created);
		await collection.AddAsync(new ChromaRecords(["a"]) { Embeddings = [Embedding1] });
		await Assert.ThatAsync(() => collection.QueryAsync(new ChromaQuery([Embedding1]) { ExpectedSpace = ChromaSpace.Cosine }), Throws.InvalidOperationException);
		Assert.That((await collection.QueryAsync(new ChromaQuery([Embedding1]) { ExpectedSpace = ChromaSpace.L2 }))[0].Single().Id, Is.EqualTo("a"));
	}

	async Task<ChromaCollectionClient> Init()
	{
		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		var collection = client.GetCollectionClient(await client.CreateCollectionAsync($"collection{Random.Shared.Next()}"));
		await collection.AddAsync(new ChromaRecords(["a", "b", "c"])
		{
			Embeddings = [Embedding1, Embedding2, Embedding1],
			Metadatas = [new Dictionary<string, object> { ["k"] = 1 }, new Dictionary<string, object> { ["k"] = 1 }, new Dictionary<string, object> { ["k"] = 2 }],
			Documents = ["pool and spa", "gym", "pool"],
		});
		return collection;
	}
}
