using System.Net;
using System.Text.Json;
using ChromaDB.Client.Models;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

// The requests of the Search API as the Python client of Chroma 1.5.9 sends them, and the answers of Chroma Cloud, against a fake server.
[TestFixture]
public class SearchRequestsTests
{
	const string CollectionPath = "/api/v2/tenants/default_tenant/databases/default_database/collections/11111111-2222-3333-4444-555555555555";
	const string Empty = """{"ids":[[]],"documents":[null],"embeddings":[null],"metadatas":[null],"scores":[null],"select":[[]]}""";

	[Test]
	public async Task SearchWithEverything()
	{
		var server = new FakeServer(_ => (HttpStatusCode.OK, Empty));
		await Client(server).SearchAsync(new ChromaSearch
		{
			Where = ChromaWhereOperator.GreaterThanOrEqual("year", 2021),
			WhereDocument = ChromaWhereDocumentOperator.Contains("apple"),
			Ids = ["a", "b"],
			Rank = ChromaRank.Knn(new([1f, 0f])),
			Limit = 3,
			Offset = 1,
			Select = [ChromaSearchKeys.Document, ChromaSearchKeys.Score, "category"],
			GroupBy = new ChromaSearchGroupBy(ChromaSearchAggregate.MinK(1, ChromaSearchKeys.Score), "category"),
		}, ChromaReadLevel.IndexOnly);
		Assert.That(server.Requests.Single().Line, Is.EqualTo($"POST {CollectionPath}/search"));
		Assert.That(server.Requests.Single().Body.GetRawText(), Is.EqualTo("""
			{"searches":[{"filter":{"$and":[{"year":{"$gte":2021}},{"#document":{"$contains":"apple"}},{"#id":{"$in":["a","b"]}}]},
			"rank":{"$knn":{"query":[1,0],"key":"#embedding","limit":16}},
			"group_by":{"keys":["category"],"aggregate":{"$min_k":{"keys":["#score"],"k":1}}},
			"limit":{"offset":1,"limit":3},"select":{"keys":["#document","#score","category"]}}],"read_level":"index_only"}
			""".Replace("\n", "").Replace("\t", "")));
	}

	// Like the Python client: no filter and no rank are null, no grouping is {}, the offset is always sent.
	[Test]
	public async Task SearchWithNothing()
	{
		var server = new FakeServer(_ => (HttpStatusCode.OK, Empty));
		await Client(server).SearchAsync(new ChromaSearch());
		Assert.That(server.Requests.Single().Body.GetRawText(), Is.EqualTo("""{"searches":[{"filter":null,"rank":null,"group_by":{},"limit":{"offset":0},"select":{"keys":[]}}]}"""));
	}

	// A single filter goes as it is; a document filter with & and | stays a tree on the #document key.
	[Test]
	public async Task SearchWithOneFilter()
	{
		var server = new FakeServer(_ => (HttpStatusCode.OK, Empty));
		await Client(server).SearchAsync(new ChromaSearch { WhereDocument = ChromaWhereDocumentOperator.Contains("a") | ChromaWhereDocumentOperator.Regex("^b") });
		Assert.That(server.Requests.Single().Body.GetProperty("searches")[0].GetProperty("filter").GetRawText(),
			Is.EqualTo("""{"$or":[{"#document":{"$contains":"a"}},{"#document":{"$regex":"^b"}}]}"""));
	}

	// The JSON the Python client of Chroma 1.5.9 builds for Rrf: -(1 / (60 + rank1) + 1 / (60 + rank2)), which Chroma Cloud accepts.
	[Test]
	public void Rrf()
	{
		var rrf = ChromaRank.Rrf([ChromaRank.Knn(new([1f, 0f]), returnRank: true), ChromaRank.Knn(new([0f, 1f]), returnRank: true)]);
		Assert.That(rrf.ToString(), Is.EqualTo("""
			{"$mul":[{"$val":-1},{"$sum":[
			{"$div":{"left":{"$val":1},"right":{"$sum":[{"$val":60},{"$knn":{"query":[1,0],"key":"#embedding","limit":16,"return_rank":true}}]}}},
			{"$div":{"left":{"$val":1},"right":{"$sum":[{"$val":60},{"$knn":{"query":[0,1],"key":"#embedding","limit":16,"return_rank":true}}]}}}]}]}
			""".Replace("\n", "").Replace("\t", "")));
	}

	[Test]
	public void RrfOfOneRankWithWeights()
	{
		var rrf = ChromaRank.Rrf([ChromaRank.Value(5)], k: 10, weights: [4], normalize: true);
		Assert.That(rrf.ToString(), Is.EqualTo("""{"$mul":[{"$val":-1},{"$div":{"left":{"$val":1},"right":{"$sum":[{"$val":10},{"$val":5}]}}}]}"""));
	}

	[Test]
	public void RrfThatChromaRejects()
	{
		Assert.That(() => ChromaRank.Rrf([]), Throws.ArgumentException);
		Assert.That(() => ChromaRank.Rrf([1], k: 0), Throws.InstanceOf<ArgumentOutOfRangeException>());
		Assert.That(() => ChromaRank.Rrf([1, 2], weights: [1]), Throws.ArgumentException);
		Assert.That(() => ChromaRank.Rrf([1], weights: [-1]), Throws.ArgumentException);
		Assert.That(() => ChromaRank.Rrf([1], weights: [0], normalize: true), Throws.ArgumentException);
	}

