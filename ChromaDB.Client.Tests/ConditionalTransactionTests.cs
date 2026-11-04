using System.Net;
using System.Text.Json;
using ChromaDB.Client.Models;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

// The conditional transactions against a fake server, as the Python and JavaScript clients of Chroma make them: the reads with the
// read token of the first one, the writes in one commit as they would go outside a transaction, the checks of the client before any
// request, and the runs again of RunConditionalTransactionAsync.
[TestFixture]
public class ConditionalTransactionTests
{
	static readonly Guid Id = Guid.Parse("11111111-2222-3333-4444-555555555555");
	const string CollectionPath = "/api/v2/tenants/default_tenant/databases/default_database/collections/11111111-2222-3333-4444-555555555555";
	const string Conflict = """{"error":"ConditionalWriteConflictError","message":"conditional write conflict"}""";
	const string StaleRead = """{"error":"StaleReadError","message":"stale read: the records changed after the first read of the transaction"}""";

	// The first read sends no read token, the later ones the token of its answer.
	[Test]
	public async Task TheReadsSendTheReadTokenOfTheFirstOne()
	{
		var server = new Server { Records = ["a"] };
		var transaction = Client(server).BeginConditionalTransaction();
		var first = await transaction.GetAsync(["a", "b"]);
		Assert.That(await transaction.GetAsync("b"), Is.Null);
		var gets = server.Gets;
		Assert.That(gets.Select(get => get.Line), Is.All.EqualTo($"POST {CollectionPath}/conditional/get"));
		Assert.That(gets[0].Body.GetProperty("read_token").ValueKind, Is.EqualTo(JsonValueKind.Null));
		Assert.That(gets[1].Body.GetProperty("read_token").GetInt64(), Is.EqualTo(42));
		Assert.That(gets[0].Body.GetProperty("ids").GetRawText(), Is.EqualTo("""["a","b"]"""));
		Assert.That((first.Single().Id, first.Single().Document), Is.EqualTo(("a", "doc a")));
	}

	// The commit sends the read token, the ids read in order, and the writes in their order.
	[Test]
	public async Task TheCommitSendsTheReadsAndTheWrites()
	{
		var server = new Server { Records = ["a", "d"] };
		var transaction = Client(server).BeginConditionalTransaction();
		await transaction.GetAsync(["d", "b", "a"]);
		await transaction.UpdateAsync(["a"], documents: ["new a"]);
		await transaction.AddAsync(["b"], embeddings: [new([0f, 1f])]);
		await transaction.UpsertAsync(["c"], embeddings: [new([1f, 1f])]);
		await transaction.DeleteAsync(["d"]);
		var result = await transaction.CommitAsync();
		var commit = server.Commits.Single();
		Assert.That(commit.Line, Is.EqualTo($"POST {CollectionPath}/conditional/commit"));
		Assert.That(commit.Body.GetProperty("read_token").GetInt64(), Is.EqualTo(42));
		Assert.That(commit.Body.GetProperty("read_ids").GetRawText(), Is.EqualTo("""["a","b","d"]"""));
		var operations = commit.Body.GetProperty("operations").EnumerateArray().ToList();
		Assert.That(operations.Select(operation => operation.GetProperty("operation").GetString()), Is.EqualTo(new[] { "update", "add", "upsert", "delete" }));
		Assert.That(operations.Select(operation => operation.GetProperty("payload").GetProperty("ids").GetRawText()), Is.EqualTo(new[] { """["a"]""", """["b"]""", """["c"]""", """["d"]""" }));
		Assert.That((result.FirstInsertedRecordOffset, result.RecordCount), Is.EqualTo(((long?)7, 4)));
	}

