using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using ChromaDB.Client.Backup;
using ChromaDB.Client.Backup.Common;
using ChromaDB.Client.Models;
using ChromaDB.Client.Tests.Common;
using FsCheck.Fluent;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

// The file of a backup without a server: the numbers that read back as the same bits, and the line of a record.
[TestFixture]
public class BackupFormatTests
{
	[Test]
	public void FloatsReadBackAsTheSameBits()
		=> PropertyChecks.ForAll(Gen.Choose(int.MinValue, int.MaxValue).Select(BitConverter.Int32BitsToSingle).Where(JsonNumbers.IsFinite),
			value => BitConverter.SingleToInt32Bits(float.Parse(JsonNumbers.Float(value), System.Globalization.CultureInfo.InvariantCulture)) == BitConverter.SingleToInt32Bits(value), 2000);

	[Test]
	public void DoublesReadBackAsTheSameBitsWithADecimalPoint()
		=> PropertyChecks.ForAll(
			from high in Gen.Choose(int.MinValue, int.MaxValue)
			from low in Gen.Choose(int.MinValue, int.MaxValue)
			let value = BitConverter.Int64BitsToDouble(((long)high << 32) | (uint)low)
			where JsonNumbers.IsFinite(value)
			select value,
			value => JsonNumbers.Double(value) is var text
				&& BitConverter.DoubleToInt64Bits(double.Parse(text, System.Globalization.CultureInfo.InvariantCulture)) == BitConverter.DoubleToInt64Bits(value)
				&& text.IndexOfAny(['.', 'E', 'e']) >= 0, 2000);

	[TestCase(2.0, "2.0")]
	[TestCase(-0.0, "-0.0")]
	[TestCase(0.1, "0.1")]
	[TestCase(1e20, "1E+20")]
	public void DoubleText(double value, string text) => Assert.That(JsonNumbers.Double(value), Is.EqualTo(text));

	[Test]
	public void NegativeZeroKeepsItsSign() => Assert.That(JsonNumbers.Float(-0f), Is.EqualTo("-0"));

	[Test]
	public void ARecordReadsBackTheSame()
	{
		var entry = new ChromaCollectionEntry("a")
		{
			Embedding = new float[] { 0.1f, -0f, float.Epsilon, 3.4e38f, 1f },
			Metadata = new Dictionary<string, object>
			{
				["s"] = "text é \"quoted\"",
				["i"] = 1L,
				["f"] = 1.0,
				["b"] = true,
				["l"] = new List<object> { 1L, 2L },
				["sv"] = new ChromaSparseVector([1, 5], [0.5f, 0.1f], ["x", "y"]),
			},
			Document = "doc",
			Uri = "file:///a",
		};
		var text = Line(entry, out var embeddingWritten);
		Assert.That(embeddingWritten, Is.True);
		Assert.That(text, Does.StartWith("""{"id":"a","metadata":{"s":"text é \"quoted\"","i":1,"f":1.0,"b":true,"l":[1,2],"sv":{"#type":"sparse_vector","indices":[1,5],"values":[0.5,0.1],"tokens":["x","y"]}},"embedding":[0.1,-0,1E-45,3.4E+38,1],"text_chunk":"doc","uri":"file:///a"}"""));
		var record = RecordJson.Read(JsonDocument.Parse(text).RootElement);
		Assert.That(Compare.Difference(record, entry), Is.Null);
		Assert.That(record.Embedding!.Select(BitConverter.SingleToInt32Bits), Is.EqualTo(entry.Embedding.Value.ToArray().Select(BitConverter.SingleToInt32Bits)));
		// An integer reads back as a long, not as a double.
		Assert.That(record.Metadata!["i"], Is.TypeOf<long>());
		Assert.That(record.Metadata["f"], Is.TypeOf<double>());
	}

	// The names of the lines of Chroma Data Pipes: id, metadata, embedding, text_chunk; a record without metadata has null, as the server sends it.
	[Test]
	public void ARecordWithoutMetadataOrDocument()
	{
		var text = Line(new ChromaCollectionEntry("b") { Embedding = new float[] { 1f }, Metadata = new Dictionary<string, object>() }, out _);
		Assert.That(text, Is.EqualTo("""{"id":"b","metadata":null,"embedding":[1],"text_chunk":null,"uri":null}"""));
	}