	// Sums and products are flattened as in the Python client; the other operators keep their two sides.
	[Test]
	public void RankOperators()
	{
		ChromaRank a = 1, b = 2, c = 3;
		Assert.That((a + b + c).ToString(), Is.EqualTo("""{"$sum":[{"$val":1},{"$val":2},{"$val":3}]}"""));
		Assert.That((a * (b * c)).ToString(), Is.EqualTo("""{"$mul":[{"$val":1},{"$val":2},{"$val":3}]}"""));
		Assert.That((a - b).ToString(), Is.EqualTo("""{"$sub":{"left":{"$val":1},"right":{"$val":2}}}"""));
		Assert.That((a / b).ToString(), Is.EqualTo("""{"$div":{"left":{"$val":1},"right":{"$val":2}}}"""));
		Assert.That((-a).ToString(), Is.EqualTo("""{"$mul":[{"$val":-1},{"$val":1}]}"""));
		Assert.That(ChromaRank.Max(a, b).ToString(), Is.EqualTo("""{"$max":[{"$val":1},{"$val":2}]}"""));
		Assert.That(ChromaRank.Min(a, b).ToString(), Is.EqualTo("""{"$min":[{"$val":1},{"$val":2}]}"""));
		Assert.That(ChromaRank.Abs(ChromaRank.Exp(ChromaRank.Log(a))).ToString(), Is.EqualTo("""{"$abs":{"$exp":{"$log":{"$val":1}}}}"""));
		Assert.That(ChromaRank.Knn(new([1f]), "sparse_key", limit: 5, defaultScore: 10).ToString(), Is.EqualTo("""{"$knn":{"query":[1],"key":"sparse_key","limit":5,"default":10}}"""));
	}

	// An answer of Chroma Cloud to two searches: the first selected document, score and one metadata field, the second only the ids.
	[Test]
	public async Task SearchResults()
	{
		var server = new FakeServer(_ => (HttpStatusCode.OK, """
			{"ids":[["a","d"],["c"]],"documents":[["apple pie","apple juice"],null],"embeddings":[[[1.0,0.0],null],null],
			"metadatas":[[{"category":"dessert"},{"category":"drink"}],null],"scores":[[0.0,0.5],null],"select":[["#document","#score","category"],[]]}
			"""));
		var results = await Client(server).SearchAsync([new ChromaSearch(), new ChromaSearch()]);
		Assert.That(results.Select(x => x.Select(e => e.Id)), Is.EqualTo(new[] { new[] { "a", "d" }, new[] { "c" } }));
		var first = results[0][0];
		Assert.That((first.Document, first.Score, first.Metadata!["category"]), Is.EqualTo(("apple pie", 0f, (object)"dessert")));
		Assert.That(first.Embedding!.Value.ToArray(), Is.EqualTo(new[] { 1f, 0f }));
		Assert.That(results[0][1].Embedding, Is.Null);
		var second = results[1][0];
		Assert.That((second.Document, second.Embedding, second.Metadata, second.Score), Is.EqualTo(((string?)null, (ReadOnlyMemory<float>?)null, (Dictionary<string, object>?)null, (float?)null)));
	}

	// What the Python client rejects, or what would ask the server for nothing: no request is sent.
	[Test]
	public async Task SearchThatIsRejected()
	{
		var server = new FakeServer(_ => (HttpStatusCode.OK, Empty));
		var client = Client(server);
		await Assert.ThatAsync(() => client.SearchAsync([]), Throws.ArgumentException);
		await Assert.ThatAsync(() => client.SearchAsync(new ChromaSearch { Offset = -1 }), Throws.InstanceOf<ArgumentOutOfRangeException>());
		await Assert.ThatAsync(() => client.SearchAsync(new ChromaSearch { Limit = 0 }), Throws.InstanceOf<ArgumentOutOfRangeException>());
		await Assert.ThatAsync(() => client.SearchAsync(new ChromaSearch { Ids = [] }), Throws.ArgumentException);
		Assert.That(server.Requests, Is.Empty);
		Assert.That(() => new ChromaSearchGroupBy(ChromaSearchAggregate.MinK(1, ChromaSearchKeys.Score)), Throws.ArgumentException);
		Assert.That(() => ChromaSearchAggregate.MaxK(0, ChromaSearchKeys.Score), Throws.InstanceOf<ArgumentOutOfRangeException>());
		Assert.That(() => ChromaSearchAggregate.MinK(1), Throws.ArgumentException);
	}

	static ChromaCollectionClient Client(HttpMessageHandler handler)
		=> new(new ChromaCollection("c") { Id = Guid.Parse("11111111-2222-3333-4444-555555555555") }, new ChromaConfigurationOptions("http://localhost:8000"), new HttpClient(handler));

	sealed record Request(string Line, JsonElement Body);

	sealed class FakeServer(Func<Request, (HttpStatusCode Status, string Body)> answer) : HttpMessageHandler
	{
		public List<Request> Requests { get; } = [];

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var text = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
			var recorded = new Request($"{request.Method.Method} {request.RequestUri!.PathAndQuery}", text is { Length: > 0 } ? JsonDocument.Parse(text).RootElement.Clone() : default);
			Requests.Add(recorded);
			var (status, response) = answer(recorded);
			return new HttpResponseMessage(status) { Content = new StringContent(response) };
		}
	}
}
