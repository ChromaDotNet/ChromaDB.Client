using System.Net;
using System.Text.Json;
using ChromaDB.Client.Models;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

// The quota of records per request of Chroma Cloud: it declares a max_batch_size of 1000, takes 300 records per write, and rejects
// more with 422 before writing any. Against a fake server with a quota of its own.
[TestFixture]
public class RecordsQuotaTests
{
	// The rejected batch and the rest go in batches of the quota, and the next writes start from it.
	[Test]
	public async Task BatchesOfTheQuotaAfterARejection()
	{
		var server = new Server(quota: 3);
		var client = Client(server, "http://localhost:8000");
		await client.Add(Records(0, 7));
		await client.Upsert(Records(7, 5));
		Assert.That(server.Requests, Is.EqualTo(new[] { ("add", 7), ("add", 3), ("add", 3), ("add", 1), ("upsert", 3), ("upsert", 2) }));
		Assert.That(server.Written, Is.EqualTo(Ids(0, 12)));
	}

	[Test]
	public async Task DeleteInBatchesOfTheQuota()
	{
		var server = new Server(quota: 3);
		await Client(server, "http://localhost:8000").Delete(Ids(0, 7));
		Assert.That(server.Requests, Is.EqualTo(new[] { ("delete", 7), ("delete", 3), ("delete", 3), ("delete", 1) }));
	}

	// One request, as asked: the rejection is the error.
	[Test]
	public async Task NoBatchesWithoutBatchSplitting()
	{
		var server = new Server(quota: 3);
		var client = new ChromaCollectionClient(Guid.Empty, "c", new ChromaConfigurationOptions("http://localhost:8000").WithBatchSplitting(false), new HttpClient(server));
		await Assert.ThatAsync(() => client.Add(Records(0, 7)), Throws.InstanceOf<ChromaException>().With.Message.Contains("exceeds limit of 3"));
		Assert.That(server.Requests, Is.EqualTo(new[] { ("add", 7) }));
	}

	// Another quota, like the keys of the metadata, is not about the number of records.
	[Test]
	public async Task OtherQuotasAreErrors()
	{
		var server = new Server(quota: 3, quotaName: "Number of metadata keys");
		await Assert.ThatAsync(() => Client(server, "http://localhost:8000").Add(Records(0, 7)), Throws.InstanceOf<ChromaException>());
		Assert.That(server.Requests, Is.EqualTo(new[] { ("add", 7) }));
	}

	// On Chroma Cloud the batches are of 300 records from the start, its quota by default.
	[Test]
	public async Task BatchesOf300OnChromaCloud()
	{
		var server = new Server(quota: 300);
		await Client(server, "https://api.trychroma.com").Add(Records(0, 301));
		Assert.That(server.Requests, Is.EqualTo(new[] { ("add", 300), ("add", 1) }));
	}

	static List<string> Ids(int start, int count) => Enumerable.Range(start, count).Select(i => $"r{i}").ToList();

	static ChromaRecords Records(int start, int count)
		=> new(Ids(start, count)) { Embeddings = Enumerable.Repeat(new ReadOnlyMemory<float>([1f, 0f]), count).ToList() };

	static ChromaCollectionClient Client(HttpMessageHandler handler, string uri)
		=> new(Guid.Empty, "c", new ChromaConfigurationOptions(uri), new HttpClient(handler));

	// Declares a max_batch_size of 1000 and rejects a write of more records than its quota, with the message of Chroma Cloud.
	sealed class Server(int quota, string quotaName = "Number of records") : HttpMessageHandler
	{
		public List<(string Action, int Records)> Requests { get; } = [];
		public List<string> Written { get; } = [];

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var path = request.RequestUri!.AbsolutePath;
			if (path.EndsWith("/pre-flight-checks"))
			{
				return Answer(HttpStatusCode.OK, """{"max_batch_size":1000,"supports_base64_encoding":false}""");
			}
			using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
			var ids = body.RootElement.GetProperty("ids").EnumerateArray().Select(x => x.GetString()!).ToList();
			var action = path[(path.LastIndexOf('/') + 1)..];
			Requests.Add((action, ids.Count));
			if (ids.Count > quota)
			{
				var name = char.ToUpperInvariant(action[0]) + action[1..];
				return Answer((HttpStatusCode)422, $$"""{"error":"ChromaError","message":"Quota exceeded: '{{quotaName}}' exceeded quota limit for action '{{name}}': current usage of {{ids.Count}} exceeds limit of {{quota}}"}""");
			}
			Written.AddRange(ids);
			return Answer(HttpStatusCode.OK, "{}");
		}

		static HttpResponseMessage Answer(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body) };
	}
}
