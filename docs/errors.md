# Errors

A failed request throws a `ChromaException`, with the original exception as `InnerException`: an error answer from the server, a network error, an answer that is not the expected JSON, or a timeout. Other exceptions, like an assembly that does not load or a disposed `HttpClient`, are not wrapped.

- `StatusCode` is the HTTP status of the answer, or null when there was no answer, like on a timeout.
- `ErrorType` is the kind of error the server names: `NotFoundError` or `InvalidArgumentError` from Chroma 1.x, `InvalidCollection` from Chroma 0.5 and 0.6, `ValueError` from the v1 API of Chroma 0.4, or null when it names none.

```csharp
if (!await client.CollectionExistsAsync("my_collection"))
{
	await client.CreateCollectionAsync("my_collection");
}
```

When a 0.x server rejects a request with validation errors, the message lists them, like `body.n_results: Input should be a valid integer`.

`CollectionExistsAsync` tells a missing collection apart from other errors on every tested server (KD-43 in [docs/COMPATIBILITY.md](COMPATIBILITY.md)). Any other error, like a bare `404` from a wrong address, throws.

## Reads that go again

A read changes nothing, so after an error that can pass it goes again: each request of `GetAsync`, `PeekAsync`, `QueryAsync`, `SearchAsync` and `CountAsync`, and the reads that a write makes first, as the get of `ChromaUpsertStrategy.SkipUnchangedEmbeddings`, the get of `ReadsBeyondKeyQuota` on Chroma Cloud and the pages of ids that `DeleteCollectionAsync` reads with `deleteRecordsFirst`. The errors that can pass are an answer `429`, `500`, `502`, `503` or `504`, other than a missing collection, a request that fails on the network, and an answer whose lists do not have one item for every record. Chroma 1.5.9 answers `500` "Error finding id" or "Error creating hnsw segment reader" to a get with embeddings while other clients write and delete the same records, or answers it without the embedding of a record, and the same get right after goes. After the last attempt the error stands; for an answer whose lists do not have one item for every record, it is a `ChromaException` that says it, like "The answer of the server to a get has 1 id and 0 embeddings". A timeout of the `HttpClient` does not go again: the `ChromaException` with its `TaskCanceledException` stands. A write does not go again with `ReadRetries`: after an error one that failed may have gone. Only after the refusal of a log that needs compaction it goes again, with `WriteRetries`, below.

Before each new attempt the client waits with an exponential backoff with jitter: a random time between the smallest delay and the smaller of the largest delay and the base delay times 2 to the attempts that went again before. The smallest delay keeps a read from going again at once. Without it, it is 0.8 times the base delay, the lower bound of the jitter of Azure.Core; up to 2.17.0 it was zero. `ChromaReadRetries.Default` makes 4 attempts at most, with a smallest delay of 40 ms, a base delay of 50 ms and a largest delay of 1 second: it waits 40 to 50 ms, then 40 to 100 ms, then 40 to 200 ms. `WithReadRetries` sets others, with or without the smallest delay, and `ChromaReadRetries.None` turns them off:

```csharp
var options = new ChromaConfigurationOptions(uri: "http://localhost:8000")
	.WithReadRetries(new ChromaReadRetries(maxAttempts: 6, baseDelay: TimeSpan.FromMilliseconds(100), maxDelay: TimeSpan.FromSeconds(2)));
var withSmallestDelay = new ChromaReadRetries(maxAttempts: 6, minDelay: TimeSpan.FromMilliseconds(20), baseDelay: TimeSpan.FromMilliseconds(100), maxDelay: TimeSpan.FromSeconds(2));
```

The smallest delay goes from zero to the base delay, and the largest delay from the base delay to `Int32.MaxValue` milliseconds; other values throw an `ArgumentOutOfRangeException` that names the parameter.

## Writes refused for compaction

Distributed Chroma refuses a write to a collection whose log holds too many records not yet compacted, before it takes any of them: `429` with the message "log needs compaction before accepting more writes; please backoff exponentially and retry". Then the request goes again, until a compaction makes room: each batch of `AddAsync`, `UpdateAsync`, `UpsertAsync` and `DeleteAsync`, and each delete of `DeleteAllAsync` and of `DeleteCollectionAsync` with `deleteRecordsFirst`. The batches that went are not sent again. After the last attempt the refusal stands, as a `ChromaException` with `StatusCode` `429`. Any other error of a write, also another `429`, stands at once.

Before each new attempt the write waits with an exponential backoff with equal jitter: a random time between half the bound and the bound, and never under the smallest delay, where the bound is the smaller of the largest delay and the base delay times 2 to the attempts that went again before. A refusal lasts until a compaction takes enough records of the log, which takes seconds, so the writes have their own setting, with longer delays than the reads, and a short refusal still goes again within the base delay. `ChromaWriteRetries.Default` makes 12 attempts at most, without a smallest delay, with a base delay of 1 second and a largest delay of 10 seconds: it waits 0.5 to 1 second, then 1 to 2, 2 to 4, 4 to 8, and then 5 to 10 seconds, from 42.5 to 85 seconds in all before the refusal stands.

When the refusal carries a `Retry-After` header, in seconds or as a date, the write waits that long instead, and up to a tenth more, so that the clients refused together do not come back together, at most 30 seconds, or the largest delay when it is longer; the attempt counts as the others. Distributed Chroma sends none. The 30 seconds are above the largest delay because a server can answer 15 or 20 seconds: a wait cut at 10 seconds sends the write again while the log is still full.

`WithWriteRetries` sets other attempts and delays, and `ChromaWriteRetries.None` turns them off:

```csharp
var options = new ChromaConfigurationOptions(uri: "http://localhost:8000")
	.WithWriteRetries(new ChromaWriteRetries(maxAttempts: 15, minDelay: TimeSpan.Zero, baseDelay: TimeSpan.FromSeconds(2), maxDelay: TimeSpan.FromSeconds(20)));
```
