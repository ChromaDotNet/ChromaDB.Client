using System.Net;
using ChromaDB.Client.Models;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

[TestFixture]
public class MetadataTests : ChromaTestsBase
{
	static readonly ReadOnlyMemory<float> Embedding1 = new([1f, 0f]);
	static readonly ReadOnlyMemory<float> Embedding2 = new([0f, 1f]);

	[Test]
	public async Task StringsStayStrings()
	{
		var client = await Init(BaseConfigurationOptions.WithMetadataValues(ChromaMetadataValues.Exact));
		await client.Add(new ChromaRecords(["a"]) { Embeddings = [Embedding1], Metadatas = [new() { ["date"] = "2026-10-04", ["text"] = "t" }] });
		var metadata = (await client.Get("a", include: ChromaGetInclude.Metadatas))!.Metadata!;
		Assert.That(metadata["date"], Is.EqualTo("2026-10-04"));
		Assert.That(metadata["text"], Is.EqualTo("t"));
	}

	[Test]
	public async Task DatesStayStringsByDefault()
	{
		var client = await Init(BaseConfigurationOptions);
		await client.Add(new ChromaRecords(["a"]) { Embeddings = [Embedding1], Metadatas = [new() { ["date"] = "2026-10-04" }] });
		Assert.That((await client.Get("a", include: ChromaGetInclude.Metadatas))!.Metadata!["date"], Is.EqualTo("2026-10-04"));
	}

	[Test]
	public async Task DatesAreInferredWithInferred()
	{
		var client = await Init(BaseConfigurationOptions.WithMetadataValues(ChromaMetadataValues.Inferred));
		await client.Add(new ChromaRecords(["a"]) { Embeddings = [Embedding1], Metadatas = [new() { ["date"] = "2026-10-04" }] });
		Assert.That((await client.Get("a", include: ChromaGetInclude.Metadatas))!.Metadata!["date"], Is.EqualTo(new DateTime(2026, 10, 4)));
	}

	[Test]
	public async Task ListsInMetadata()
	{
		var client = await Init(BaseConfigurationOptions.WithMetadataValues(ChromaMetadataValues.Exact));
		var records = new ChromaRecords(["a"])
		{
			Embeddings = [Embedding1],
			Metadatas = [new() { ["texts"] = new List<string> { "x", "y" }, ["array"] = new[] { "z" }, ["ints"] = new List<int> { 1, 2 }, ["floats"] = new List<double> { 1.5, 2.25 }, ["bools"] = new List<bool> { true, false } }],
		};
		if (IsChroma0)
		{
			// The server would store the record without its lists: the client stops before sending it.
			var ex = Assert.ThrowsAsync<ChromaException>(() => client.Add(records));
			Assert.That(ex!.StatusCode, Is.Null);
			Assert.That(await client.Count(), Is.EqualTo(0));
			return;
		}
		if (!MetadataListsSupported)
		{
			var ex = Assert.ThrowsAsync<ChromaException>(() => client.Add(records));
			Assert.That(ex!.StatusCode, Is.EqualTo((HttpStatusCode)422));
			return;
		}
		await client.Add(records);
		var metadata = (await client.Get("a", include: ChromaGetInclude.Metadatas))!.Metadata!;
		Assert.That(metadata["texts"], Is.EqualTo(new List<object> { "x", "y" }));
		Assert.That(metadata["array"], Is.EqualTo(new List<object> { "z" }));
		Assert.That(metadata["ints"], Is.EqualTo(new List<object> { 1L, 2L }));
		Assert.That(metadata["floats"], Is.EqualTo(new List<object> { 1.5, 2.25 }));
		Assert.That(metadata["bools"], Is.EqualTo(new List<object> { true, false }));
	}

	[Test]
	public async Task UpsertAndUpdateListsInMetadata()
	{
		Assume.That(MetadataListsSupported, Is.True, "Chroma 1.4.1 and earlier do not store lists in metadata.");
		var client = await Init(BaseConfigurationOptions.WithMetadataValues(ChromaMetadataValues.Exact));
		await client.Upsert(new ChromaRecords(["a"]) { Embeddings = [Embedding1], Metadatas = [new() { ["texts"] = new[] { "x" } }] });
		await client.Update(new ChromaRecords(["a"]) { Metadatas = [new() { ["texts"] = new[] { "y", "z" } }] });
		Assert.That((await client.Get("a", include: ChromaGetInclude.Metadatas))!.Metadata!["texts"], Is.EqualTo(new List<object> { "y", "z" }));
	}

	[Test]
	public async Task ContainsAndNotContains()
	{
		Assume.That(MetadataListsSupported, Is.True, "Chroma 1.4.1 and earlier do not store lists in metadata.");
		var client = await Init(BaseConfigurationOptions);
		await client.Add(new ChromaRecords(["a", "b"])
		{
			Embeddings = [Embedding1, Embedding2],
			Metadatas = [new() { ["texts"] = new[] { "x", "y" }, ["ints"] = new[] { 1, 2 } }, new() { ["texts"] = new[] { "z" }, ["ints"] = new[] { 3 } }],
		});
		Assert.That((await client.Get(where: ChromaWhereOperator.Contains("texts", "x"))).Select(x => x.Id), Is.EqualTo(new[] { "a" }));
		Assert.That((await client.Get(where: ChromaWhereOperator.NotContains("texts", "x"))).Select(x => x.Id), Is.EqualTo(new[] { "b" }));
		Assert.That((await client.Get(where: ChromaWhereOperator.Contains("ints", 3))).Select(x => x.Id), Is.EqualTo(new[] { "b" }));
		Assert.That((await client.Query(Embedding2, where: ChromaWhereOperator.Contains("texts", "y"))).Select(x => x.Id), Is.EqualTo(new[] { "a" }));
	}

	// The 0.x servers know only $gt, $gte, $lt, $lte, $ne, $eq, $in and $nin in metadata filters.
	[Test]
	public async Task ContainsOnChroma0Throws()
	{
		Assume.That(IsChroma0, Is.True, "Chroma 1.x has $contains in metadata filters.");
		var client = await Init(BaseConfigurationOptions);
		await client.Add(new ChromaRecords(["a"]) { Embeddings = [Embedding1], Metadatas = [new() { ["text"] = "x" }] });
		var ex = Assert.ThrowsAsync<ChromaException>(() => client.Get(where: ChromaWhereOperator.Contains("text", "x")));
		Assert.That(ex!.Message, Does.Contain("$contains"));
	}

	async Task<ChromaCollectionClient> Init(ChromaConfigurationOptions options)
	{
		var collection = await new ChromaClient(options, HttpClient).CreateCollection($"collection{Random.Shared.Next()}");
		return new ChromaCollectionClient(collection, options, HttpClient);
	}
}