	// A write of a transaction goes as the same write outside a transaction, with the copy of a document copy key, the nulls of its
	// deletions and the URIs.
	[Test]
	public async Task AWriteGoesAsOutsideATransaction()
	{
		var records = new ChromaRecords(["b"])
		{
			Embeddings = [new([0.5f, 1f])],
			Metadatas = [new Dictionary<string, object> { ["k"] = 1, ["gone"] = null! }],
			Documents = ["a document"],
			Uris = ["https://example.com/b"],
		};
		foreach (var update in new[] { false, true })
		{
			var outside = new Server { Records = ["b"] };
			var client = Client(outside).WithDocumentCopyKey("copy");
			await (update ? client.UpdateAsync(new ChromaRecords(records.Ids) { Metadatas = records.Metadatas, Documents = records.Documents, Uris = records.Uris }) : client.AddAsync(new ChromaRecords(records.Ids) { Embeddings = records.Embeddings, Metadatas = [new Dictionary<string, object> { ["k"] = 1 }], Documents = records.Documents, Uris = records.Uris }));
			var inside = new Server { Records = update ? ["b"] : [] };
			var transaction = Client(inside).WithDocumentCopyKey("copy").BeginConditionalTransaction();
			await transaction.GetAsync(["b"]);
			await (update ? transaction.UpdateAsync(new ChromaRecords(records.Ids) { Metadatas = records.Metadatas, Documents = records.Documents, Uris = records.Uris }) : transaction.AddAsync(new ChromaRecords(records.Ids) { Embeddings = records.Embeddings, Metadatas = [new Dictionary<string, object> { ["k"] = 1 }], Documents = records.Documents, Uris = records.Uris }));
			await transaction.CommitAsync();
			var write = outside.Requests.Single(request => request.Path.EndsWith(update ? "/update" : "/add"));
			var operation = inside.Commits.Single().Body.GetProperty("operations")[0];
			Assert.That(operation.GetProperty("operation").GetString(), Is.EqualTo(update ? "update" : "add"));
			Assert.That(operation.GetProperty("payload").GetRawText(), Is.EqualTo(write.Body.GetRawText()));
			Assert.That(operation.GetProperty("payload").GetProperty("metadatas")[0].GetProperty("copy").GetString(), Is.EqualTo("a document"));
		}
	}

	// As the Python client of Chroma sends them in a transaction: the embeddings as numbers, also where the server takes base64.
	[Test]
	public async Task TheEmbeddingsGoAsNumbers()
	{
		var server = new Server { Base64 = true };
		var transaction = Client(server).BeginConditionalTransaction();
		await transaction.UpsertAsync(["a"], embeddings: [new([0.5f, 1f])]);
		await transaction.CommitAsync();
		var embedding = server.Commits.Single().Body.GetProperty("operations")[0].GetProperty("payload").GetProperty("embeddings")[0];
		Assert.That(embedding.EnumerateArray().Select(value => value.ValueKind), Is.All.EqualTo(JsonValueKind.Number));
		Assert.That(embedding.EnumerateArray().Select(value => value.GetSingle()), Is.EqualTo(new[] { 0.5f, 1f }));
	}

	// Without writes the commit sends nothing.
	[Test]
	public async Task ACommitWithoutWritesSendsNothing()
	{
		var server = new Server();
		var transaction = Client(server).BeginConditionalTransaction();
		await transaction.GetAsync(["a"]);
		var result = await transaction.CommitAsync();
		Assert.That(server.Commits, Is.Empty);
		Assert.That((result.FirstInsertedRecordOffset, result.RecordCount), Is.EqualTo(((long?)null, 0)));
	}

