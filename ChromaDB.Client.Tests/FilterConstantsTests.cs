using System.Net;
using System.Text.Json;
using ChromaDB.Client.Models;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

// Chroma has no $not and no filter that matches every record or none: the client pushes a negation into the operators, sends no
// where for All, and no request for None.
[TestFixture]
public class FilterConstantsTests
{
	static readonly ChromaWhereOperator A = ChromaWhereOperator.Equal("a", 1);
	static readonly ChromaWhereOperator B = ChromaWhereOperator.Equal("b", 2);

	[Test]
	public void AllAndNoneAreSingleInstances()
	{
		Assert.That(ChromaWhereOperator.All, Is.SameAs(ChromaWhereOperator.All));
		Assert.That(ChromaWhereOperator.None, Is.SameAs(ChromaWhereOperator.None));
		Assert.That((ChromaWhereOperator.All.ToString(), ChromaWhereOperator.None.ToString()), Is.EqualTo(("true", "false")));
		Assert.That(() => ChromaWhereOperator.None.ToWhere(), Throws.InvalidOperationException);
		Assert.That(ChromaWhereOperator.ToRequestWhere(ChromaWhereOperator.All), Is.Null);
		Assert.That(ChromaWhereOperator.ToRequestWhere(null), Is.Null);
	}

	[Test]
	public void InAndNotInWithoutValues()
	{
		Assert.That(ChromaWhereOperator.In("k"), Is.SameAs(ChromaWhereOperator.None));
		Assert.That(ChromaWhereOperator.NotIn("k", []), Is.SameAs(ChromaWhereOperator.All));
	}

	[Test]
	public void AndAndOrWithAllAndNone()
	{
		var all = ChromaWhereOperator.All;
		var none = ChromaWhereOperator.None;
		Assert.That(A & none, Is.SameAs(none));
		Assert.That(none & A, Is.SameAs(none));
		Assert.That(all & A, Is.SameAs(A));
		Assert.That(A & all, Is.SameAs(A));
		Assert.That(A | all, Is.SameAs(all));
		Assert.That(all | A, Is.SameAs(all));
		Assert.That(none | A, Is.SameAs(A));
		Assert.That(A | none, Is.SameAs(A));
		Assert.That((A & B).ToString(), Is.EqualTo("""{"$and":[{"a":{"$eq":1}},{"b":{"$eq":2}}]}"""));
	}

	[TestCase("$eq", "$ne")]
	[TestCase("$ne", "$eq")]
	[TestCase("$gt", "$lte")]
	[TestCase("$gte", "$lt")]
	[TestCase("$lt", "$gte")]
	[TestCase("$lte", "$gt")]
	[TestCase("$in", "$nin")]
	[TestCase("$nin", "$in")]
	[TestCase("$contains", "$not_contains")]
	[TestCase("$not_contains", "$contains")]
	public void NotOfAComparison(string @operator, string negated)
	{
		var filter = @operator switch
		{
			"$eq" => ChromaWhereOperator.Equal("k", 1),
			"$ne" => ChromaWhereOperator.NotEqual("k", 1),
			"$gt" => ChromaWhereOperator.GreaterThan("k", 1),
			"$gte" => ChromaWhereOperator.GreaterThanOrEqual("k", 1),
			"$lt" => ChromaWhereOperator.LessThan("k", 1),
			"$lte" => ChromaWhereOperator.LessThanOrEqual("k", 1),
			"$in" => ChromaWhereOperator.In("k", 1),
			"$nin" => ChromaWhereOperator.NotIn("k", 1),
			"$contains" => ChromaWhereOperator.Contains("k", 1),
			_ => ChromaWhereOperator.NotContains("k", 1),
		};
		var value = @operator is "$in" or "$nin" ? "[1]" : "1";
		Assert.That(ChromaWhereOperator.Not(filter).ToString(), Is.EqualTo("{\"k\":{\"" + negated + "\":" + value + "}}"));
		Assert.That(ChromaWhereOperator.Not(ChromaWhereOperator.Not(filter)).ToString(), Is.EqualTo(filter.ToString()));
	}

	// !(a && (b || c)) is !a || (!b && !c); the negation of All is None, and of None All.
	[Test]
	public void NotOfAndAndOr()
	{
		var c = ChromaWhereOperator.Equal("c", 3);
		Assert.That(ChromaWhereOperator.Not(A & (B | c)).ToString(),
			Is.EqualTo("""{"$or":[{"a":{"$ne":1}},{"$and":[{"b":{"$ne":2}},{"c":{"$ne":3}}]}]}"""));
		Assert.That(ChromaWhereOperator.Not(ChromaWhereOperator.All), Is.SameAs(ChromaWhereOperator.None));
		Assert.That(ChromaWhereOperator.Not(ChromaWhereOperator.None), Is.SameAs(ChromaWhereOperator.All));
	}

