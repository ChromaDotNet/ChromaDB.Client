using ChromaDB.Client.Models;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

[TestFixture]
public class CollectionClientCUDTests : ChromaTestsBase
{
	[Test]
	public async Task AddJustIds()
	{
		var client = await Init();
		await WithoutEmbeddings(() => client.Add([$"{Guid.NewGuid()}"]));
	}

	[Test]
	public async Task AddWithEmbeddings()
	{
		var client = await Init();
		await client.Add([$"{Guid.NewGuid()}"],
			embeddings: [new([1f, 0.5f, 0f, -0.5f, -1f])]);
	}

	[Test]
	public async Task AddWithMetadatas()
	{
		var client = await Init();
		await WithoutEmbeddings(() => client.Add([$"{Guid.NewGuid()}"],
			metadatas: [new Dictionary<string, object>
			{
				{ "key", "value" },
				{ "key2", 10 },
			}]));
	}

	[Test]
	public async Task AddWithDocuments()
	{
		var client = await Init();
		await WithoutEmbeddings(() => client.Add([$"{Guid.NewGuid()}"],
			documents: ["test"]));
	}

	[Test]
	public async Task AddWithAll()
	{
		var client = await Init();
		await client.Add([$"{Guid.NewGuid()}"],
			embeddings: [new([1f, 0.5f, 0f, -0.5f, -1f])],
			metadatas: [new Dictionary<string, object>
			{
				{ "key", "value" },
				{ "key2", 10 },
			}],
			documents: ["test"]);
	}

	[Test]
	public async Task UpdateNothing()
	{
		var id = $"{Guid.NewGuid()}";

		var client = await Init();
		await client.Add([id], embeddings: Embeddings(1));
		await client.Update([id]);
	}

	[Test]
	public async Task UpdateEmbeddings()
	{
		var id = $"{Guid.NewGuid()}";

		var client = await Init();
		await client.Add([id], embeddings: Embeddings(1));
		await client.Update([id],
			embeddings: [new([1f, 0.5f, 0f, -0.5f, -1f])]);
	}

	[Test]
	public async Task UpdateMetadatas()
	{
		var id = $"{Guid.NewGuid()}";

		var client = await Init();
		await client.Add([id], embeddings: Embeddings(1));
		await client.Update([id],
			metadatas: [new Dictionary<string, object>
			{
				{ "key", "value" },
				{ "key2", 10 },
			}]);
	}

	[Test]
	public async Task UpdateDocuments()
	{
		var id = $"{Guid.NewGuid()}";

		var client = await Init();
		await client.Add([id], embeddings: Embeddings(1));
		await client.Update([id],
			documents: ["test"]);
	}

	[Test]
	public async Task UpdateAll()
	{
		var id = $"{Guid.NewGuid()}";

		var client = await Init();
		await client.Add([id], embeddings: Embeddings(1));
		await client.Update([id],
			embeddings: [new([1f, 0.5f, 0f, -0.5f, -1f])],
			metadatas: [new Dictionary<string, object>
			{
				{ "key", "value" },
				{ "key2", 10 },
			}],
			documents: ["test"]);
	}

	[Test]
	public async Task UpsertJustIds()
	{
		var client = await Init();
		await WithoutEmbeddings(() => client.Upsert([$"{Guid.NewGuid()}"]));
	}

	[Test]
	public async Task UpsertWithEmbeddings()
	{
		var client = await Init();
		await client.Upsert([$"{Guid.NewGuid()}"],
			embeddings: [new([1f, 0.5f, 0f, -0.5f, -1f])]);
	}

	[Test]
	public async Task UpsertWithMetadatas()
	{
		var client = await Init();
		await WithoutEmbeddings(() => client.Upsert([$"{Guid.NewGuid()}"],
			metadatas: [new Dictionary<string, object>
			{
				{ "key", "value" },
				{ "key2", 10 },
			}]));
	}

	[Test]
	public async Task UpsertWithDocuments()
	{
		var client = await Init();
		await WithoutEmbeddings(() => client.Upsert([$"{Guid.NewGuid()}"],
			documents: ["test"]));
	}

	[Test]
	public async Task UpsertWithAll()
	{
		var client = await Init();
		await client.Upsert([$"{Guid.NewGuid()}"],
			embeddings: [new([1f, 0.5f, 0f, -0.5f, -1f])],
			metadatas: [new Dictionary<string, object>
			{
				{ "key", "value" },
				{ "key2", 10 },
			}],
			documents: ["test"]);
	}