	// As in the clients of Chroma, before any request: an add needs a read that did not find the id, an update and a delete a read that
	// found it. A read with a filter, an offset or a limit below the number of its ids tells nothing of the ids it did not return: they
	// may exist.
	[TestCase("add", "a", "", TestName = "An add of an id that a read found")]
	[TestCase("add", "x", "", TestName = "An add of an id no read asked for")]
	[TestCase("add", "b", "filter", TestName = "An add of an id that a read with a filter did not return")]
	[TestCase("add", "b", "limit", TestName = "An add of an id that a read with a limit below its ids did not return")]
	[TestCase("add", "b", "offset", TestName = "An add of an id that a read with an offset did not return")]
	[TestCase("update", "b", "", TestName = "An update of an id that a read did not find")]
	[TestCase("update", "x", "", TestName = "An update of an id no read asked for")]
	[TestCase("delete", "b", "", TestName = "A delete of an id that a read did not find")]
	public async Task AWriteNeedsTheRightRead(string operation, string id, string read)
	{
		var server = new Server { Records = ["a"] };
		var transaction = Client(server).BeginConditionalTransaction();
		await transaction.GetAsync(["a", "b"], where: read == "filter" ? ChromaWhereOperator.Equal("k", 1) : null, limit: read == "limit" ? 1 : null, offset: read == "offset" ? 1 : null);
		var requests = server.Requests.Count;
		Assert.That(() => Write(transaction, operation, id), Throws.InvalidOperationException.With.Message.Contains($"\"{id}\""));
		Assert.That(server.Requests, Has.Count.EqualTo(requests));
	}

	// As GetAsync, a read of ids sends no limit above their number: Chroma Cloud rejects a limit beyond its quota. The read still tells
	// which of them exist.
	[Test]
	public async Task ALimitAboveTheIds()
	{
		var server = new Server { Records = [] };
		var transaction = Client(server).BeginConditionalTransaction();
		await transaction.GetAsync(["b"], limit: 1000);
		Assert.That(server.Gets.Single().Body.GetProperty("limit").GetInt32(), Is.EqualTo(1));
		await transaction.AddAsync(["b"], embeddings: [new([1f, 0f])]);
	}

	// One write for each id, and no read of an id after its write.
	[Test]
	public async Task OneWriteForEachIdAndNoReadAfter()
	{
		var server = new Server { Records = ["a"] };
		var transaction = Client(server).BeginConditionalTransaction();
		Assert.That(() => transaction.UpsertAsync(["x", "x"], embeddings: [new([1f, 0f]), new([0f, 1f])]), Throws.ArgumentException.With.Message.Contains("twice"));
		await transaction.UpsertAsync(["x"], embeddings: [new([1f, 0f])]);
		Assert.That(() => transaction.UpsertAsync(["x"], embeddings: [new([1f, 0f])]), Throws.InvalidOperationException.With.Message.Contains("already"));
		var gets = server.Gets.Count;
		Assert.That(() => transaction.GetAsync(["x"]), Throws.InvalidOperationException.With.Message.Contains("after its write"));
		Assert.That(server.Gets, Has.Count.EqualTo(gets), "no read went");
		// A read that returns it, by a filter, throws when its answer comes.
		server.Records.Add("x");
		Assert.That(() => transaction.GetAsync(limit: 10), Throws.InvalidOperationException.With.Message.Contains("after its write"));
	}

	// A read without ids needs a positive limit; a delete needs ids.
	[Test]
	public void TheArgumentsOfTheReadsAndOfTheDeletes()
	{
		var transaction = Client(new Server()).BeginConditionalTransaction();
		Assert.That(() => transaction.GetAsync(), Throws.ArgumentException.With.Property("ParamName").EqualTo("limit"));
		Assert.That(() => transaction.GetAsync(limit: 0), Throws.ArgumentException.With.Property("ParamName").EqualTo("limit"));
		Assert.That(() => transaction.GetAsync(["a"], limit: -1), Throws.InstanceOf<ArgumentOutOfRangeException>());
		Assert.That(() => transaction.DeleteAsync([]), Throws.ArgumentException.With.Property("ParamName").EqualTo("ids"));
		Assert.That(() => transaction.DeleteAsync(null!), Throws.ArgumentNullException);
	}

	// The writes go in one request: a write beyond the records that the server takes in one request throws.
	[Test]
	public void AWriteBeyondTheBatchSize()
	{
		var transaction = Client(new Server { MaxBatchSize = 2 }).BeginConditionalTransaction();
		Assert.That(() => transaction.UpsertAsync(["x", "y", "z"], embeddings: [new([1f, 0f]), new([0f, 1f]), new([1f, 1f])]), Throws.ArgumentException.With.Message.Contains("the 2 that the server takes in one request"));
	}