	[Test]
	public void NotOfADocumentFilter()
	{
		var contains = ChromaWhereDocumentOperator.Contains("x");
		var regex = ChromaWhereDocumentOperator.Regex("^a");
		Assert.That(ChromaWhereDocumentOperator.Not(contains).ToString(), Is.EqualTo("""{"$not_contains":"x"}"""));
		Assert.That(ChromaWhereDocumentOperator.Not(ChromaWhereDocumentOperator.NotContains("x")).ToString(), Is.EqualTo("""{"$contains":"x"}"""));
		Assert.That(ChromaWhereDocumentOperator.Not(regex).ToString(), Is.EqualTo("""{"$not_regex":"^a"}"""));
		Assert.That(ChromaWhereDocumentOperator.Not(ChromaWhereDocumentOperator.NotRegex("^a")).ToString(), Is.EqualTo("""{"$regex":"^a"}"""));
		Assert.That(ChromaWhereDocumentOperator.Not(contains & regex).ToString(), Is.EqualTo("""{"$or":[{"$not_contains":"x"},{"$not_regex":"^a"}]}"""));
		Assert.That(ChromaWhereDocumentOperator.Not(contains | regex).ToString(), Is.EqualTo("""{"$and":[{"$not_contains":"x"},{"$not_regex":"^a"}]}"""));
	}

	[Test]
	public async Task NoneSendsNoRequest()
	{
		var server = new FakeServer();
		var client = Client(server);
		var none = ChromaWhereOperator.None;
		Assert.That(await client.GetAsync(where: none), Is.Empty);
		Assert.That(await client.GetAsync(["a"], where: none), Is.Empty);
		var query = await client.QueryAsync(new ChromaQuery([new([1f, 0f]), new([0f, 1f])]) { Where = none });
		Assert.That(query, Has.Count.EqualTo(2).And.All.Empty);
		await client.DeleteAsync(["a"], where: none);
		Assert.That(await client.DeleteAsync(new ChromaDelete { Where = none }), Is.EqualTo(0));
		var search = await client.SearchAsync([new ChromaSearch { Where = none }, new ChromaSearch { Where = none }]);
		Assert.That(search, Has.Count.EqualTo(2).And.All.Empty);
		Assert.That(server.Requests, Is.Empty);
	}

	// A search with None is left out of the request, and its results are empty in its place.
	[Test]
	public async Task SearchesWithNoneAmongOthers()
	{
		var server = new FakeServer();
		var results = await Client(server).SearchAsync([new ChromaSearch { Where = ChromaWhereOperator.None }, new ChromaSearch { Where = A }, new ChromaSearch { Where = ChromaWhereOperator.All }]);
		Assert.That(results.Select(x => x.Select(e => e.Id)), Is.EqualTo(new[] { new string[0], ["r0"], ["r1"] }));
		var searches = server.Requests.Single().Body.GetProperty("searches").EnumerateArray().ToList();
		Assert.That(searches, Has.Count.EqualTo(2));
		Assert.That(searches[0].GetProperty("filter").GetRawText(), Is.EqualTo("""{"a":{"$eq":1}}"""));
		Assert.That(searches[1].GetProperty("filter").ValueKind, Is.EqualTo(JsonValueKind.Null));
	}

	// All is no where clause; a delete needs ids or a filter, and All is not one.
	[Test]
	public async Task AllSendsNoWhere()
	{
		var server = new FakeServer();
		var client = Client(server);
		await client.GetAsync(["a"], where: ChromaWhereOperator.All);
		await client.QueryAsync(new ChromaQuery([new([1f, 0f])]) { Where = ChromaWhereOperator.All });
		await client.DeleteAsync(["a"], where: ChromaWhereOperator.All);
		await client.DeleteAsync(new ChromaDelete { Ids = ["a"], Where = ChromaWhereOperator.All });
		Assert.That(server.Requests.Select(x => x.Body.GetProperty("where").ValueKind), Has.All.EqualTo(JsonValueKind.Null));
		Assert.That(() => client.DeleteAsync(new ChromaDelete { Where = ChromaWhereOperator.All }), Throws.ArgumentException);
		Assert.That(() => client.DeleteAsync(new ChromaDelete { Ids = ["a"], Where = ChromaWhereOperator.All, Limit = 1 }), Throws.ArgumentException);
	}

	static ChromaCollectionClient Client(HttpMessageHandler handler)
		=> new(new ChromaCollection("c") { Id = Guid.NewGuid() }, new ChromaConfigurationOptions("http://localhost:8000").WithBatchSplitting(false), new HttpClient(handler));

	// Answers a search with one record per search sent, and the other requests with what they need; records the requests.
	sealed class FakeServer : HttpMessageHandler
	{
		public List<(string Path, JsonElement Body)> Requests { get; } = [];

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var path = request.RequestUri!.AbsolutePath;
			var body = request.Content is null ? default : JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken)).RootElement.Clone();
			Requests.Add((path, body));
			var answer = path.EndsWith("/search")
				? JsonSerializer.Serialize(new { ids = body.GetProperty("searches").EnumerateArray().Select((_, i) => new[] { $"r{i}" }) })
				: path.EndsWith("/query") ? """{"ids":[["a"]]}"""
				: path.EndsWith("/get") ? """{"ids":["a"]}"""
				: "{}";
			return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer) };
		}
	}
}
