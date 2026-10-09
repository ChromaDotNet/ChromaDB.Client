using System.Net;
using System.Text.Json;
using ChromaDB.Client.Models;
using ChromaDB.Client.Tests.Common;
using FsCheck;
using FsCheck.Fluent;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

// Writes of a random number of records, also with repeated ids, with random limits of the caller and of the server, against a fake
// server: the ids that arrive, one batch after the other, are the ones given and in their order; no batch goes over the smaller limit,
// and only the last one is not full; each record keeps its own fields.
[TestFixture]
public class WriteBatchPropertyTests
{
	[Test]
	public void TheBatchesAreTheRecordsInOrder()
		=> PropertyChecks.ForAll(Cases, write =>
		{
			var server = new Server(write.ServerLimit, write.Base64);
			var options = new ChromaConfigurationOptions("http://localhost:8000");
			var client = new ChromaCollectionClient(Guid.Empty, "c", write.CallerLimit is { } callerLimit ? options.WithBatchSplitting(callerLimit) : options, new HttpClient(server));
			var records = new ChromaRecords(write.Ids)
			{
				Embeddings = write.Ids.Select((_, i) => new ReadOnlyMemory<float>([i, -0.5f * i])).ToList(),
				Metadatas = write.Ids.Select((_, i) => (IReadOnlyDictionary<string, object>?)new Dictionary<string, object> { ["i"] = (long)i }).ToList(),
				Documents = write.Ids.Select((_, i) => $"d{i}").ToList(),
			};
			(write.Operation switch
			{
				"add" => client.AddAsync(records),
				"update" => client.UpdateAsync(records),
				"upsert" => client.UpsertAsync(records),
				_ => client.DeleteAsync(write.Ids),
			}).GetAwaiter().GetResult();

			var batches = server.Bodies.Where(x => x.Path == write.Operation).Select(x => x.Body).ToList();
			var limit = new[] { write.CallerLimit, write.ServerLimit }.Min();
			var ids = batches.SelectMany(x => x.GetProperty("ids").EnumerateArray().Select(id => id.GetString()!)).ToList();
			var sizes = batches.Select(x => x.GetProperty("ids").GetArrayLength()).ToList();
			// The fields of the records are numbered by their place among those given.
			var places = write.Operation == "delete" ? null : batches.SelectMany(Places).ToList();
			return (server.Bodies.All(x => x.Path == write.Operation)
				&& ids.SequenceEqual(write.Ids)
				&& sizes.All(size => size <= (limit ?? write.Ids.Count))
				&& sizes.Take(sizes.Count - 1).All(size => size == limit)
				&& (places is null || places.SequenceEqual(write.Ids.Select((_, i) => i))))
				.Label($"limit {limit?.ToString() ?? "none"}; batches: {string.Join(" ", batches.Select(x => x.GetRawText()))}");
		}, 300);

	// The place of each record of a batch, from each of its fields; -1 where they differ. A batch without records has no fields.
	static IEnumerable<int> Places(JsonElement batch)
	{
		if (batch.GetProperty("ids").GetArrayLength() == 0)
		{
			yield break;
		}
		var embeddings = batch.GetProperty("embeddings").EnumerateArray().Select(Floats).ToList();
		var metadatas = batch.GetProperty("metadatas").EnumerateArray().ToList();
		var documents = batch.GetProperty("documents").EnumerateArray().ToList();
		for (var j = 0; j < embeddings.Count; j++)
		{
			var place = (int)embeddings[j][0];
			yield return embeddings[j].SequenceEqual(new[] { place, -0.5f * place })
				&& metadatas[j].GetProperty("i").GetInt32() == place
				&& documents[j].GetString() == $"d{place}"
				? place
				: -1;
		}
	}

	// An embedding as a list of numbers, or as a base64 string of float32 values in little-endian order.
	internal static float[] Floats(JsonElement embedding)
		=> embedding.ValueKind == JsonValueKind.String
			? System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(Convert.FromBase64String(embedding.GetString()!)).ToArray()
			: embedding.EnumerateArray().Select(x => x.GetSingle() == 0 && x.GetRawText().StartsWith('-') ? -0f : x.GetSingle()).ToArray();

	internal sealed record Write(string Operation, List<string> Ids, int? CallerLimit, int? ServerLimit, bool Base64)
	{
		public override string ToString()
			=> $"{Operation} of {Ids.Count} [{string.Join(",", Ids)}], limit of the caller {CallerLimit?.ToString() ?? "none"}, of the server {ServerLimit?.ToString() ?? "none"}, base64 {Base64}";
	}

	static readonly Gen<int?> Limits = Gen.Frequency(
		(1, Gen.Constant((int?)null)),
		(4, Gen.Choose(1, 25).Select(x => (int?)x)));

	// The ids from a pool, small or large, so that some repeat. A delete without ids and filters is rejected before any request.
	static readonly Gen<Write> Cases =
		from operation in Gen.Elements("add", "update", "upsert", "delete")
		from pool in Gen.Choose(1, 80)
		from ids in Gen.Sized(size => Gen.Choose(operation == "delete" ? 1 : 0, Math.Max(1, size)).SelectMany(count => Gen.ListOf(Gen.Choose(0, pool - 1).Select(i => $"r{i}"), count)))
		from callerLimit in Limits
		from serverLimit in Limits
		from base64 in Gen.Elements(false, true)
		select new Write(operation, ids.ToList(), callerLimit, serverLimit, base64);

	// Declares its limit, or has no pre-flight-checks, like Chroma 0.4.10, and keeps the requests.
	sealed class Server(int? maxBatchSize, bool base64) : HttpMessageHandler
	{
		public List<(string Path, JsonElement Body)> Bodies { get; } = [];

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var path = request.RequestUri!.AbsolutePath.Split('/').Last();
			if (path == "pre-flight-checks")
			{
				return maxBatchSize is { } limit
					? Answer(HttpStatusCode.OK, $"{{\"max_batch_size\":{limit},\"supports_base64_encoding\":{(base64 ? "true" : "false")}}}")
					: Answer(HttpStatusCode.NotFound, """{"error":"NotFoundError","message":"Not Found"}""");
			}
			Bodies.Add((path, JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement.Clone()));
			return Answer(HttpStatusCode.OK, "{}");
		}

		static HttpResponseMessage Answer(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body) };
	}
}