	// The batch size of the caller, which splits the writes outside a transaction, does not limit the writes of a transaction.
	[Test]
	public async Task TheBatchSizeOfTheCallerIsNoLimit()
	{
		var server = new Server();
		var options = new ChromaConfigurationOptions("http://localhost:8000").WithBatchSplitting(1);
		var transaction = new ChromaCollectionClient(new ChromaCollection("c") { Id = Id }, options, new HttpClient(server)).BeginConditionalTransaction();
		await transaction.UpsertAsync(["x", "y"], embeddings: [new([1f, 0f]), new([0f, 1f])]);
		await transaction.CommitAsync();
		Assert.That(server.Commits.Single().Body.GetProperty("operations")[0].GetProperty("payload").GetProperty("ids").GetArrayLength(), Is.EqualTo(2));
	}

	// As a DbContext of EF Core, an operation that starts before the previous one ends throws.
	[Test]
	public async Task OneOperationAtATime()
	{
		var server = new Server { Gate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
		var transaction = Client(server).BeginConditionalTransaction();
		var first = transaction.GetAsync(["a"]);
		var second = transaction.GetAsync(["b"]);
		// Without the check, the second read would wait for the server as the first one does.
		var ended = await Task.WhenAny(second, Task.Delay(TimeSpan.FromSeconds(5)));
		server.Gate.SetResult(true);
		await first;
		Assert.That(ended, Is.SameAs(second), "the second operation throws at once");
		Assert.That(() => second, Throws.InvalidOperationException.With.Message.Contains("one at a time"));
		Assert.That(await transaction.GetAsync("b"), Is.Null, "the next operation goes");
	}

	// After its commit the transaction takes no other operation.
	[Test]
	public async Task NothingAfterTheCommit()
	{
		var transaction = Client(new Server()).BeginConditionalTransaction();
		await transaction.UpsertAsync(["x"], embeddings: [new([1f, 0f])]);
		await transaction.CommitAsync();
		Assert.That(() => transaction.GetAsync(["a"]), Throws.InvalidOperationException.With.Message.Contains("committed"));
		Assert.That(() => transaction.UpsertAsync(["y"], embeddings: [new([1f, 0f])]), Throws.InvalidOperationException.With.Message.Contains("committed"));
		Assert.That(() => transaction.CommitAsync(), Throws.InvalidOperationException.With.Message.Contains("committed"));
	}

	// As an ADO.NET transaction, also after a commit that failed: it may have gone, and the server would refuse it again.
	[Test]
	public async Task NothingAfterACommitThatFailed()
	{
		var server = new Server();
		server.CommitAnswers.Enqueue((HttpStatusCode.Conflict, Conflict));
		var transaction = Client(server).BeginConditionalTransaction();
		await transaction.UpsertAsync(["x"], embeddings: [new([1f, 0f])]);
		Assert.ThrowsAsync<ChromaConditionalWriteConflictException>(() => transaction.CommitAsync());
		Assert.That(() => transaction.CommitAsync(), Throws.InvalidOperationException.With.Message.Contains("commit failed"));
		Assert.That(server.Commits, Has.Count.EqualTo(1));
	}

	// Every read has the read token of the first one.
	[Test]
	public async Task AnAnswerWithoutOrWithAnotherReadToken()
	{
		var server = new Server();
		var transaction = Client(server).BeginConditionalTransaction();
		await transaction.GetAsync(["a"]);
		server.ReadToken = 43;
		Assert.That(() => transaction.GetAsync(["b"]), Throws.InstanceOf<ChromaException>().With.Message.Contains("changed from 42 to 43"));
		server.ReadToken = null;
		Assert.That(() => Client(server).BeginConditionalTransaction().GetAsync(["a"]), Throws.InstanceOf<ChromaException>().With.Message.Contains("no read token"));
	}

	// A client made by name reads its collection first, and the transaction works on that one.
	[Test]
	public async Task AClientMadeByName()
	{
		var server = new Server();
		var transaction = new ChromaClient(new ChromaConfigurationOptions("http://localhost:8000"), new HttpClient(server)).GetCollectionClient("c").BeginConditionalTransaction();
		await transaction.GetAsync(["a"]);
		Assert.That(server.Requests[0].Line, Is.EqualTo("GET /api/v2/tenants/default_tenant/databases/default_database/collections/c"));
		Assert.That(server.Requests[^1].Line, Is.EqualTo($"POST {CollectionPath}/conditional/get"));
	}

	// A client made by name that reads another collection under the name, as after a delete and a create, cannot go on with the
	// transaction.
	[Test]
	public async Task AClientMadeByNameStaysOnItsCollection()
	{
		var server = new Server();
		var transaction = new ChromaClient(new ChromaConfigurationOptions("http://localhost:8000"), new HttpClient(server)).GetCollectionClient("c").BeginConditionalTransaction();
		await transaction.GetAsync(["a"]);
		server.CollectionId = Guid.NewGuid();
		server.GetAnswers.Enqueue((HttpStatusCode.NotFound, $$"""{"error":"NotFoundError","message":"Collection {{Id}} does not exist"}"""));
		Assert.That(() => transaction.GetAsync(["b"]), Throws.InvalidOperationException.With.Message.Contains("one collection"));
	}

	// A first read that fails on a collection created again under the name goes again on the new collection, as any operation of a
	// client made by name: the transaction had read nothing.
	[Test]
	public async Task AClientMadeByNameBeforeTheFirstRead()
	{
		var server = new Server();
		var collection = new ChromaClient(new ChromaConfigurationOptions("http://localhost:8000"), new HttpClient(server)).GetCollectionClient("c");
		await collection.CountAsync();
		var created = Guid.Parse("99999999-2222-3333-4444-555555555555");
		server.CollectionId = created;
		server.GetAnswers.Enqueue((HttpStatusCode.NotFound, $$"""{"error":"NotFoundError","message":"Collection {{Id}} does not exist"}"""));
		var transaction = collection.BeginConditionalTransaction();
		await transaction.GetAsync(["a"]);
		Assert.That(server.Gets.Select(get => get.Path), Is.EqualTo(new[] { $"{CollectionPath}/conditional/get", $"{CollectionPath.Replace(Id.ToString(), created.ToString())}/conditional/get" }));
	}

	// On Chroma Cloud a write whose nulls would take a record beyond the quota of keys throws in a transaction, also with
	// ReadsBeyondKeyQuota: a transaction reads only with its own reads.
	[Test]
	public void NoReadBeyondTheKeyQuota()
	{
		var server = new Server();
		var options = new ChromaConfigurationOptions("http://localhost:8000").WithChromaCloud().WithReadsBeyondKeyQuota();
		var transaction = new ChromaCollectionClient(new ChromaCollection("c") { Id = Id }, options, new HttpClient(server)).BeginConditionalTransaction();
		var metadata = Enumerable.Range(0, 33).ToDictionary(i => $"k{i}", _ => (object)null!);
		Assert.That(() => transaction.UpsertAsync(new ChromaRecords(["x"]) { Embeddings = [new([1f, 0f])], Metadatas = [metadata] }), Throws.ArgumentException.With.Message.Contains("conditional transaction"));
		Assert.That(server.Requests.Where(request => request.Path.EndsWith("/get")), Is.Empty);
	}

	// The conflict and the stale read have their exceptions, as the JavaScript client of Chroma tells them; any other 409 and 412 stays a
	// ChromaException.
	[TestCase(409, Conflict, typeof(ChromaConditionalWriteConflictException))]
	[TestCase(409, """{"error":"ChromaError","message":"conditional write conflict"}""", typeof(ChromaConditionalWriteConflictException))]
	[TestCase(409, """{"error":"UniqueConstraintError","message":"Collection c already exists"}""", typeof(ChromaException))]
	[TestCase(412, StaleRead, typeof(ChromaStaleReadException))]
	[TestCase(412, """{"error":"ChromaError","message":"precondition failed"}""", typeof(ChromaException))]
	public void TheExceptions(int status, string body, Type expected)
	{
		var client = new ChromaClient(new ChromaConfigurationOptions("http://localhost:8000"), new HttpClient(new Answer((HttpStatusCode)status, body)));
		var ex = (ChromaException)Assert.ThrowsAsync(Is.InstanceOf<ChromaException>(), () => client.HeartbeatAsync())!;
		Assert.That((ex.GetType(), ex.StatusCode, ex.ErrorType), Is.EqualTo((expected, (HttpStatusCode?)status, JsonDocument.Parse(body).RootElement.GetProperty("error").GetString())));
	}

	// After a conflict of the commit, the run goes again with a new transaction, which reads from the start.
	[Test]
	public async Task ARunGoesAgainAfterAConflict()
	{
		var server = new Server { Records = ["a"] };
		server.CommitAnswers.Enqueue((HttpStatusCode.Conflict, Conflict));
		server.CommitAnswers.Enqueue((HttpStatusCode.Conflict, Conflict));
		var runs = 0;
		var result = await Client(server).RunConditionalTransactionAsync(async (transaction, cancellationToken) =>
		{
			runs++;
			var entry = await transaction.GetAsync("a", cancellationToken: cancellationToken);
			await transaction.UpdateAsync(["a"], documents: [entry!.Document + "!"], cancellationToken: cancellationToken);
			return runs;
		});
		Assert.That((result, runs, server.Commits.Count), Is.EqualTo((3, 3, 3)));
		Assert.That(server.Gets.Select(get => get.Body.GetProperty("read_token").ValueKind), Is.All.EqualTo(JsonValueKind.Null));
	}

	// After maxRetries runs again the error stands; 0 runs once.
	[TestCase(3, 4)]
	[TestCase(0, 1)]
	public void AfterTheLastRunTheConflictStands(int maxRetries, int expected)
	{
		var server = new Server();
		for (var i = 0; i < 5; i++)
		{
			server.CommitAnswers.Enqueue((HttpStatusCode.Conflict, Conflict));
		}
		var runs = 0;
		Assert.ThrowsAsync<ChromaConditionalWriteConflictException>(() => Client(server).RunConditionalTransactionAsync(async (transaction, cancellationToken) =>
		{
			runs++;
			await transaction.UpsertAsync(["a"], embeddings: [new([1f, 0f])], cancellationToken: cancellationToken);
		}, maxRetries));
		Assert.That((runs, server.Commits.Count), Is.EqualTo((expected, expected)));
	}

	// A stale read or a Backoff goes again too; any other error of the commit stands at once.
	[TestCase(412, StaleRead, 2)]
	[TestCase(429, """{"error":"Backoff","message":"Backoff and retry"}""", 2)]
	[TestCase(429, """{"error":"RateLimitError","message":"rate limit exceeded"}""", 1)]
	[TestCase(500, """{"error":"InternalError","message":"Internal error"}""", 1)]
	public async Task WhichErrorsOfTheCommitGoAgain(int status, string body, int expected)
	{
		var server = new Server();
		server.CommitAnswers.Enqueue(((HttpStatusCode)status, body));
		var runs = 0;
		var run = Client(server).RunConditionalTransactionAsync(async (transaction, cancellationToken) =>
		{
			runs++;
			await transaction.UpsertAsync(["a"], embeddings: [new([1f, 0f])], cancellationToken: cancellationToken);
		});
		if (expected == 1)
		{
			Assert.ThrowsAsync(Is.InstanceOf<ChromaException>(), () => run);
		}
		else
		{
			await run;
		}
		Assert.That((runs, server.Commits.Count), Is.EqualTo((expected, expected)));
	}

	// A stale read of a read goes again when the operation throws it on, not when it throws another error.
	[Test]
	public async Task AStaleReadOfARead()
	{
		var server = new Server { Records = ["a"] };
		server.GetAnswers.Enqueue((HttpStatusCode.PreconditionFailed, StaleRead));
		var runs = 0;
		await Client(server).RunConditionalTransactionAsync(async (transaction, cancellationToken) =>
		{
			runs++;
			await transaction.GetAsync(["a"], cancellationToken: cancellationToken);
		});
		Assert.That(runs, Is.EqualTo(2));

		server.GetAnswers.Enqueue((HttpStatusCode.PreconditionFailed, StaleRead));
		runs = 0;
		Assert.ThrowsAsync<InvalidOperationException>(() => Client(server).RunConditionalTransactionAsync(async (transaction, cancellationToken) =>
		{
			runs++;
			try
			{
				await transaction.GetAsync(["a"], cancellationToken: cancellationToken);
			}
			catch (ChromaStaleReadException ex)
			{
				throw new InvalidOperationException("another error", ex);
			}
		}));
		Assert.That(runs, Is.EqualTo(1));

		// The same kind of error, thrown by the operation itself, does not go again: only the error of an operation of the transaction.
		runs = 0;
		Assert.ThrowsAsync<ChromaStaleReadException>(() => Client(server).RunConditionalTransactionAsync(async (transaction, cancellationToken) =>
		{
			runs++;
			await transaction.GetAsync(["a"], cancellationToken: cancellationToken);
			throw new ChromaStaleReadException("from the operation");
		}));
		Assert.That(runs, Is.EqualTo(1));
	}

	// The run commits: the operation cannot, and after an error that does not go again nothing is committed.
	[Test]
	public void TheRunCommits()
	{
		var server = new Server();
		Assert.ThrowsAsync<InvalidOperationException>(() => Client(server).RunConditionalTransactionAsync(async (transaction, cancellationToken) =>
		{
			await transaction.UpsertAsync(["a"], embeddings: [new([1f, 0f])], cancellationToken: cancellationToken);
			await transaction.CommitAsync(cancellationToken);
		}));
		Assert.ThrowsAsync<FormatException>(async () => await Client(server).RunConditionalTransactionAsync<int>(async (transaction, cancellationToken) =>
		{
			await transaction.UpsertAsync(["a"], embeddings: [new([1f, 0f])], cancellationToken: cancellationToken);
			throw new FormatException();
		}));
		Assert.That(server.Commits, Is.Empty);
	}

	[Test]
	public void TheArgumentsOfTheRun()
	{
		var client = Client(new Server());
		Assert.That(() => client.RunConditionalTransactionAsync<int>(null!), Throws.ArgumentNullException);
		Assert.That(() => client.RunConditionalTransactionAsync(null!), Throws.ArgumentNullException);
		Assert.That(() => client.RunConditionalTransactionAsync((_, _) => Task.FromResult(1), maxRetries: -1), Throws.InstanceOf<ArgumentOutOfRangeException>());
	}

	// With trimming and NativeAOT, where reflection is off, the client knows only the types of ChromaJsonContext: the requests and the
	// answers of the transactions are there.
	[Test]
	public void TheirTypesAreGenerated()
	{
		var types = new[]
		{
			typeof(Models.Requests.ConditionalGetRequest), typeof(Models.Responses.ConditionalGetResponse), typeof(Models.Requests.ConditionalCommitRequest),
			typeof(Models.Requests.ConditionalOperation), typeof(ChromaConditionalCommitResult),
		};
		Assert.That(types.Where(type => ChromaDB.Client.Common.ChromaJsonContext.Default.GetTypeInfo(type) is null), Is.Empty);
	}

	static Task Write(ChromaConditionalTransaction transaction, string operation, string id) => operation switch
	{
		"add" => transaction.AddAsync([id], embeddings: [new([1f, 0f])]),
		"update" => transaction.UpdateAsync([id], embeddings: [new([1f, 0f])]),
		_ => transaction.DeleteAsync([id]),
	};

	static ChromaCollectionClient Client(HttpMessageHandler handler)
		=> new(new ChromaCollection("c") { Id = Id }, new ChromaConfigurationOptions("http://localhost:8000"), new HttpClient(handler));

	sealed record Request(string Method, string Path, string Line, JsonElement Body);

	// Answers every request with the status and the body.
	sealed class Answer(HttpStatusCode status, string body) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			=> Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
	}