	// Chroma 1.x sends null for a float of an embedding that it cannot write, which the client reads as NaN: the line has no embedding.
	[Test]
	public void AnEmbeddingWithoutAFloatGoesAsNull()
	{
		var text = Line(new ChromaCollectionEntry("c") { Embedding = new float[] { float.NaN, 1f } }, out var embeddingWritten);
		Assert.That(embeddingWritten, Is.False);
		Assert.That(text, Does.Contain("\"embedding\":null"));
	}

	static string Line(ChromaCollectionEntry entry, out bool embeddingWritten)
	{
		using var stream = new MemoryStream();
		using (var writer = new Utf8JsonWriter(stream, RecordJson.WriterOptions))
		{
			embeddingWritten = RecordJson.Write(writer, entry);
		}
		return Encoding.UTF8.GetString(stream.ToArray());
	}
}

// A backup against a fake server: the pages, their order, the counts, the waits and the errors.
[TestFixture]
public class BackupFakeServerTests
{
	string _path = null!;

	[SetUp]
	public void SetUp() => _path = Path.Combine(Path.GetTempPath(), $"chromadotnet-backup-test-{Guid.NewGuid():N}.zip");

	[TearDown]
	public void TearDown()
	{
		File.Delete(_path);
		File.Delete(_path + ".partial");
	}

	[Test]
	public async Task PagesInTheOrderOfTheCollectionUntilOneIsNotFull()
	{
		var server = new FakeChroma(250);
		var result = await ChromaBackup.BackupAsync(Client(server), _path, new() { BatchSize = 100, Concurrency = 3 });
		Assert.That(result.Collections.Single().Records, Is.EqualTo(250));
		Assert.That(result.ChangedDuringBackup, Is.False);
		Assert.That(Records(_path).Select(x => x.Id), Is.EqualTo(Enumerable.Range(0, 250).Select(FakeChroma.Id)));
		Assert.That(server.Offsets.Take(3), Is.EqualTo(new[] { 0, 100, 200 }));
		var manifest = Manifest(_path);
		Assert.That(manifest.GetProperty("format").GetString(), Is.EqualTo("chromadotnet-backup"));
		Assert.That(manifest.GetProperty("server").GetProperty("version").GetString(), Is.EqualTo("1.5.9"));
		Assert.That(manifest.GetProperty("changed_during_backup").GetBoolean(), Is.False);
		Assert.That(File.Exists(_path + ".partial"), Is.False);
	}

	[Test]
	public async Task ACountThatChangesSaysTheServerTookWrites()
	{
		var server = new FakeChroma(250) { CountAtEnd = 251 };
		var result = await ChromaBackup.BackupAsync(Client(server), _path);
		Assert.That(result.ChangedDuringBackup, Is.True);
		Assert.That(Manifest(_path).GetProperty("changed_during_backup").GetBoolean(), Is.True);
	}

	// A page that gives again an id of a page already read, as when the server takes writes: the file keeps the id once.
	[Test]
	public async Task AnIdGivenAgainGoesOnce()
	{
		var server = new FakeChroma(150) { RepeatInSecondPage = 99 };
		var result = await ChromaBackup.BackupAsync(Client(server), _path, new() { BatchSize = 100 });
		var collection = result.Collections.Single();
		Assert.That(collection.DuplicateIds, Is.EqualTo(1));
		Assert.That(collection.ChangedDuringBackup, Is.True);
		Assert.That(Records(_path).Count(x => x.Id == FakeChroma.Id(99)), Is.EqualTo(1));
	}

	// As the copy command of the CLI: a 429 waits, as long as its Retry-After says from 0.5 to 60 s, and the get goes again. The client
	// tries a read 4 times first: the fifth refusal reaches the backup, which waits once. One get at a time, so that the refusals all
	// go to the same get.
	[Test]
	public async Task A429WaitsAndTheGetGoesAgain()
	{
		var server = new FakeChroma(50) { RefusalsBeforeFirstGet = 5 };
		var messages = new List<string>();
		var progress = new SynchronousProgress(x => { if (x.Message is { } m) messages.Add(m); });
		var result = await ChromaBackup.BackupAsync(Client(server), _path, new() { Concurrency = 1 }, progress);
		Assert.That(result.Collections.Single().Records, Is.EqualTo(50));
		Assert.That(messages, Has.Count.EqualTo(1).And.All.Contains("429"));
	}

	// A 400 is not something waiting fixes: the backup fails, and leaves no file.
	[Test]
	public void A400Fails()
	{
		var server = new FakeChroma(50) { GetStatus = HttpStatusCode.BadRequest };
		Assert.That(() => ChromaBackup.BackupAsync(Client(server), _path), Throws.InstanceOf<ChromaException>());
		Assert.That(File.Exists(_path), Is.False);
		Assert.That(File.Exists(_path + ".partial"), Is.False);
	}

