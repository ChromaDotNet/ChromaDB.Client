# Conditional transactions

A conditional transaction reads records of a collection and writes them only if none of the records it read or writes changed after its reads, as `collection.conditional()` of the Python and JavaScript clients of Chroma. Distributed Chroma, the one Chroma Cloud runs on, has conditional transactions for the tenants that the configuration of its frontend enables: the sample configuration of Chroma lists only `default_tenant` in `tenants_with_transactions_enabled`, and another tenant gets "conditional transactions are disabled by frontend configuration". VecturaDB has them. Chroma 1.5.9 does not.

```csharp
var counted = await collectionClient.RunConditionalTransactionAsync(async (transaction, cancellationToken) =>
{
	var counter = await transaction.GetAsync("counter", include: ChromaGetInclude.Documents, cancellationToken: cancellationToken);
	var value = int.Parse(counter!.Document!) + 1;
	await transaction.UpdateAsync(["counter"], documents: [$"{value}"], cancellationToken: cancellationToken);
	return value;
});
```

`RunConditionalTransactionAsync` runs the operation in a new transaction and commits it. When another writer changed a record of the transaction, the commit throws a `ChromaConditionalWriteConflictException` and writes nothing, and the run goes again with a new transaction, which reads the records as the other writer left them: at once, up to `maxRetries` times, 3 by default. A `ChromaStaleReadException` and a `429` whose `ErrorType` is `Backoff` go again too, when an operation of the transaction throws them or the commit does. Any other error stands at once, and the operation cannot commit the transaction itself.

`BeginConditionalTransaction` gives a transaction to commit without a run:

```csharp
var transaction = collectionClient.BeginConditionalTransaction();
var found = await transaction.GetAsync(["a", "b"]);                                   // "a" exists, "b" does not
await transaction.UpdateAsync(["a"], documents: ["new text"]);
await transaction.AddAsync(["b"], embeddings: [embedding], documents: ["a new record"]);
var result = await transaction.CommitAsync();                                         // result.RecordCount: 2
```

The reads go at once, one request each, without the pages of `GetAsync`. The writes wait in the client until `CommitAsync`, which sends them all in one request; a transaction without writes sends none. As in the clients of Chroma:

- a transaction works on one collection, with `GetAsync`, `AddAsync`, `UpdateAsync`, `UpsertAsync` and `DeleteAsync` by ids, and no query;
- an add needs a read of its ids that did not find them, without filters, without an offset and without a limit below their number; an update and a delete need a read that found them; an upsert needs no read;
- it takes one write for each id, and no read of an id after its write;
- a read without ids needs a positive `limit`, and the commit checks only the records it returned;
- a later read answers the records as they were at the first read, or throws a `ChromaStaleReadException` when they changed since.

The client checks these rules before any request, with an `ArgumentException` or an `InvalidOperationException`. It prepares the writes as outside a transaction, with the embeddings of `WithEmbeddingGenerator`, the document copies and the BM25 sparse vectors, but without the reads of `UpsertStrategy` and of `ReadsBeyondKeyQuota`. A write takes at most the records that the server takes in one request.

The operations of a transaction go one at a time: one that starts before the previous one ends throws an `InvalidOperationException`, as on a `DbContext` of EF Core. After `CommitAsync`, also when it throws, the transaction takes no other operation: a commit that failed may have gone, as after a timeout, and the server would refuse it again.