	// A server with the records of Records: a read of a transaction returns those of them it asks for, the first ones up to the limit
	// without ids, with the read token ReadToken; a commit answers with 7 and the records of its writes. GetAnswers and CommitAnswers answer
	// first, in order.
	sealed class Server : HttpMessageHandler
	{
		public List<string> Records { get; init; } = ["a"];
		public Guid CollectionId { get; set; } = Id;
		public long? ReadToken { get; set; } = 42;
		public bool Base64 { get; init; }
		public int MaxBatchSize { get; init; } = 100;
		public TaskCompletionSource<bool>? Gate { get; init; }
		public Queue<(HttpStatusCode Status, string Body)> GetAnswers { get; } = new();
		public Queue<(HttpStatusCode Status, string Body)> CommitAnswers { get; } = new();
		public List<Request> Requests { get; } = [];
		public List<Request> Gets => Requests.Where(request => request.Path.EndsWith("/conditional/get")).ToList();
		public List<Request> Commits => Requests.Where(request => request.Path.EndsWith("/conditional/commit")).ToList();

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var text = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
			var body = text is { Length: > 0 } ? JsonDocument.Parse(text).RootElement.Clone() : default;
			var path = request.RequestUri!.AbsolutePath;
			Requests.Add(new Request(request.Method.Method, path, $"{request.Method.Method} {request.RequestUri.PathAndQuery}", body));
			if (Gate is { } gate && path.EndsWith("/conditional/get"))
			{
				await gate.Task;
			}
			var (status, answer) = path switch
			{
				_ when path.EndsWith("/pre-flight-checks") => (HttpStatusCode.OK, $$"""{"max_batch_size":{{MaxBatchSize}},"supports_base64_encoding":{{(Base64 ? "true" : "false")}}}"""),
				_ when path.EndsWith("/conditional/get") => GetAnswers.Count > 0 ? GetAnswers.Dequeue() : (HttpStatusCode.OK, Read(body)),
				_ when path.EndsWith("/conditional/commit") => CommitAnswers.Count > 0 ? CommitAnswers.Dequeue() : (HttpStatusCode.OK, $$"""{"first_inserted_record_offset":7,"record_count":{{Written(body)}}}"""),
				_ when path.EndsWith("/collections/c") => (HttpStatusCode.OK, $$"""{"id":"{{CollectionId}}","name":"c"}"""),
				_ when path.EndsWith("/version") => (HttpStatusCode.OK, "\"1.5.9\""),
				_ when path.EndsWith("/count") => (HttpStatusCode.OK, "0"),
				_ => (HttpStatusCode.OK, "{}"),
			};
			return new HttpResponseMessage(status) { Content = new StringContent(answer) };
		}

		string Read(JsonElement body)
		{
			var found = body.GetProperty("ids") is { ValueKind: JsonValueKind.Array } ids
				? Records.Where(record => ids.EnumerateArray().Any(id => id.GetString() == record)).ToList()
				: Records.Take(body.GetProperty("limit").GetInt32()).ToList();
			var token = ReadToken is { } value ? value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "null";
			return $$"""{"ids":[{{string.Join(",", found.Select(id => $"\"{id}\""))}}],"documents":[{{string.Join(",", found.Select(id => $"\"doc {id}\""))}}],"include":["documents"],"read_token":{{token}}}""";
		}

		static int Written(JsonElement body)
			=> body.GetProperty("operations").EnumerateArray().Sum(operation => operation.GetProperty("payload").GetProperty("ids").GetArrayLength());
	}
}
