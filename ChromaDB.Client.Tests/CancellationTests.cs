using ChromaDB.Client.Models;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

[TestFixture]
public class CancellationTests
{
	static readonly ChromaConfigurationOptions Options = new(uri: "http://localhost:8000/api/v2/");
	static readonly ChromaCollection Collection = new("collection") { Id = Guid.NewGuid() };
	static readonly ReadOnlyMemory<float> Embedding = new([1f, 0.5f, 0f, -0.5f, -1f]);

	static IEnumerable<TestCaseData> ClientCalls()
	{
		yield return Case<ChromaClient>("ListCollections", (c, t) => c.ListCollections(cancellationToken: t));
		yield return Case<ChromaClient>("ListCollectionsPage", (c, t) => c.ListCollections(limit: 2, cancellationToken: t));
		yield return Case<ChromaClient>("GetCollection", (c, t) => c.GetCollection("collection", cancellationToken: t));
		yield return Case<ChromaClient>("GetCollectionById", (c, t) => c.GetCollectionById(Guid.NewGuid(), cancellationToken: t));
		yield return Case<ChromaClient>("Heartbeat", (c, t) => c.Heartbeat(t));
		yield return Case<ChromaClient>("CreateCollection", (c, t) => c.CreateCollection("collection", cancellationToken: t));
		yield return Case<ChromaClient>("GetOrCreateCollection", (c, t) => c.GetOrCreateCollection("collection", cancellationToken: t));
		yield return Case<ChromaClient>("DeleteCollection", (c, t) => c.DeleteCollection("collection", cancellationToken: t));
		yield return Case<ChromaClient>("GetVersion", (c, t) => c.GetVersion(t));
		yield return Case<ChromaClient>("GetUserIdentity", (c, t) => c.GetUserIdentity(t));
		yield return Case<ChromaClient>("GetPreFlightChecks", (c, t) => c.GetPreFlightChecks(t));
		yield return Case<ChromaClient>("Reset", (c, t) => c.Reset(t));
		yield return Case<ChromaClient>("CountCollections", (c, t) => c.CountCollections(cancellationToken: t));
		yield return Case<ChromaClient>("CreateTenant", (c, t) => c.CreateTenant("tenant", t));
		yield return Case<ChromaClient>("GetTenant", (c, t) => c.GetTenant("tenant", t));
		yield return Case<ChromaClient>("CreateDatabase", (c, t) => c.CreateDatabase("database", cancellationToken: t));
		yield return Case<ChromaClient>("GetDatabase", (c, t) => c.GetDatabase("database", cancellationToken: t));
		yield return Case<ChromaClient>("ListDatabases", (c, t) => c.ListDatabases(cancellationToken: t));
		yield return Case<ChromaClient>("ListDatabasesPage", (c, t) => c.ListDatabases(limit: 2, cancellationToken: t));
		yield return Case<ChromaClient>("DeleteDatabase", (c, t) => c.DeleteDatabase("database", cancellationToken: t));
	}

	static IEnumerable<TestCaseData> CollectionClientCalls()
	{
		yield return Case<ChromaCollectionClient>("GetById", (c, t) => c.Get("id", cancellationToken: t));
		yield return Case<ChromaCollectionClient>("Get", (c, t) => c.Get(cancellationToken: t));
		yield return Case<ChromaCollectionClient>("QuerySingle", (c, t) => c.Query(Embedding, cancellationToken: t));
		yield return Case<ChromaCollectionClient>("Query", (c, t) => c.Query([Embedding], cancellationToken: t));
		yield return Case<ChromaCollectionClient>("QueryWithIds", (c, t) => c.Query(new ChromaQuery([Embedding]) { Ids = ["id"] }, t));
		yield return Case<ChromaCollectionClient>("Add", (c, t) => c.Add(["id"], embeddings: [Embedding], cancellationToken: t));
		yield return Case<ChromaCollectionClient>("AddRecords", (c, t) => c.Add(new ChromaRecords(["id"]) { Embeddings = [Embedding] }, t));
		yield return Case<ChromaCollectionClient>("UpdateRecords", (c, t) => c.Update(new ChromaRecords(["id"]), t));
		yield return Case<ChromaCollectionClient>("UpsertRecords", (c, t) => c.Upsert(new ChromaRecords(["id"]) { Embeddings = [Embedding] }, t));
		yield return Case<ChromaCollectionClient>("Update", (c, t) => c.Update(["id"], cancellationToken: t));
		yield return Case<ChromaCollectionClient>("Upsert", (c, t) => c.Upsert(["id"], embeddings: [Embedding], cancellationToken: t));
		yield return Case<ChromaCollectionClient>("Delete", (c, t) => c.Delete(["id"], cancellationToken: t));
		yield return Case<ChromaCollectionClient>("Count", (c, t) => c.Count(t));
		yield return Case<ChromaCollectionClient>("Peek", (c, t) => c.Peek(cancellationToken: t));
		yield return Case<ChromaCollectionClient>("Modify", (c, t) => c.Modify(name: "collection", cancellationToken: t));
	}

	static TestCaseData Case<TClient>(string name, Func<TClient, CancellationToken, Task> call)
		=> new TestCaseData(call).SetName($"{typeof(TClient).Name}.{name}");

	[TestCaseSource(nameof(ClientCalls))]
	public async Task ClientPassesTheToken(Func<ChromaClient, CancellationToken, Task> call)
	{
		using var httpClient = new HttpClient(new PendingHandler());
		var client = new ChromaClient(Options, httpClient);
		using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
		await Assert.ThatAsync(() => call(client, cts.Token), Throws.InstanceOf<OperationCanceledException>());
	}

	[TestCaseSource(nameof(CollectionClientCalls))]
	public async Task CollectionClientPassesTheToken(Func<ChromaCollectionClient, CancellationToken, Task> call)
	{
		using var httpClient = new HttpClient(new PendingHandler());
		var client = new ChromaCollectionClient(Collection, Options, httpClient);
		using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
		await Assert.ThatAsync(() => call(client, cts.Token), Throws.InstanceOf<OperationCanceledException>());
	}

	// A timeout of the HttpClient is not a cancellation requested by the caller: it stays a ChromaException.
	[Test]
	public async Task TimeoutIsChromaException()
	{
		using var httpClient = new HttpClient(new PendingHandler()) { Timeout = TimeSpan.FromMilliseconds(100) };
		var client = new ChromaClient(Options, httpClient);
		await Assert.ThatAsync(() => client.Heartbeat(), Throws.InstanceOf<ChromaException>());
	}

	// Never answers: the request ends only when its token is canceled.
	sealed class PendingHandler : HttpMessageHandler
	{
		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			await Task.Delay(Timeout.Infinite, cancellationToken);
			throw new InvalidOperationException();
		}
	}
}
