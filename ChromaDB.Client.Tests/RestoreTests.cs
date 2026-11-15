using ChromaDB.Client.Backup;
using ChromaDB.Client.Models;
using NUnit.Framework;
using Testcontainers.Chroma;

namespace ChromaDB.Client.Tests;

// A restore into the server of the backup: into a new collection, and into one that exists, as the copy command of the CLI writes
// a source into its target.
[TestFixture]
public class RestoreServerTests : ChromaTestsBase
{
	string _path = null!;

	[SetUp]
	public void SetUp() => _path = Path.Combine(Path.GetTempPath(), $"chromadotnet-restore-test-{Guid.NewGuid():N}.zip");

	[TearDown]
	public void TearDown() => File.Delete(_path);

	[Test]
	public async Task IntoANewCollection()
	{
		var (client, name) = await Collection();
		await ChromaBackup.BackupAsync(client, _path, new() { Collections = [name] });
		await client.DeleteCollectionAsync(name);

		var result = (await ChromaBackup.RestoreAsync(client, _path)).Collections.Single();
		Assert.That((result.Created, result.Added), Is.EqualTo((true, 5L)));
		var verify = (await ChromaBackup.VerifyAsync(client, _path)).Collections.Single();
		Assert.That(verify.Same, Is.True, verify.FirstDifference);
	}

	// The old settings hnsw:* in the metadata, next to a schema, which Chroma refuses together in a new collection: they come at
	// the end, with the metadata, as the copy command sets them.
	[Test]
	public async Task OldSettingsInTheMetadataNextToASchema()
	{
		Assume.That(DistributedChroma, Is.False, "Distributed Chroma, the one Chroma Cloud runs on, has SPANN and refuses HNSW settings.");
		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		var name = $"collection{Random.Shared.Next()}";
		await client.CreateCollectionAsync(new ChromaCollectionDefinition(name) { Configuration = new() { Space = ChromaSpace.Cosine, Hnsw = new() { MaxNeighbors = 20, EfConstruction = 120 } } });
		await client.GetCollectionClient(name).AddAsync(new ChromaRecords(["a", "b"]) { Embeddings = [new([1f, 0f]), new([0f, 1f])] });
		await ChromaBackup.BackupAsync(client, _path, new() { Collections = [name] });
		await client.DeleteCollectionAsync(name);

		await ChromaBackup.RestoreAsync(client, _path);
		var restored = await client.GetCollectionAsync(name);
		Assert.That(restored.Space, Is.EqualTo(ChromaSpace.Cosine));
		var verify = (await ChromaBackup.VerifyAsync(client, _path)).Collections.Single();
		Assert.That(verify.Same, Is.True, $"{verify.FirstDifference}, schema {verify.SchemaSame}, metadata {verify.MetadataSame}");
	}

	[Test]
	public async Task AnExistingCollectionThatIsTheSameStays()
	{
		var (client, name) = await Collection();
		await ChromaBackup.BackupAsync(client, _path, new() { Collections = [name] });
		var result = (await ChromaBackup.RestoreAsync(client, _path)).Collections.Single();
		Assert.That((result.AlreadySame, result.Added, result.Created), Is.EqualTo((true, 0L, false)));
	}

	// With Fail, the default, a collection that exists and is not the same stops the restore before any write.
	[Test]
	public async Task FailWritesNothing()
	{
		var (client, name) = await Collection();
		await ChromaBackup.BackupAsync(client, _path, new() { Collections = [name] });
		var collectionClient = client.GetCollectionClient(name);
		await collectionClient.DeleteAsync(["r0"]);

		Assert.That(() => ChromaBackup.RestoreAsync(client, _path), Throws.InvalidOperationException.With.Message.Contains("1 missing"));
		Assert.That(await collectionClient.CountAsync(), Is.EqualTo(4));
	}

	// Resume adds what is missing, and leaves a different record as the server has it.
	[Test]
	public async Task ResumeAddsTheMissingRecords()
	{
		var (client, name) = await Collection();
		await ChromaBackup.BackupAsync(client, _path, new() { Collections = [name] });
		var collectionClient = client.GetCollectionClient(name);
		await collectionClient.DeleteAsync(["r0", "r3"]);
		await collectionClient.UpdateAsync(new ChromaRecords(["r1"]) { Documents = ["changed"] });

		var result = (await ChromaBackup.RestoreAsync(client, _path, new() { Existing = ChromaRestoreExisting.Resume })).Collections.Single();
		Assert.That((result.Added, result.Rewritten, result.Deleted), Is.EqualTo((2L, 0L, 0L)));
		var verify = (await ChromaBackup.VerifyAsync(client, _path)).Collections.Single();
		Assert.That((verify.Missing, verify.Different, verify.FirstDifference), Is.EqualTo((0L, 1L, "r1: document")));
	}

