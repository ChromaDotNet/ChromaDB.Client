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
		yield return Case<ChromaClient>("ListCollections", (c, t) => c.ListCollectionsAsync(cancellationToken: t));
		yield return Case<ChromaClient>("ListCollectionsPage", (c, t) => c.ListCollectionsAsync(limit: 2, cancellationToken: t));
		yield return Case<ChromaClient>("GetCollection", (c, t) => c.GetCollectionAsync("collection", cancellationToken: t));
		yield return Case<ChromaClient>("CollectionExists", (c, t) => c.CollectionExistsAsync("collection", cancellationToken: t));
		yield return Case<ChromaClient>("GetCollectionById", (c, t) => c.GetCollectionByIdAsync(Guid.NewGuid(), cancellationToken: t));
		yield return Case<ChromaClient>("Heartbeat", (c, t) => c.HeartbeatAsync(t));
		yield return Case<ChromaClient>("CreateCollection", (c, t) => c.CreateCollectionAsync("collection", cancellationToken: t));
		yield return Case<ChromaClient>("CreateCollectionDefinition", (c, t) => c.CreateCollectionAsync(new ChromaCollectionDefinition("collection"), cancellationToken: t));
		yield return Case<ChromaClient>("GetOrCreateCollectionDefinition", (c, t) => c.GetOrCreateCollectionAsync(new ChromaCollectionDefinition("collection"), cancellationToken: t));
		yield return Case<ChromaClient>("GetOrCreateCollection", (c, t) => c.GetOrCreateCollectionAsync("collection", cancellationToken: t));
		yield return Case<ChromaClient>("DeleteCollection", (c, t) => c.DeleteCollectionAsync("collection", cancellationToken: t));
		yield return Case<ChromaClient>("GetVersion", (c, t) => c.GetVersionAsync(t));
		yield return Case<ChromaClient>("GetUserIdentity", (c, t) => c.GetUserIdentityAsync(t));
		yield return Case<ChromaClient>("GetPreFlightChecks", (c, t) => c.GetPreFlightChecksAsync(t));
		yield return Case<ChromaClient>("Reset", (c, t) => c.ResetAsync(t));
		yield return Case<ChromaClient>("CountCollections", (c, t) => c.CountCollectionsAsync(cancellationToken: t));
		yield return Case<ChromaClient>("CreateTenant", (c, t) => c.CreateTenantAsync("tenant", t));
		yield return Case<ChromaClient>("GetTenant", (c, t) => c.GetTenantAsync("tenant", t));
		yield return Case<ChromaClient>("CreateDatabase", (c, t) => c.CreateDatabaseAsync("database", cancellationToken: t));
		yield return Case<ChromaClient>("GetDatabase", (c, t) => c.GetDatabaseAsync("database", cancellationToken: t));
		yield return Case<ChromaClient>("ListDatabases", (c, t) => c.ListDatabasesAsync(cancellationToken: t));
		yield return Case<ChromaClient>("ListDatabasesPage", (c, t) => c.ListDatabasesAsync(limit: 2, cancellationToken: t));
		yield return Case<ChromaClient>("DeleteDatabase", (c, t) => c.DeleteDatabaseAsync("database", cancellationToken: t));
	}

	static IEnumerable<TestCaseData> CollectionClientCalls()
	{
		yield return Case<ChromaCollectionClient>("GetById", (c, t) => c.GetAsync("id", cancellationToken: t));
		yield return Case<ChromaCollectionClient>("Get", (c, t) => c.GetAsync(cancellationToken: t));
		yield return Case<ChromaCollectionClient>("QuerySingle", (c, t) => c.QueryAsync(Embedding, cancellationToken: t));
		yield return Case<ChromaCollectionClient>("Query", (c, t) => c.QueryAsync([Embedding], cancellationToken: t));
		yield return Case<ChromaCollectionClient>("QueryWithIds", (c, t) => c.QueryAsync(new ChromaQuery([Embedding]) { Ids = ["id"] }, t));
		yield return Case<ChromaCollectionClient>("Add", (c, t) => c.AddAsync(["id"], embeddings: [Embedding], cancellationToken: t));
		yield return Case<ChromaCollectionClient>("AddRecords", (c, t) => c.AddAsync(new ChromaRecords(["id"]) { Embeddings = [Embedding] }, t));
		yield return Case<ChromaCollectionClient>("UpdateRecords", (c, t) => c.UpdateAsync(new ChromaRecords(["id"]), t));
		yield return Case<ChromaCollectionClient>("UpsertRecords", (c, t) => c.UpsertAsync(new ChromaRecords(["id"]) { Embeddings = [Embedding] }, t));
		yield return Case<ChromaCollectionClient>("Update", (c, t) => c.UpdateAsync(["id"], cancellationToken: t));
		yield return Case<ChromaCollectionClient>("Upsert", (c, t) => c.UpsertAsync(["id"], embeddings: [Embedding], cancellationToken: t));
		yield return Case<ChromaCollectionClient>("Delete", (c, t) => c.DeleteAsync(["id"], cancellationToken: t));
		yield return Case<ChromaCollectionClient>("Count", (c, t) => c.CountAsync(t));
		yield return Case<ChromaCollectionClient>("Peek", (c, t) => c.PeekAsync(cancellationToken: t));
		yield return Case<ChromaCollectionClient>("Modify", (c, t) => c.ModifyAsync(name: "collection", cancellationToken: t));
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
		await Assert.ThatAsync(() => client.HeartbeatAsync(), Throws.InstanceOf<ChromaException>());
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
