using System.Net;
using System.Text.Json;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

// Get with WithBatchSplitting, against a fake server that answers at most `cap` records, as Chroma Cloud answers at most 300 without
// an error: pages of the batch size, or the ids in batches.
[TestFixture]
public class GetPagesTests
{
	[Test]
	public async Task AllTheRecordsInPages()
	{
		var server = new Server(records: 7, cap: 3);
		var entries = await Client(server, 3).GetAsync();
		Assert.That(entries.Select(x => x.Id), Is.EqualTo(Ids(0, 7)));
		Assert.That(server.Pages, Is.EqualTo(new (int?, int?, int?)[] { (null, 3, 0), (null, 3, 3), (null, 3, 6) }));
	}

	// With WithBatchSplitting(false), one request, and the server leaves records out.
	[Test]
	public async Task OneRequestWithoutBatchSplitting()
	{
		var server = new Server(records: 7, cap: 3);
		var entries = await new ChromaCollectionClient(Guid.Empty, "c", new ChromaConfigurationOptions("http://localhost:8000").WithBatchSplitting(false), new HttpClient(server)).GetAsync();
		Assert.That(entries.Select(x => x.Id), Is.EqualTo(Ids(0, 3)));
		Assert.That(server.Pages, Is.EqualTo(new (int?, int?, int?)[] { (null, null, null) }));
	}

	// A full last page needs one more request, which comes back empty.
	[Test]
	public async Task FullLastPage()
	{
		var server = new Server(records: 6, cap: 3);
		Assert.That((await Client(server, 3).GetAsync()).Count, Is.EqualTo(6));
		Assert.That(server.Pages, Is.EqualTo(new (int?, int?, int?)[] { (null, 3, 0), (null, 3, 3), (null, 3, 6) }));
	}

	[Test]
	public async Task LimitAndOffsetInPages()
	{
		var server = new Server(records: 7, cap: 3);
		var entries = await Client(server, 3).GetAsync(limit: 5, offset: 1);
		Assert.That(entries.Select(x => x.Id), Is.EqualTo(Ids(1, 5)));
		Assert.That(server.Pages, Is.EqualTo(new (int?, int?, int?)[] { (null, 3, 1), (null, 2, 4) }));
	}

	// The ids in batches, each read whole; the limit and the offset apply to all of them.
	[Test]
	public async Task IdsInBatches()
	{
		var server = new Server(records: 7, cap: 3);
		var entries = await Client(server, 3).GetAsync(Ids(0, 7));
		Assert.That(entries.Select(x => x.Id), Is.EqualTo(Ids(0, 7)));
		Assert.That(server.Pages, Is.EqualTo(new (int?, int?, int?)[] { (3, null, null), (3, null, null), (1, null, null) }));

		server.Pages.Clear();
		entries = await Client(server, 3).GetAsync(Ids(0, 7), limit: 2, offset: 4);
		Assert.That(entries.Select(x => x.Id), Is.EqualTo(Ids(4, 2)));
		Assert.That(server.Pages, Has.Count.EqualTo(3));
	}

	// Within the batch size, the request is the one without WithBatchSplitting.
	[Test]
	public async Task SmallReadsInOneRequest()
	{
		var server = new Server(records: 7, cap: 3);
		await Client(server, 3).GetAsync(limit: 3, offset: 2);
		await Client(server, 3).GetAsync(Ids(0, 3));
		Assert.That(server.Pages, Is.EqualTo(new (int?, int?, int?)[] { (null, 3, 2), (3, null, null) }));
	}

	// With ids, the limit is never beyond them.
	[Test]
	public async Task LimitBeyondTheIds()
	{
		var server = new Server(records: 7, cap: 3);
		var entries = await Client(server, 3).GetAsync(Ids(0, 2), limit: 1000);
		Assert.That(entries.Select(x => x.Id), Is.EqualTo(Ids(0, 2)));
		Assert.That(server.Pages, Is.EqualTo(new (int?, int?, int?)[] { (2, 2, null) }));
	}

	// An id given twice comes back once, as in one request, also when the ids go in batches.
	[Test]
	public async Task RepeatedIdsInBatches()
	{
		var server = new Server(records: 7, cap: 3);
		var entries = await Client(server, 3).GetAsync(["r0", "r1", "r2", "r3", "r0"]);
		Assert.That(entries.Select(x => x.Id), Is.EqualTo(Ids(0, 4)));
	}

	static List<string> Ids(int start, int count) => Enumerable.Range(start, count).Select(i => $"r{i}").ToList();

	static ChromaCollectionClient Client(HttpMessageHandler handler, int maxBatchSize)
		=> new(Guid.Empty, "c", new ChromaConfigurationOptions("http://localhost:8000").WithBatchSplitting(maxBatchSize), new HttpClient(handler));

	// The records r0, r1, ... in order; each answer has at most `cap` of them. Pages records the ids count, the limit and the offset of each get.
	sealed class Server(int records, int cap) : HttpMessageHandler
	{
		public List<(int? Ids, int? Limit, int? Offset)> Pages { get; } = [];

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			if (request.RequestUri!.AbsolutePath.EndsWith("/pre-flight-checks"))
			{
				return Answer("""{"max_batch_size":1000}""");
			}
			using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
			var root = body.RootElement;
			int? Number(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;
			var ids = root.TryGetProperty("ids", out var list) && list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().Select(x => x.GetString()!).ToHashSet() : null;
			Pages.Add((ids?.Count, Number("limit"), Number("offset")));
			var selected = Ids(0, records).Where(id => ids is null || ids.Contains(id)).Skip(Number("offset") ?? 0).Take(Math.Min(Number("limit") ?? int.MaxValue, cap)).ToList();
			return Answer(JsonSerializer.Serialize(new { ids = selected, documents = (object?)null, metadatas = (object?)null, embeddings = (object?)null }));
		}

		static HttpResponseMessage Answer(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
	}
}