	// Force makes the collection the same: the record only on the server goes, the different one is deleted and added again, the
	// missing one added.
	[Test]
	public async Task ForceMakesTheCollectionTheSame()
	{
		var (client, name) = await Collection();
		await ChromaBackup.BackupAsync(client, _path, new() { Collections = [name] });
		var collectionClient = client.GetCollectionClient(name);
		await collectionClient.DeleteAsync(["r0"]);
		await collectionClient.UpdateAsync(new ChromaRecords(["r1"]) { Documents = ["changed"], Embeddings = [new([7f, 7f, 7f])] });
		await collectionClient.AddAsync(new ChromaRecords(["extra"]) { Embeddings = [new([9f, 9f, 9f])] });
		await collectionClient.ModifyAsync(metadata: new Dictionary<string, object> { ["owner"] = "someone else" });

		var result = (await ChromaBackup.RestoreAsync(client, _path, new() { ExistingByCollection = new Dictionary<string, ChromaRestoreExisting> { [name] = ChromaRestoreExisting.Force } })).Collections.Single();
		Assert.That((result.Created, result.Added, result.Rewritten, result.Deleted), Is.EqualTo((false, 1L, 1L, 1L)));
		var verify = (await ChromaBackup.VerifyAsync(client, _path)).Collections.Single();
		Assert.That(verify.Same, Is.True, $"{verify.FirstDifference}, metadata {verify.MetadataSame}");
	}

	// A collection with another dimension: Force deletes it and creates it again.
	[Test]
	public async Task ForceWithAnotherDimensionCreatesTheCollectionAgain()
	{
		var (client, name) = await Collection();
		await ChromaBackup.BackupAsync(client, _path, new() { Collections = [name] });
		await client.DeleteCollectionAsync(name);
		await client.CreateCollectionAsync(name);
		await client.GetCollectionClient(name).AddAsync(new ChromaRecords(["other"]) { Embeddings = [new([1f, 2f])] });

		var result = (await ChromaBackup.RestoreAsync(client, _path, new() { Existing = ChromaRestoreExisting.Force })).Collections.Single();
		Assert.That((result.Created, result.Added), Is.EqualTo((true, 5L)));
		Assert.That((await ChromaBackup.VerifyAsync(client, _path)).Same, Is.True);
	}

	async Task<(ChromaClient, string)> Collection()
	{
		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		var name = $"collection{Random.Shared.Next()}";
		await client.CreateCollectionAsync(new ChromaCollectionDefinition(name) { Metadata = new Dictionary<string, object> { ["owner"] = "tests" } });
		await client.GetCollectionClient(name).AddAsync(new ChromaRecords(["r0", "r1", "r2", "r3", "r4"])
		{
			Embeddings = [new([0.1f, -0f, 1e-38f]), new([3.4e38f, -3.4e38f, 1f]), new([1f / 3, 2f / 3, 1f]), new([float.Epsilon, 0f, 1f]), new([123456.789f, -0.000001f, 7f])],
			Metadatas = [new Dictionary<string, object> { ["i"] = 1L, ["f"] = 2.0 }, null, new Dictionary<string, object> { ["s"] = "é" }, new Dictionary<string, object> { ["b"] = false }, new Dictionary<string, object> { ["i"] = -3L }],
			Documents = ["d0", null, "d2", "d3", "d4"],
			Uris = UrisSupported ? ["u0", null, "u2", "u3", "u4"] : null,
		});
		return (client, name);
	}
}

// A backup of one server restored into another: the server of the tests and a second one, from the image of
// CHROMA_TEST_SECOND_IMAGE, like the one of VecturaDB with Chroma 1.5.9.
[TestFixture]
public class RestoreAcrossServersTests : ChromaTestsBase
{
	static readonly string? SecondImage = Environment.GetEnvironmentVariable("CHROMA_TEST_SECOND_IMAGE") is { Length: > 0 } image ? image : null;

