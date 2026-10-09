using System.Globalization;
using System.Text.Json;
using ChromaDB.Client.Models;
using ChromaDB.Client.Tests.Common;
using FsCheck;
using FsCheck.Fluent;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

// SkipUnchangedEmbeddings on random records, against a fake server: a record is updated without its embedding if and only if its id is
// given once and stored, and its embedding is the one stored, each number equal, +0 and -0 too, or in a cosine collection within 4 ulp
// with the same sign. The others, and the ids given more than once, go by the upsert or the update of the server. The embeddings are
// read once for each id; each record goes once, with its own fields, in the order given.
[TestFixture]
public class UpsertStrategyPropertyTests
{
	[Test]
	public void UnchangedEmbeddingsGoWithoutThem()
		=> PropertyChecks.ForAll(Cases, write =>
		{
			var server = new UpsertStrategyTests.Server();
			foreach (var record in write.Records.DistinctBy(x => x.Id).Where(x => x.Stored is not null))
			{
				server.Store(record.Id, record.Stored!, """{"i":-1}""", "stored");
			}
			var collection = new ChromaCollection("c") { Id = Guid.Empty, Metadata = new Dictionary<string, object> { ["hnsw:space"] = write.Cosine ? "cosine" : "l2" } };
			var options = new ChromaConfigurationOptions("http://localhost:8000").WithUpsertStrategy(ChromaUpsertStrategy.SkipUnchangedEmbeddings);
			var client = new ChromaCollectionClient(collection, write.Limit is { } limit ? options.WithBatchSplitting(limit) : options, new HttpClient(server));
			var records = new ChromaRecords(write.Records.Select(x => x.Id).ToList())
			{
				Embeddings = write.Records.Select(x => new ReadOnlyMemory<float>(x.Given)).ToList(),
				Metadatas = write.Records.Select((_, i) => (IReadOnlyDictionary<string, object>?)new Dictionary<string, object> { ["i"] = (long)i }).ToList(),
				Documents = write.Records.Select((_, i) => $"d{i}").ToList(),
			};
			(write.Upsert ? client.UpsertAsync(records) : client.UpdateAsync(records)).GetAwaiter().GetResult();

			var expected = Expected(write);
			var actual = server.Bodies.Select(x => Request(x.Path, x.Body)).ToList();
			return actual.SequenceEqual(expected).Label($"expected:\n{string.Join("\n", expected)}\nsent:\n{string.Join("\n", actual)}");
		}, 300);

	// The requests the strategy sends: the gets of the embeddings, then the update of the unchanged records without them, then the others,
	// in batches of the limit of the caller, or of the max_batch_size of 100 that the fake server declares.
	static List<string> Expected(Case write)
	{
		var size = write.Limit ?? 100;
		var repeated = write.Records.GroupBy(x => x.Id).Where(x => x.Count() > 1).Select(x => x.Key).ToHashSet();
		var places = Enumerable.Range(0, write.Records.Count).ToList();
		var unchanged = places.Where(i => write.Records[i] is var x && !repeated.Contains(x.Id) && x.Stored is not null && (write.Cosine ? x.SameInCosine : x.SameInL2)).ToList();
		var others = places.Except(unchanged).ToList();
		return
		[
			.. write.Records.Select(x => x.Id).Distinct().Chunk(size).Select(ids => $"get {string.Join(",", ids)} include embeddings"),
			.. unchanged.Chunk(size).Select(batch => Write("update", batch, write, embeddings: false)),
			.. others.Chunk(size).Select(batch => Write(write.Upsert ? "upsert" : "update", batch, write, embeddings: true)),
		];
	}

	static string Write(string path, int[] places, Case write, bool embeddings)
		=> $"{path} {string.Join(",", places.Select(i => write.Records[i].Id))} documents {string.Join(",", places.Select(i => $"d{i}"))}"
			+ $" metadatas {string.Join(",", places)} embeddings {(embeddings ? string.Join(";", places.Select(i => Bits(write.Records[i].Given))) : "none")}";

	static string Request(string path, JsonElement body)
	{
		var ids = string.Join(",", body.GetProperty("ids").EnumerateArray().Select(x => x.GetString()));
		if (path == "get")
		{
			return $"get {ids} include {string.Join(",", body.GetProperty("include").EnumerateArray().Select(x => x.GetString()))}";
		}
		var embeddings = body.TryGetProperty("embeddings", out var given) && given.ValueKind != JsonValueKind.Null
			? string.Join(";", given.EnumerateArray().Select(x => Bits(WriteBatchPropertyTests.Floats(x))))
			: "none";
		return $"{path} {ids} documents {string.Join(",", body.GetProperty("documents").EnumerateArray().Select(x => x.GetString()))}"
			+ $" metadatas {string.Join(",", body.GetProperty("metadatas").EnumerateArray().Select(x => x.GetProperty("i").GetInt64()))} embeddings {embeddings}";
	}