	[Test]
	public async Task DeleteByIdExisting()
	{
		var id = $"{Guid.NewGuid()}";

		var client = await Init();
		await client.Add([id], embeddings: Embeddings(1));
		await client.Delete([id]);
	}

	[Test]
	public async Task DeleteByIdNonExisting()
	{
		var id = $"{Guid.NewGuid()}";

		var client = await Init();
		await client.Delete([id]);
	}

	[Test]
	public async Task DeleteByMultipleIds()
	{
		var id1 = $"{Guid.NewGuid()}";
		var id2 = $"{Guid.NewGuid()}";

		var client = await Init();
		await client.Add([id1, id2], embeddings: Embeddings(2));
		await client.Delete([id1, id2]);
	}

	[Test]
	public async Task DeleteByMultipleIdsOneNonExisting()
	{
		var id1 = $"{Guid.NewGuid()}";
		var id2 = $"{Guid.NewGuid()}";

		var client = await Init();
		await client.Add([id1], embeddings: Embeddings(1));
		await client.Delete([id1, id2]);
	}

	[Test]
	public async Task DeleteByMultipleIdsWithWhere()
	{
		var id1 = $"{Guid.NewGuid()}";
		var id2 = $"{Guid.NewGuid()}";

		var client = await Init();
		await client.Add([id1, id2],
			embeddings: Embeddings(2),
			metadatas: [new Dictionary<string, object>
			{
			}, new Dictionary<string, object>
			{
				{ "key", "value" },
			}]);
		await client.Delete([id1, id2],
			where: ChromaWhereOperator.Equal("key", "value"));
	}

	[Test]
	public async Task DeleteByMultipleIdsWithWhereDocument()
	{
		var id1 = $"{Guid.NewGuid()}";
		var id2 = $"{Guid.NewGuid()}";

		var client = await Init();
		await client.Add([id1, id2],
			embeddings: Embeddings(2),
			documents: ["Doc1", "Doc2"]);
		await client.Delete([id1, id2],
			whereDocument: ChromaWhereDocumentOperator.Contains("2"));
	}

	[Test]
	public async Task AddWithInconsistentDimensions()
	{
		Assume.That(EmbeddingDimensionsChecked, Is.True, "Before Chroma 0.5.20 the server accepts embeddings of different dimensions.");
		var client = await Init();
		var exception = Assert.ThrowsAsync<ChromaException>(() => client.Add(["a", "b"], embeddings: [new([1f, 2f]), new([1f, 2f, 3f])]));
		Assert.That(exception!.Message, Does.Contain("dimension").IgnoreCase.And.Not.Contain("{"));
	}

	[Test]
	public async Task RecordsWithUris()
	{
		Assume.That(UrisSupported, Is.True, "Chroma 0.4.15 and earlier do not return the URIs of the records.");
		var client = await Init();
		await client.Add(new ChromaRecords(["a", "b"]) { Embeddings = Embeddings(2), Uris = ["file://a", null] });
		await client.Update(new ChromaRecords(["b"]) { Uris = ["file://b2"] });
		await client.Upsert(new ChromaRecords(["c"]) { Embeddings = Embeddings(1), Uris = ["file://c"] });
		var entries = await client.Get(["a", "b", "c"], include: ChromaGetInclude.Uris);
		Assert.That(entries.ToDictionary(x => x.Id, x => x.Uri), Is.EquivalentTo(new Dictionary<string, string?> { ["a"] = "file://a", ["b"] = "file://b2", ["c"] = "file://c" }));
		var nearest = await client.Query(new ReadOnlyMemory<float>([1f, 0.5f, 0f, -0.5f, -1f]), nResults: 3, include: ChromaQueryInclude.Uris);
		Assert.That(nearest.Select(x => x.Uri), Is.EquivalentTo(new[] { "file://a", "file://b2", "file://c" }));
	}

	// Chroma 0.4.10 to 0.4.15 reject "uris" in include with the validation errors of FastAPI: the message is theirs.
	[Test]
	public async Task UrisOnAServerWithoutThem()
	{
		Assume.That(UrisSupported, Is.False, "This server returns the URIs of the records.");
		var client = await Init();
		await Assert.ThatAsync(() => client.Get(include: ChromaGetInclude.Uris),
			Throws.InstanceOf<ChromaException>().With.Message.StartsWith("body.include.0").And.Message.Contains("documents"));
	}

	async Task<ChromaCollectionClient> Init()
	{
		var name = $"collection{Random.Shared.Next()}";
		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		var collection = await client.GetOrCreateCollection(name);
		return new ChromaCollectionClient(collection, BaseConfigurationOptions, HttpClient);
	}
}