	[Test]
	public void AFileThatExistsStays()
	{
		File.WriteAllText(_path, "mine");
		Assert.That(() => ChromaBackup.BackupAsync(Client(new FakeChroma(1)), _path), Throws.InstanceOf<IOException>());
		Assert.That(File.ReadAllText(_path), Is.EqualTo("mine"));
	}

	// The records of a backup that do not match the digest of its manifest: a damaged file, which a verification refuses.
	[Test]
	public async Task ADamagedFileIsRefused()
	{
		var server = new FakeChroma(20);
		await ChromaBackup.BackupAsync(Client(server), _path);
		using (var zip = ZipFile.Open(_path, ZipArchiveMode.Update))
		{
			var entry = zip.Entries.Single(x => x.FullName.EndsWith("records.jsonl"));
			string text;
			using (var reader = new StreamReader(entry.Open()))
			{
				text = reader.ReadToEnd();
			}
			entry.Delete();
			using var writer = new StreamWriter(zip.CreateEntry("collections/0001/records.jsonl").Open());
			writer.Write(text.Replace("\"d1\"", "\"dX\""));
		}
		Assert.That(() => ChromaBackup.VerifyAsync(Client(server), _path), Throws.InstanceOf<ChromaBackupFormatException>().With.Message.Contains("digest"));
	}

	[Test]
	public async Task AVerificationOfTheSameRecords()
	{
		var server = new FakeChroma(120);
		await ChromaBackup.BackupAsync(Client(server), _path);
		var verify = await ChromaBackup.VerifyAsync(Client(server), _path);
		Assert.That(verify.Same, Is.True);
		Assert.That(verify.Collections.Single().SameRecords, Is.EqualTo(120));
	}

	static List<BackupRecord> Records(string path)
	{
		using var zip = ZipFile.OpenRead(path);
		var entry = BackupFile.ReadManifest(zip).Single();
		return BackupFile.ReadRecords(zip, entry).ToList();
	}

	static JsonElement Manifest(string path)
	{
		using var zip = ZipFile.OpenRead(path);
		using var stream = zip.GetEntry("manifest.json")!.Open();
		return JsonDocument.Parse(stream).RootElement.Clone();
	}

	static ChromaClient Client(HttpMessageHandler handler) => new(new ChromaConfigurationOptions("http://localhost:8000"), new HttpClient(handler));

	sealed class SynchronousProgress(Action<ChromaBackupProgress> report) : IProgress<ChromaBackupProgress>
	{
		public void Report(ChromaBackupProgress value) => report(value);
	}

	// A Chroma 1.5.9 with one collection, c, of the given records: r000 with embedding [0, 0.5, -0], metadata {"i": 0}, document d0
	// and URI u0, and so on. Records the offsets of the gets.
	internal sealed class FakeChroma(int records) : HttpMessageHandler
	{
		const string CollectionId = "11111111-2222-3333-4444-555555555555";
		readonly object _lock = new();
		int _counts;

		public int? CountAtEnd { get; init; }
		public int? RepeatInSecondPage { get; init; }
		public int RefusalsBeforeFirstGet { get; set; }
		public HttpStatusCode? GetStatus { get; init; }
		public List<int> Offsets { get; } = [];