	static string Bits(float[] embedding) => string.Join(" ", embedding.Select(x => BitConverter.SingleToInt32Bits(x).ToString("x8", CultureInfo.InvariantCulture)));

	// A record written: the embedding stored for its id, if any, and the one given; whether they are the same in an l2 and in a cosine
	// collection, as the generator made them. The first record of an id says what is stored.
	internal sealed record Record(string Id, float[]? Stored, float[] Given, bool SameInL2, bool SameInCosine)
	{
		public override string ToString()
			=> $"{Id}: stored {(Stored is null ? "none" : Numbers(Stored))}, given {Numbers(Given)}, same in l2 {SameInL2}, in cosine {SameInCosine}";
	}

	internal sealed record Case(bool Upsert, bool Cosine, int? Limit, List<Record> Records)
	{
		public override string ToString()
			=> $"{(Upsert ? "upsert" : "update")} in a {(Cosine ? "cosine" : "l2")} collection, batches of {Limit?.ToString(CultureInfo.InvariantCulture) ?? "100"}:\n{string.Join("\n", Records)}";
	}

	static string Numbers(float[] embedding) => $"[{string.Join(", ", embedding.Select(x => x.ToString("R", CultureInfo.InvariantCulture)))}] ({Bits(embedding)})";

	static float Ulps(float value, int ulps) => BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(value) + ulps);

	// Numbers far from zero, so that a few ulp keep the sign.
	static readonly Gen<float> NonZero =
		from magnitude in Gen.Choose(1, 1_000_000)
		from sign in Gen.Elements(1f, -1f)
		select sign * magnitude / 997f;

	static readonly Gen<float> Zeros = Gen.Elements(0f, -0f);

	// A number stored, the one written, and whether they are the same in an l2 and in a cosine collection.
	static readonly Gen<(float Stored, float Given, bool SameInL2, bool SameInCosine)> Pairs = Gen.Frequency(
		(8, NonZero.Select(x => (x, x, true, true))),
		(1, Zeros.Select(x => (x, x, true, true))),
		(1, Zeros.Select(x => (x, -x, true, true))),
		(3, from x in NonZero from ulps in Gen.Choose(-8, 8).Where(k => k != 0) select (x, Ulps(x, ulps), false, Math.Abs(ulps) <= 4)),
		(1, from x in Zeros from ulps in Gen.Choose(1, 8) select (x, Ulps(x, ulps), false, ulps <= 4)),
		(1, from x in Zeros from ulps in Gen.Choose(1, 4) select (x, Ulps(-x, ulps), false, false)),
		(1, NonZero.Select(x => (x, -x, false, false))),
		(1, NonZero.Select(x => (x, x * 2, false, false))));

	// A stored embedding, of the same dimension or of one more, or none.
	static Gen<Record> Records(int pool, int dimension)
		=> from id in Gen.Choose(0, pool - 1).Select(i => $"r{i}")
			from pairs in Gen.ArrayOf(Pairs, dimension)
			from kind in Gen.Frequency((6, Gen.Constant("stored")), (1, Gen.Constant("longer")), (2, Gen.Constant("new")))
			select kind switch
			{
				"stored" => new Record(id, pairs.Select(x => x.Stored).ToArray(), pairs.Select(x => x.Given).ToArray(), pairs.All(x => x.SameInL2), pairs.All(x => x.SameInCosine)),
				"longer" => new Record(id, [.. pairs.Select(x => x.Stored), 1f], pairs.Select(x => x.Given).ToArray(), false, false),
				_ => new Record(id, null, pairs.Select(x => x.Given).ToArray(), false, false),
			};

	static readonly Gen<Case> Cases =
		from upsert in Gen.Elements(true, false)
		from cosine in Gen.Elements(true, false)
		from limit in Gen.Frequency((1, Gen.Constant((int?)null)), (3, Gen.Choose(1, 6).Select(x => (int?)x)))
		from pool in Gen.Choose(1, 12)
		from dimension in Gen.Choose(1, 4)
		from records in Gen.Sized(size => Gen.Choose(0, Math.Max(1, size / 4)).SelectMany(count => Gen.ListOf(Records(pool, dimension), count)))
		select new Case(upsert, cosine, limit, records.ToList());
}
