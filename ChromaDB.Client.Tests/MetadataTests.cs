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
		await client.AddAsync(new ChromaRecords(["a"]) { Embeddings = [Embedding1], Metadatas = [new Dictionary<string, object> { ["date"] = "2026-10-04", ["text"] = "t" }] });
		var metadata = (await client.GetAsync("a", include: ChromaGetInclude.Metadatas))!.Metadata!;
		Assert.That(metadata["date"], Is.EqualTo("2026-10-04"));
		Assert.That(metadata["text"], Is.EqualTo("t"));
	}

	// A whole double goes as 2.0, as the Python client writes it: Chroma keeps it a float, and a list of doubles in a
	// filter is not a list of ints and floats, which Chroma rejects.
	[Test]
	public async Task WholeDoublesStayFloats()
	{
		var client = await Init(BaseConfigurationOptions.WithMetadataValues(ChromaMetadataValues.Exact));
		await client.AddAsync(new ChromaRecords(["a", "b"])
		{
			Embeddings = [Embedding1, Embedding2],
			Metadatas = [new Dictionary<string, object> { ["d"] = 2.0, ["f"] = 3f, ["m"] = 4m }, new Dictionary<string, object> { ["d"] = 2.25 }],
		});
		var metadata = (await client.GetAsync("a", include: ChromaGetInclude.Metadatas))!.Metadata!;
		Assert.That(metadata["d"], Is.InstanceOf<double>().And.EqualTo(2.0));
		Assert.That(metadata["f"], Is.InstanceOf<double>().And.EqualTo(3.0));
		Assert.That(metadata["m"], Is.InstanceOf<double>().And.EqualTo(4.0));
		var found = await client.GetAsync(where: ChromaWhereOperator.In("d", 2.0, 2.25), include: ChromaGetInclude.None);
		Assert.That(found.Select(e => e.Id), Is.EquivalentTo(new[] { "a", "b" }));
		// Stored as an int, 2 would not be under 2.2 for Chroma.
		found = await client.GetAsync(where: ChromaWhereOperator.LessThan("d", 2.2), include: ChromaGetInclude.None);
		Assert.That(found.Select(e => e.Id), Is.EqualTo(new[] { "a" }));
	}

	[Test]
	public async Task DatesStayStringsByDefault()
	{
		var client = await Init(BaseConfigurationOptions);
		await client.AddAsync(new ChromaRecords(["a"]) { Embeddings = [Embedding1], Metadatas = [new Dictionary<string, object> { ["date"] = "2026-10-04" }] });
		Assert.That((await client.GetAsync("a", include: ChromaGetInclude.Metadatas))!.Metadata!["date"], Is.EqualTo("2026-10-04"));
	}

	[Test]
	public async Task DatesAreInferredWithInferred()
	{
		var client = await Init(BaseConfigurationOptions.WithMetadataValues(ChromaMetadataValues.Inferred));
		await client.AddAsync(new ChromaRecords(["a"]) { Embeddings = [Embedding1], Metadatas = [new Dictionary<string, object> { ["date"] = "2026-10-04" }] });
		Assert.That((await client.GetAsync("a", include: ChromaGetInclude.Metadatas))!.Metadata!["date"], Is.EqualTo(new DateTime(2026, 10, 4)));
	}

	[Test]
	public async Task ListsInMetadata()
	{
		var client = await Init(BaseConfigurationOptions.WithMetadataValues(ChromaMetadataValues.Exact));
		var records = new ChromaRecords(["a"])
		{
			Embeddings = [Embedding1],
			Metadatas = [new Dictionary<string, object> { ["texts"] = new List<string> { "x", "y" }, ["array"] = new[] { "z" }, ["ints"] = new List<int> { 1, 2 }, ["floats"] = new List<double> { 1.5, 2.25 }, ["bools"] = new List<bool> { true, false } }],
		};
		if (IsChroma0)
		{
			// The server would store the record without its lists: the client stops before sending it.
			var ex = Assert.ThrowsAsync<ChromaException>(() => client.AddAsync(records));
			Assert.That(ex!.StatusCode, Is.Null);
			Assert.That(await client.CountAsync(), Is.EqualTo(0));
			return;
		}
		if (!MetadataListsSupported)
		{
			var ex = Assert.ThrowsAsync<ChromaException>(() => client.AddAsync(records));
			Assert.That(ex!.StatusCode, Is.EqualTo((HttpStatusCode)422));
			return;
		}
		await client.AddAsync(records);
		var metadata = (await client.GetAsync("a", include: ChromaGetInclude.Metadatas))!.Metadata!;
		Assert.That(metadata["texts"], Is.EqualTo(new List<object> { "x", "y" }));
		Assert.That(metadata["array"], Is.EqualTo(new List<object> { "z" }));
		Assert.That(metadata["ints"], Is.EqualTo(new List<object> { 1L, 2L }));
		Assert.That(metadata["floats"], Is.EqualTo(new List<object> { 1.5, 2.25 }));
		Assert.That(metadata["bools"], Is.EqualTo(new List<object> { true, false }));
		// A whole double goes as 1.0: Chroma finds 1 only in a list of ints, and 1.0 only in a list of doubles.
		await client.AddAsync(new ChromaRecords(["b"]) { Embeddings = [Embedding2], Metadatas = [new Dictionary<string, object> { ["floats"] = new List<double> { 1.0, 2.5 } }] });
		var found = await client.GetAsync(where: ChromaWhereOperator.Contains("floats", 1.0), include: ChromaGetInclude.None);
		Assert.That(found.Select(e => e.Id), Is.EqualTo(new[] { "b" }));
	}

	[Test]
	public async Task UpsertAndUpdateListsInMetadata()
	{
		Assume.That(MetadataListsSupported, Is.True, "Chroma 1.4.1 and earlier do not store lists in metadata.");
		var client = await Init(BaseConfigurationOptions.WithMetadataValues(ChromaMetadataValues.Exact));
		await client.UpsertAsync(new ChromaRecords(["a"]) { Embeddings = [Embedding1], Metadatas = [new Dictionary<string, object> { ["texts"] = new[] { "x" } }] });
		await client.UpdateAsync(new ChromaRecords(["a"]) { Metadatas = [new Dictionary<string, object> { ["texts"] = new[] { "y", "z" } }] });
		Assert.That((await client.GetAsync("a", include: ChromaGetInclude.Metadatas))!.Metadata!["texts"], Is.EqualTo(new List<object> { "y", "z" }));
	}

	[Test]
	public async Task ContainsAndNotContains()
	{
		Assume.That(MetadataListsSupported, Is.True, "Chroma 1.4.1 and earlier do not store lists in metadata.");
		var client = await Init(BaseConfigurationOptions);
		await client.AddAsync(new ChromaRecords(["a", "b"])
		{
			Embeddings = [Embedding1, Embedding2],
			Metadatas = [new Dictionary<string, object> { ["texts"] = new[] { "x", "y" }, ["ints"] = new[] { 1, 2 } }, new Dictionary<string, object> { ["texts"] = new[] { "z" }, ["ints"] = new[] { 3 } }],
		});
		Assert.That((await client.GetAsync(where: ChromaWhereOperator.Contains("texts", "x"))).Select(x => x.Id), Is.EqualTo(new[] { "a" }));
		Assert.That((await client.GetAsync(where: ChromaWhereOperator.NotContains("texts", "x"))).Select(x => x.Id), Is.EqualTo(new[] { "b" }));
		Assert.That((await client.GetAsync(where: ChromaWhereOperator.Contains("ints", 3))).Select(x => x.Id), Is.EqualTo(new[] { "b" }));
		Assert.That((await client.QueryAsync(Embedding2, where: ChromaWhereOperator.Contains("texts", "y"))).Select(x => x.Id), Is.EqualTo(new[] { "a" }));
	}

	// The 0.x servers know only $gt, $gte, $lt, $lte, $ne, $eq, $in and $nin in metadata filters.
	[Test]
	public async Task ContainsOnChroma0Throws()
	{
		Assume.That(IsChroma0, Is.True, "Chroma 1.x has $contains in metadata filters.");
		var client = await Init(BaseConfigurationOptions);
		await client.AddAsync(new ChromaRecords(["a"]) { Embeddings = [Embedding1], Metadatas = [new Dictionary<string, object> { ["text"] = "x" }] });
		var ex = Assert.ThrowsAsync<ChromaException>(() => client.GetAsync(where: ChromaWhereOperator.Contains("text", "x")));
		Assert.That(ex!.Message, Does.Contain("$contains"));
	}

	async Task<ChromaCollectionClient> Init(ChromaConfigurationOptions options)
	{
		var collection = await new ChromaClient(options, HttpClient).CreateCollectionAsync($"collection{Random.Shared.Next()}");
		return new ChromaCollectionClient(collection, options, HttpClient);
	}
}