		public static string Id(int i) => $"r{i:000}";

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var path = request.RequestUri!.AbsolutePath;
			var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
			if (path.EndsWith("/version"))
			{
				return Json("\"1.5.9\"");
			}
			if (path.EndsWith("/pre-flight-checks"))
			{
				return Json("""{"max_batch_size":5461,"supports_base64_encoding":true}""");
			}
			if (path.EndsWith("/count"))
			{
				lock (_lock)
				{
					return Json((_counts++ > 0 && CountAtEnd is { } end ? end : records).ToString());
				}
			}
			if (path.EndsWith("/get") && request.Method == HttpMethod.Post)
			{
				if (GetStatus is { } status)
				{
					return new HttpResponseMessage(status) { Content = new StringContent("""{"error":"InvalidArgumentError","message":"refused"}""") };
				}
				lock (_lock)
				{
					if (RefusalsBeforeFirstGet > 0)
					{
						RefusalsBeforeFirstGet--;
						var refused = new HttpResponseMessage((HttpStatusCode)429) { Content = new StringContent("""{"error":"TooManyRequests","message":"slow down"}""") };
						refused.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
						return refused;
					}
				}
				var json = JsonDocument.Parse(body!).RootElement;
				if (json.TryGetProperty("ids", out var ids) && ids.ValueKind == JsonValueKind.Array)
				{
					return Json(Page(ids.EnumerateArray().Select(x => int.Parse(x.GetString()![1..])).Where(i => i < records)));
				}
				var limit = json.GetProperty("limit").GetInt32();
				var offset = json.TryGetProperty("offset", out var o) && o.ValueKind == JsonValueKind.Number ? o.GetInt32() : 0;
				lock (_lock)
				{
					Offsets.Add(offset);
				}
				var page = Enumerable.Range(offset, Math.Max(0, Math.Min(limit, records - offset))).ToList();
				if (RepeatInSecondPage is { } repeated && offset > 0 && offset <= repeated + limit && page.Count > 0)
				{
					page[0] = repeated;
				}
				return Json(Page(page));
			}
			if (path.EndsWith("/collections") && request.Method == HttpMethod.Get)
			{
				return Json($"[{Collection}]");
			}
			if (path.Contains("/collections/") && request.Method == HttpMethod.Get)
			{
				return Json(Collection);
			}
			return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("""{"error":"NotFoundError","message":"not found"}""") };
		}

		static string Collection
			=> """{"id":""" + "\"" + CollectionId + "\"" + ""","name":"c","metadata":{"owner":"tests"},"dimension":3,"tenant":"default_tenant","database":"default_database","configuration_json":{"hnsw":{"space":"l2"}},"schema":{"defaults":{},"keys":{}}}""";

		static string Page(IEnumerable<int> indexes)
		{
			var list = indexes.ToList();
			return JsonSerializer.Serialize(new
			{
				ids = list.Select(Id),
				embeddings = list.Select(i => new[] { (float)i, 0.5f, -0f }),
				metadatas = list.Select(i => new Dictionary<string, int> { ["i"] = i }),
				documents = list.Select(i => $"d{i}"),
				uris = list.Select(i => $"u{i}"),
				include = new[] { "embeddings", "metadatas", "documents", "uris" },
			});
		}

		static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
	}
}

// A backup of a collection of the server, read back and compared with it.
[TestFixture]
public class BackupServerTests : ChromaTestsBase
{
	string _path = null!;

	[SetUp]
	public void SetUp() => _path = Path.Combine(Path.GetTempPath(), $"chromadotnet-backup-test-{Guid.NewGuid():N}.zip");

	[TearDown]
	public void TearDown() => File.Delete(_path);

	[Test]
	public async Task TheBackupHoldsTheCollectionAsItIs()
	{
		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		var collection = await client.CreateCollectionAsync(new ChromaCollectionDefinition($"collection{Random.Shared.Next()}") { Metadata = new Dictionary<string, object> { ["owner"] = "tests", ["n"] = 2L } });
		var collectionClient = client.GetCollectionClient(collection);
		await collectionClient.AddAsync(Records());

		var result = await ChromaBackup.BackupAsync(client, _path, new() { Collections = [collection.Name], BatchSize = 2 });
		Assert.That(result.Collections.Single().Records, Is.EqualTo(5));
		Assert.That(result.ChangedDuringBackup, Is.False);

		var onServer = (await collectionClient.GetAsync(include: ChromaGetInclude.Embeddings | ChromaGetInclude.Metadatas | ChromaGetInclude.Documents | ChromaGetInclude.Uris)).ToDictionary(x => x.Id);
		using (var zip = ZipFile.OpenRead(_path))
		{
			var entry = BackupFile.ReadManifest(zip).Single();
			var saved = BackupFile.ReadCollection(zip, entry);
			var read = await client.GetCollectionAsync(collection.Name);
			Assert.That(saved.Name, Is.EqualTo(collection.Name));
			Assert.That(Compare.SameMetadata(saved.Metadata, read.Metadata), Is.True);
			Assert.That(saved.SchemaJson is null ? read.SchemaJson is null : Compare.SameJson(saved.SchemaJson.Value, read.SchemaJson!.Value), Is.True);
			var records = BackupFile.ReadRecords(zip, entry).ToList();
			Assert.That(records.Select(x => x.Id), Is.EquivalentTo(onServer.Keys));
			foreach (var record in records)
			{
				var server = onServer[record.Id];
				Assert.That(Compare.Difference(record, server), Is.Null, record.Id);
				Assert.That(record.Embedding!.Select(BitConverter.SingleToInt32Bits), Is.EqualTo(server.Embedding!.Value.ToArray().Select(BitConverter.SingleToInt32Bits)), record.Id);
			}
		}

		var verify = await ChromaBackup.VerifyAsync(client, _path);
		Assert.That(verify.Same, Is.True, verify.Collections.Single().FirstDifference);
	}