	ChromaContainer? _second;
	string _path = null!;

	[OneTimeSetUp]
	public async Task StartTheSecondServer()
	{
		Assume.That(SecondImage, Is.Not.Null, "CHROMA_TEST_SECOND_IMAGE names the image of a second server.");
		_second = new ChromaBuilder(SecondImage!).Build();
		await _second.StartAsync();
	}

	[OneTimeTearDown]
	public async Task StopTheSecondServer()
	{
		if (_second is not null)
		{
			await _second.DisposeAsync();
		}
	}

	[SetUp]
	public void SetUp() => _path = Path.Combine(Path.GetTempPath(), $"chromadotnet-across-test-{Guid.NewGuid():N}.zip");

	[TearDown]
	public void TearDown() => File.Delete(_path);

	ChromaClient Second() => new(new ChromaConfigurationOptions($"{_second!.GetConnectionString()}api/v2/"), new HttpClient());

	[Test]
	public async Task FromTheServerOfTheTestsToTheSecond()
	{
		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		var name = await Filled(client);
		await ChromaBackup.BackupAsync(client, _path, new() { Collections = [name] });
		using var second = Second();
		await ChromaBackup.RestoreAsync(second, _path);
		var verify = (await ChromaBackup.VerifyAsync(second, _path)).Collections.Single();
		Assert.That(verify.Same, Is.True, $"{verify.FirstDifference}, schema {verify.SchemaSame}, metadata {verify.MetadataSame}");
	}

	[Test]
	public async Task FromTheSecondServerToTheOneOfTheTests()
	{
		using var second = Second();
		var name = await Filled(second);
		await ChromaBackup.BackupAsync(second, _path, new() { Collections = [name] });
		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		await ChromaBackup.RestoreAsync(client, _path);
		var verify = (await ChromaBackup.VerifyAsync(client, _path)).Collections.Single();
		Assert.That(verify.Same, Is.True, $"{verify.FirstDifference}, schema {verify.SchemaSame}, metadata {verify.MetadataSame}");
	}

	// Sparse vectors into a single Chroma server, which refuses the schema with their index: the error names the key, and no
	// collection stays there.
	[Test]
	public async Task SparseVectorsIntoAServerWithout()
	{
		Assume.That(SparseVectorIndexesSupported, Is.True, "Distributed Chroma, Chroma Cloud and VecturaDB take sparse vectors.");
		using var second = Second();
		Assume.That(await second.SupportsSparseVectorIndexesAsync(), Is.False, "The second server takes sparse vectors too.");
		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		var name = $"collection{Random.Shared.Next()}";
		await client.CreateCollectionAsync(new ChromaCollectionDefinition(name) { Schema = new ChromaCollectionSchema().WithSparseVectorIndex("sv") });
		await client.GetCollectionClient(name).AddAsync(new ChromaRecords(["a"]) { Embeddings = [new([1f, 2f])], Metadatas = [new Dictionary<string, object> { ["sv"] = new ChromaSparseVector([1], [0.5f]) }] });
		await ChromaBackup.BackupAsync(client, _path, new() { Collections = [name] });

		var error = Assert.ThrowsAsync<ChromaException>(() => ChromaBackup.RestoreAsync(second, _path));
		Assert.That(error!.Message, Does.Contain("sparse vector indexes of the keys sv"));
		Assert.That(await second.CollectionExistsAsync(name), Is.False);
	}

	static async Task<string> Filled(ChromaClient client)
	{
		var name = $"collection{Random.Shared.Next()}";
		await client.CreateCollectionAsync(new ChromaCollectionDefinition(name) { Metadata = new Dictionary<string, object> { ["owner"] = "tests" }, Configuration = new() { Space = ChromaSpace.Cosine } });
		await client.GetCollectionClient(name).AddAsync(new ChromaRecords(["a", "b", "c"])
		{
			Embeddings = [new([0.6f, 0.8f, 0f]), new([0f, 1f, 0f]), new([0.1f, 0.2f, 0.3f])],
			Metadatas = [new Dictionary<string, object> { ["i"] = 1L, ["f"] = 0.5 }, null, new Dictionary<string, object> { ["s"] = "x" }],
			Documents = ["a doc", null, "c doc"],
			Uris = ["ua", null, "uc"],
		});
		return name;
	}
}