	// After the backup: a document changed, a record deleted and one added on the server.
	[Test]
	public async Task AVerificationTellsWhatChanged()
	{
		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		var collection = await client.CreateCollectionAsync($"collection{Random.Shared.Next()}");
		var collectionClient = client.GetCollectionClient(collection);
		await collectionClient.AddAsync(Records());
		await ChromaBackup.BackupAsync(client, _path, new() { Collections = [collection.Name] });

		await collectionClient.UpdateAsync(new ChromaRecords(["r1"]) { Documents = ["changed"] });
		await collectionClient.DeleteAsync(["r2"]);
		await collectionClient.AddAsync(new ChromaRecords(["r9"]) { Embeddings = [new([9f, 9f, 9f])] });

		var verify = (await ChromaBackup.VerifyAsync(client, _path)).Collections.Single();
		Assert.That(verify.Same, Is.False);
		Assert.That((verify.SameRecords, verify.Different, verify.Missing, verify.OnlyOnServer), Is.EqualTo((3L, 1L, 1L, 1L)));
		Assert.That(verify.FirstDifference, Is.EqualTo("r1: document"));
	}

	[Test]
	public async Task SparseVectorsInTheMetadata()
	{
		Assume.That(SparseVectorIndexesSupported, Is.True, "Distributed Chroma, Chroma Cloud and VecturaDB take sparse vectors.");
		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		var collection = await client.CreateCollectionAsync(new ChromaCollectionDefinition($"collection{Random.Shared.Next()}") { Schema = new ChromaCollectionSchema().WithSparseVectorIndex("sv") });
		var collectionClient = client.GetCollectionClient(collection);
		await collectionClient.AddAsync(new ChromaRecords(["a", "b"])
		{
			Embeddings = [new([1f, 2f, 3f]), new([4f, 5f, 6f])],
			Metadatas = [new Dictionary<string, object> { ["sv"] = new ChromaSparseVector([1, 7], [0.25f, 0.1f]) }, new Dictionary<string, object> { ["sv"] = new ChromaSparseVector([3], [2f]) }],
		});
		await ChromaBackup.BackupAsync(client, _path, new() { Collections = [collection.Name] });
		using (var zip = ZipFile.OpenRead(_path))
		{
			var records = BackupFile.ReadRecords(zip, BackupFile.ReadManifest(zip).Single()).ToDictionary(x => x.Id);
			Assert.That(((ChromaSparseVector)records["a"].Metadata!["sv"]).Values, Is.EqualTo(new[] { 0.25f, 0.1f }));
		}
		Assert.That((await ChromaBackup.VerifyAsync(client, _path)).Same, Is.True);
	}

	// Embeddings at the edges of a float, every type of metadata the server keeps, a record without a document, and URIs.
	ChromaRecords Records()
	{
		var metadata = new List<IReadOnlyDictionary<string, object>?>
		{
			new Dictionary<string, object> { ["s"] = "one", ["i"] = 1L, ["f"] = 1.5, ["b"] = true },
			new Dictionary<string, object> { ["i"] = -9007199254740993L, ["f"] = 2.0 },
			null,
			new Dictionary<string, object> { ["s"] = "é ☃ \"q\"" },
			new Dictionary<string, object> { ["f"] = 0.1 },
		};
		if (MetadataListsSupported)
		{
			metadata[4] = new Dictionary<string, object> { ["f"] = 0.1, ["l"] = new List<object> { 1L, 2L } };
		}
		return new ChromaRecords(["r0", "r1", "r2", "r3", "r4"])
		{
			Embeddings = [new([0.1f, -0f, 1e-38f]), new([3.4e38f, -3.4e38f, 1f]), new([1f / 3, 2f / 3, 1f]), new([float.Epsilon, 0f, 1f]), new([123456.789f, -0.000001f, 7f])],
			Metadatas = metadata,
			Documents = ["d0", null, "d2", "d3", "d4"],
			Uris = UrisSupported ? ["u0", "u1", null, "u3", "u4"] : null,
		};
	}
}
