[![ChromaDotNet](https://raw.githubusercontent.com/ChromaDotNet/.github/main/assets/logo-64.png)](https://chromadotnet.org)

# ChromaDB.Client

[![NuGet](https://img.shields.io/nuget/v/ChromaDotNet.Client)](https://www.nuget.org/packages/ChromaDotNet.Client/)
[![CI](https://img.shields.io/github/actions/workflow/status/ChromaDotNet/ChromaDB.Client/ci.yml?branch=main)](https://github.com/ChromaDotNet/ChromaDB.Client/actions/workflows/ci.yml)
[![License](https://img.shields.io/github/license/ChromaDotNet/ChromaDB.Client)](https://github.com/ChromaDotNet/ChromaDB.Client/blob/main/LICENSE)

_ChromaDB.Client_, published on NuGet as `ChromaDotNet.Client`, is a .NET client for Chroma and Chroma Cloud. It covers:

- collections, tenants and databases;
- writing and reading records, and nearest neighbor queries with filters;
- the Search API of Chroma Cloud, with hybrid BM25 search;
- OpenTelemetry traces and metrics.

> This is a community project, the continuation of [ssone95/ChromaDB.Client](https://github.com/ssone95/ChromaDB.Client), kept up to date with current Chroma versions. It is not affiliated with or endorsed by Chroma. See [About this fork](#about-this-fork).

Website: [chromadotnet.org](https://chromadotnet.org)

## Compatibility

| Chroma server | API | Tested |
|---|---|---|
| 0.5.16 – 1.5.9 | v2, the default | all tests pass on each tested release |
| 0.5.1 – 0.5.15 | v1, with `ChromaApiVersion.V1` | all tests pass on each tested release |
| 0.4.10 – 0.5.0 | v1, with `ChromaApiVersion.V1` | collections and records work on each tested release; some of these servers lack tenants, `CountCollectionsAsync` or the `$not_contains` filter |
| Chroma Cloud | v2 | the tests pass, except the operations an API key cannot run, like `CreateTenantAsync` and `ResetAsync`; see [Chroma Cloud](#chroma-cloud) |

[docs/COMPATIBILITY.md](https://github.com/ChromaDotNet/ChromaDB.Client/blob/main/docs/COMPATIBILITY.md) lists every tested release, the differences between them and the versions the CI runs.

The [compatibility table](https://chromadotnet.org/compatibility/) shows every check of the latest release on Chroma 1.5.9 and on Chroma Cloud, on each .NET runtime and platform.

Chroma 1.0.16 and later require embeddings in `AddAsync` and `UpsertAsync`. The client does not compute them, so pass them yourself.

The package has three builds: .NET 8, .NET Framework 4.6.2 and .NET Standard 2.0. The tests run the .NET 8 build on every tested Chroma version, and the .NET Standard 2.0 build on Chroma 1.5.9. The .NET Framework 4.6.2 build has the same code as the .NET Standard 2.0 one. It is built against the assemblies that .NET Framework applications ship, so it runs next to OpenTelemetry without binding redirects.

## Installation

```
dotnet add package ChromaDotNet.Client
```

`ChromaDotNet.Client.DependencyInjection` adds the registration for `Microsoft.Extensions.DependencyInjection`:

- `AddChromaClient` registers the `ChromaClient` as a singleton, so singletons can take it too. `AddKeyedChromaClient` registers more than one, under different keys, for other servers, tenants or databases.
- The client's `HttpClient` sends each request through the current handler of `IHttpClientFactory`. The factory renews the handler after its lifetime, two minutes by default, so a DNS change of the server address is picked up on every target framework. After two minutes the client also asks again what it learned about the server, like its version.
- An overload configures that `HttpClient`, for example with a resilience handler, a proxy or a timeout: `services.AddChromaClient(_ => options, builder => builder.AddStandardResilienceHandler())`.
- `serviceProvider.CreateChromaClient(options, httpClientName)` creates a client the same way when it is kept elsewhere, like in an integration with an `HttpClient` name of its own.

Only releases are published to NuGet. Every change merged into `main` builds the packages, and its CI run keeps them as artifacts.

## Example

The URI can be just the address of the server, like `http://localhost:8000`, and the client adds `/api/v2/`. A URI with a path, like `http://localhost:8000/api/v2`, is used as it is, with or without the trailing slash.

Without an `HttpClient`, `new ChromaClient("http://localhost:8000")` or `new ChromaClient(options)` creates its own, and `Dispose` closes it: `using var client = new ChromaClient("http://localhost:8000");`. The clients it returns, like the collection clients, share that `HttpClient`. An `HttpClient` you pass to the constructor stays open.

The models, like `ChromaRecords`, `ChromaQuery` and `ChromaCollectionDefinition`, are in `ChromaDB.Client.Models`. `AddChromaClient` is in `ChromaDB.Client.DependencyInjection`.

```csharp
using ChromaDB.Client;
using ChromaDB.Client.Models;

var configOptions = new ChromaConfigurationOptions(uri: "http://localhost:8000/api/v2/");
using var httpClient = new HttpClient();
var client = new ChromaClient(configOptions, httpClient);

Console.WriteLine(await client.GetVersionAsync());

var string5Collection = await client.GetOrCreateCollectionAsync("string5");
var string5Client = new ChromaCollectionClient(string5Collection, configOptions, httpClient);

await string5Client.AddAsync(["340a36ad-c38a-406c-be38-250174aee5a4"], embeddings: [new([1f, 0.5f, 0f, -0.5f, -1f])]);

var getResult = await string5Client.GetAsync("340a36ad-c38a-406c-be38-250174aee5a4", include: ChromaGetInclude.Metadatas | ChromaGetInclude.Documents | ChromaGetInclude.Embeddings);
Console.WriteLine($"ID: {getResult!.Id}");

var queryData = await string5Client.QueryAsync([new([1f, 0.5f, 0f, -0.5f, -1f]), new([1.5f, 0f, 2f, -1f, -1.5f])], include: ChromaQueryInclude.Metadatas | ChromaQueryInclude.Distances);
foreach (var item in queryData)
{
	foreach (var entry in item)
	{
		Console.WriteLine($"ID: {entry.Id} | Distance: {entry.Distance}");
	}
}
```

## Collections and records

```csharp
var collections = await client.ListCollectionsAsync();
var page = await client.ListCollectionsAsync(limit: 10, offset: 20);
var count = await client.CountCollectionsAsync();
await collectionClient.ModifyAsync(name: "renamed", metadata: new Dictionary<string, object> { ["owner"] = "me" });
var first = await collectionClient.PeekAsync(5);
await client.DeleteCollectionAsync("renamed");
var deleted = await client.DeleteCollectionIfExistsAsync("renamed");   // false: it no longer exists
var heartbeat = await client.HeartbeatAsync();
var checks = await client.GetPreFlightChecksAsync();   // MaxBatchSize, SupportsBase64Encoding
var identity = await client.GetUserIdentityAsync();    // UserId, Tenant, Databases
var database = await client.GetDatabaseAsync("my_database");
```

`ModifyAsync` changes the name or the metadata of a collection. `PeekAsync` returns its first records. `DeleteCollectionIfExistsAsync` takes a missing collection as deleted already: each server tells it in its own way, which the client recognizes as `CollectionExistsAsync` does.

## API version

The client uses the v2 API. For the servers that have only the v1 API, like Chroma 0.5.15, choose it once in the options:

```csharp
var options = new ChromaConfigurationOptions(uri: "http://localhost:8000").WithApiVersion(ChromaApiVersion.V1);

// or, with dependency injection
services.AddChromaClient(options => options!.WithUri("http://localhost:8000").WithApiVersion(ChromaApiVersion.V1));
```

Chroma 0.5.16 to 0.5.23 serve both APIs. The v1 API of Chroma 0.6.3 fails on most requests, and Chroma 1.x answers it with `410 Gone`, so use v2 there.

## Records with URIs

```csharp
await collectionClient.AddAsync(new ChromaRecords(["a"]) { Embeddings = [new([1f, 0.5f, 0f])], Uris = ["s3://bucket/a.png"] });

var entries = await collectionClient.GetAsync(["a"], include: ChromaGetInclude.Uris);
Console.WriteLine(entries[0].Uri);
```

`ChromaRecords` holds the ids, embeddings, metadata, documents and URIs that `AddAsync`, `UpdateAsync` and `UpsertAsync` write.

## Querying some records only

```csharp
var results = await collectionClient.QueryAsync(new ReadOnlyMemory<float>([1f, 0.5f, 0f]), nResults: 1, ids: ["a", "c"]);
var same = await collectionClient.QueryAsync(new ChromaQuery([new([1f, 0.5f, 0f])]) { Ids = ["a", "c"], NResults = 1 });
```

`ChromaQuery` holds the query embeddings, the number of results, the filters, what to include and the ids to search among, and the offset: Chroma has no offset in queries, so the client asks for the skipped records too and leaves them out. Chroma 1.0.0 and later search only the records with those ids. An id without a record is left out, as Chroma Cloud does: Chroma 1.x fails on it, so `QueryAsync` asks again with the ids that have one. Chroma 0.x ignores the ids and searches all the records, so when a result falls outside the ids, `QueryAsync` throws a `ChromaException` instead of returning it.

## Metadata values

By default, with `ChromaMetadataValues.Exact`:

- strings stay strings;
- lists come back as `List<object>` of `string`, `long`, `double` and `bool`, like single values;
- a `double`, `float` or `decimal` comes back as a `double`, also when it is whole. The client writes `2.0`, as the Python client does, and Chroma keeps it a float.

Values that earlier versions of this client wrote as `2` stay integers in Chroma, so `LessThan("d", 2.2)` does not find them until they are written again.

With `ChromaMetadataValues.Inferred`, as in earlier versions, a string that looks like a date comes back as a `DateTime`, and a list as a `JsonElement`:

```csharp
var options = new ChromaConfigurationOptions(uri: "http://localhost:8000").WithMetadataValues(ChromaMetadataValues.Inferred);
```

A list in metadata, written and filtered:

```csharp
await collectionClient.AddAsync(new ChromaRecords(["a"]) { Embeddings = [new([1f, 0.5f, 0f])], Metadatas = [new Dictionary<string, object> { ["tags"] = new[] { "red", "blue" } }] });
var tagged = await collectionClient.GetAsync(where: ChromaWhereOperator.Contains("tags", "red"));
```

In `UpdateAsync` and `UpsertAsync`, a null value deletes the key on every tested Chroma. The type does not allow null, so write `null!`. `AddAsync` throws an `ArgumentException` for a null value: Chroma 0.x would drop the key, and 1.x rejects the request. A record without metadata keys comes back with `Metadata` null.

`ChromaMetadataConvert` converts .NET values to metadata values and back, always in the same form: `ToMetadataValue` writes a `DateTimeOffset` as round-trip text in UTC, so that equal instants are equal text, a `DateTime` as round-trip text with its `Kind`, a `DateOnly` as `yyyy-MM-dd`, and a sequence as a list; null and an empty sequence give null, no value. `FromMetadataValue(value, type)` reads a value of `ChromaMetadataValues.Exact` as the type, also arrays and lists, and throws an `InvalidCastException` for a value that does not convert. A filter with a converted value finds the values converted the same way. The client does not convert the values of a metadata dictionary by itself.

Chroma 1.5.0 and later store lists in metadata and filter them with `Contains` and `NotContains`. Chroma 1.0.0 to 1.4.1 reject them. Chroma 0.x accepts them but drops them without an error, so `AddAsync`, `UpdateAsync` and `UpsertAsync` throw a `ChromaException` before sending them. The client asks the server for its version once, and only when a record has a list.

Chroma 1.5 keeps the lists of the records of a deleted collection or database, and gives them to the next records it stores, in any collection. So on Chroma 1.x, except Chroma Cloud, `DeleteCollectionAsync` and `DeleteDatabaseAsync` delete the records first, in batches; every Chroma 1.x reports the same version, so this happens on all of them. A collection with many records takes more requests to delete.

An existing `ChromaClient`, for example one from dependency injection, gives a client that reads values the other way. That client shares the `HttpClient`, the options and what was learned about the server. `Options` returns the options of a client:

```csharp
var inferred = client.WithMetadataValues(ChromaMetadataValues.Inferred);
Console.WriteLine(inferred.Options.MetadataValues); // Inferred
```

## Errors

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

`CollectionExistsAsync` tells a missing collection apart from other errors on every tested server. Chroma 1.x answers `404`, the 0.x servers `400` or `500`, always with "does not exist" in the message. Any other error, like a bare `404` from a wrong address, throws.

## Mocks in tests

`ChromaClient` and `ChromaCollectionClient` can be mocked, as in the Azure SDKs. Their members are virtual, and a protected constructor creates a client without a server, for a subclass or a mocking library. Only the members it overrides work.

```csharp
sealed class FakeClient : ChromaClient
{
	public override Task<ChromaCollection> GetCollectionAsync(string name, string? tenant = null, string? database = null, CancellationToken cancellationToken = default)
		=> Task.FromResult(new ChromaCollection(name));
}
```

## Filters

`ToString()` of a `ChromaWhereOperator` or a `ChromaWhereDocumentOperator` returns the JSON the client sends:

```csharp
var where = ChromaWhereOperator.Equal("year", 2026) & ChromaWhereOperator.In("lang", "en", "it");
Console.WriteLine(where); // {"$and":[{"year":{"$eq":2026}},{"lang":{"$in":["en","it"]}}]}
```

- `ChromaWhereOperator` has `Equal`, `NotEqual`, `GreaterThan`, `GreaterThanOrEqual`, `LessThan`, `LessThanOrEqual`, `In`, `NotIn`, `Contains` and `NotContains`, combined with `&` and `|`.
- `ChromaWhereDocumentOperator` has `Contains`, `NotContains`, `Regex` and `NotRegex`.
- A chain of the same operator, like `a & b & c` or one built in a loop, goes as one list, `{"$and":[a,b,c]}`, as the Python client writes it.
- `Not` negates a filter. Chroma has no `$not`, so the negation goes into the operators: `$eq` becomes `$ne`, `$gt` becomes `$lte`, `$in` becomes `$nin`, `$contains` becomes `$not_contains`, `$regex` becomes `$not_regex`, and `$and` becomes `$or` of the negations. `$ne`, `$nin` and `$not_contains` match the records without the key, a comparison like `$lte` does not, on every tested Chroma: `Not(GreaterThan("k", 5))` leaves out the records without `k`, as `GreaterThan("k", 5)` does.
- `ChromaWhereOperator.All` matches every record and `None` no record, which Chroma has no filter for: the client sends no `where` for `All`, and no request for `None`, so a read with it returns nothing and a delete deletes nothing. `&` and `|` simplify with them, and each is a single instance, which `Not` also returns. `In` without values is `None`, and `NotIn` without values `All`: every tested Chroma rejects `$in` and `$nin` without values.

**Very long filters.** A single Chroma server turns a list of filters into an SQLite expression as deep as the list, and SQLite stops at 1000. Chroma 1.5.9 takes 987 to 994 filters in one list, depending on the operator. Beyond that it answers 500, and from about 4,400 it crashes. So the client counts that depth, the length of the lists along the deepest path of the filter. Beyond 900, it splits each list of n filters into ⌈√n⌉ lists with the same meaning. Split this way, Chroma 1.5.9 takes up to 8,167 filters, and Chroma 1.0.0 about 4,090. Beyond that, SQLite answers "too many SQL variables", the client throws a `ChromaException`, and the server stays up. That limit counts the values of the query, not the filters: on Chroma 1.5.9 an `In` or a `NotIn` takes about 16,000 values. Chroma 0.6.3 counts every filter of the query, however they are nested, and answers 500 from about 490.

`ChromaWhereDocumentOperator.Regex` and `NotRegex` filter the documents with a regular expression, from Chroma 1.0.12. Earlier versions fail on them.

```csharp
var apples = await collectionClient.GetAsync(whereDocument: ChromaWhereDocumentOperator.Regex("^apple"));
```

## Large writes

By default, `AddAsync`, `UpdateAsync`, `UpsertAsync` and `DeleteAsync` send their records in batches of the server's `max_batch_size`, one request after the other. The client asks `pre-flight-checks` once. If a batch fails, the earlier ones stay written. Chroma 0.4.10 has no `pre-flight-checks`, so it gets the records in one request, and so does a server whose answer the client cannot read. `WithBatchSplitting(false)` sends them in one request, as earlier versions did. Up to Chroma 1.0.13 a request beyond the limit fails; later versions accept it.

```csharp
var options = new ChromaConfigurationOptions(uri: "http://localhost:8000").WithBatchSplitting(false);
```

`GetAsync` reads more records than the batch size in pages: pages of the batch size from the offset, until the limit or the last record. Ids beyond the batch size go in batches, with the limit and the offset applied to all of them together. The pages are separate requests, so records written in between can be read twice or missed.

`WithBatchSplitting(maxBatchSize)` uses the smaller of that limit and the server's, or that limit alone where the server declares none.

Chroma Cloud declares 1000, but takes 300 records per write and answers at most 300 records per read, without an error, unless the quota is raised. So on Chroma Cloud, at `*.trychroma.com`, the client uses 300 when no limit is given. A server may reject a batch over its quota of records before writing any of it, as Chroma Cloud does with "current usage of 301 exceeds limit of 300". The client then sends that batch, the rest and the next writes in batches of the quota. A raised quota needs its limit:

```csharp
var options = new ChromaConfigurationOptions(uri: "https://api.trychroma.com").WithChromaToken(apiKey)
	.WithTenant(tenant).WithDatabase(database)
	.WithBatchSplitting(maxBatchSize: 1000); // a quota raised to 1000 records
```

`ChromaCloudQuotas` holds the default quotas of a Chroma Cloud tenant, as its documentation lists them: 300 records per request, 8,182 bytes per metadata value, 16,384 per document, 32 metadata keys of at most 36 bytes, 8 predicates per filter. A single Chroma server has none of them.

## Deleting records

```csharp
var deleted = await collectionClient.DeleteAsync(new ChromaDelete { WhereDocument = ChromaWhereDocumentOperator.Contains("draft"), Limit = 100 });
```

`ChromaDelete` holds the ids, the filters and the limit of a delete.

- Without ids it deletes by the filters only. Without ids and filters it throws an `ArgumentException`, since it would select every record.
- As in Chroma and its Python client, the ids cannot be an empty list, and the limit needs a `where` or `where_document` filter and cannot be negative.
- Chroma 1.5.3 and later apply `Limit` and answer how many records they deleted, which `DeleteAsync` returns. On earlier servers it returns null.
- Earlier servers ignore the limit and would delete every matching record. So before a delete with a limit, the client reads the OpenAPI description of the server once, and throws a `ChromaException` without sending the delete if the limit is not declared there.

## Embeddings in base64

Where `pre-flight-checks` declares `supports_base64_encoding`, from Chroma 1.0.13, `AddAsync`, `UpdateAsync` and `UpsertAsync` send the embeddings as base64 strings of their float32 values, about half the size of the numbers. The server stores the same values. Queries always send numbers, because the servers reject base64 there. Elsewhere, or when `pre-flight-checks` does not answer, the embeddings go as numbers.

## Tenants and databases

```csharp
await client.CreateTenantAsync("my_tenant");
await client.CreateDatabaseAsync("my_database", tenant: "my_tenant");

var options = new ChromaConfigurationOptions(uri: "http://localhost:8000", tenant: "my_tenant", database: "my_database");
```

The collections created with these options belong to that tenant and database.

```csharp
var databases = await client.ListDatabasesAsync(tenant: "my_tenant");
var page = await client.ListDatabasesAsync(limit: 10, offset: 20, tenant: "my_tenant");
await client.DeleteDatabaseAsync("my_database", tenant: "my_tenant");
```

`ListDatabasesAsync` and `DeleteDatabaseAsync` need the v2 API of Chroma 0.6.3 or later. Older servers answer `405 Method Not Allowed`.

## Collections by id

```csharp
var collection = await client.GetCollectionByIdAsync(id);
```

`GetCollectionByIdAsync` looks for the id in the tenant and database of the options, or in the ones you pass. It needs the v2 API of Chroma 1.5.7 or later; older servers answer `404 Not Found`.

A `ChromaClient` hands out the clients for the records of its collections, with its options and `HttpClient`, without sending a request. The id and the name are enough:

```csharp
var collectionClient = client.GetCollectionClient(collection);
var sameCollection = client.GetCollectionClient(collectionId, "my_collection");

// or without a ChromaClient; the tenant and database come from the options
var standalone = new ChromaCollectionClient(collectionId, "my_collection", options, httpClient);
```

## Distance of a collection

```csharp
var collection = await client.CreateCollectionAsync(new ChromaCollectionDefinition("my_collection")
{
	Configuration = new() { Space = ChromaSpace.Cosine },
});
Console.WriteLine(collection.Space);
```

`ChromaSpace` is `L2` (Chroma's default), `Cosine` or `InnerProduct`. The client sends it as the `hnsw:space` metadata, which every tested Chroma applies. `GetOrCreateCollectionAsync` takes a `ChromaCollectionDefinition` too.

`ChromaCollection.Space` reads the space back from that metadata, or from the configuration that Chroma 1.0.6 and later and Chroma Cloud send. It is null for a collection created without a space on the older servers, which do not report it reliably. `ChromaCollection.ConfigurationJson` holds the configuration as the server sends it.

The configuration of a new collection also takes the settings of its vector index and the embedding function it declares:

```csharp
var local = await client.CreateCollectionAsync(new ChromaCollectionDefinition("articles")
{
	Configuration = new() { Space = ChromaSpace.Cosine, Hnsw = new() { EfConstruction = 200, MaxNeighbors = 32 } },
});
var cloud = await cloudClient.CreateCollectionAsync(new ChromaCollectionDefinition("articles")
{
	Configuration = new() { Space = ChromaSpace.Cosine, Spann = new() { SearchNprobe = 32, WriteNprobe = 16 }, EmbeddingFunction = ChromaEmbeddingFunctionReference.Known("openai", new Dictionary<string, object> { ["model_name"] = "text-embedding-3-small" }) },
});
```

- **`Hnsw`**, the index of a single Chroma server: `EfConstruction`, `EfSearch`, `MaxNeighbors`, `ResizeFactor`, `SyncThreshold`, `BatchSize` and `NumThreads`. They go as `hnsw:` metadata, like the space, which every tested Chroma applies. Chroma 1.0.6 and later report them in the configuration.
  - `MaxNeighbors` must be at least 2, here, in the `hnsw:M` metadata and in `ModifyConfigurationAsync`. Chroma 1.5.9 crashes on the first write with 0 and misses the nearest records with 1, so the client throws an `ArgumentException` for them.
  - In the metadata, `hnsw:M`, `hnsw:construction_ef`, `hnsw:search_ef`, `hnsw:num_threads`, `hnsw:batch_size` and `hnsw:sync_threshold` are integers. Chroma rejects `100.0` for them, so the client sends a whole `double`, `float` or `decimal` as an integer and throws an `ArgumentException` for any other number.
- **`Spann`**, the index of Chroma Cloud:
  - `SearchNprobe`, `WriteNprobe`, `EfConstruction`, `EfSearch`, `MaxNeighbors`, `SplitThreshold`, `MergeThreshold` and `ReassignNeighborCount` go in the `configuration` of the request, with the space, because Chroma ignores the `hnsw:space` metadata next to SPANN settings.
  - `SearchRngEpsilon`, `WriteRngEpsilon`, `NreplicaCount`, `NumSamplesKmeans`, `NumCentersToMergeTo` and `CenterDriftThreshold` go in a schema, the only place Chroma takes them.
  - Chroma Cloud keeps the other SPANN settings fixed: the RNG factors at 1, `initial_lambda` at 100, and the quantization, which users cannot set.
- **`EmbeddingFunction`** goes in the `configuration` of the request, which Chroma 1.0.0 and later take. Chroma 1.0.6 and later report it.
- **What the client checks:**
  - A collection has one vector index. `Hnsw` and `Spann` together throw an `ArgumentException`, as Chroma rejects them.
  - Chroma Cloud ignores `Hnsw`, and a single server ignores `Spann`. Then `CreateCollectionAsync` deletes the collection and throws a `ChromaException`, and `GetOrCreateCollectionAsync` throws and keeps it. Chroma 1.0.0 to 1.0.5 report no configuration, so there the client cannot tell.
  - Chroma 0.x fails on the `configuration` of the request or ignores it. With `Spann` or `EmbeddingFunction`, the client throws a `ChromaException` before sending it.
- **With a schema**, every setting goes in the schema, on the vector index of `#embedding`, as `create_index(VectorIndexConfig(...))` of the Python client writes them.

`ChromaCollection.Dimension` is the number of dimensions of the embeddings, set by the first write. `Version` and `LogPosition` are the version of the collection and its position in the log, as the server reports them. Chroma 0.5.0 and earlier send no dimension and no version, and 0.5.7 and earlier no log position.

## Settings of the index

```csharp
await collectionClient.ModifyConfigurationAsync(new() { Hnsw = new() { EfSearch = 200 } });
```

`ModifyConfigurationAsync` changes the index settings that Chroma allows after the creation:

- the HNSW settings, like `EfSearch`;
- the `EfSearch` and `SearchNprobe` of the SPANN index of Chroma Cloud;
- the embedding function the collection declares, `EmbeddingFunction`.

Chroma 1.0.6 and later apply them. Earlier versions answer without applying them. The client recognizes those versions from the configuration they send with the collection, and throws a `ChromaException` without sending the request.

The settings must be those of the index of the collection: `Hnsw` on a single Chroma server, `Spann` on Chroma Cloud. Chroma Cloud answers `500` to `Hnsw` settings, and a single server accepts `Spann` settings without applying them. So for the settings of the other index, the client throws a `ChromaException` without sending them. On Chroma Cloud:

```csharp
await collectionClient.ModifyConfigurationAsync(new() { Spann = new() { SearchNprobe = 32 } });
```

## Health of the server

```csharp
var health = await client.HealthcheckAsync();
Console.WriteLine(health.IsExecutorReady);
```

`HealthcheckAsync` needs Chroma 1.0.0 or later; the 0.x servers answer `404 Not Found`. A server that is not ready answers `503`, and the client throws a `ChromaException`.

## Traces and metrics

```csharp
builder.Services.AddOpenTelemetry()
	.WithTracing(tracing => tracing.AddSource(ChromaTelemetry.ActivitySourceName))
	.WithMetrics(metrics => metrics.AddMeter(ChromaTelemetry.MeterName));
```

- **Spans:** each operation, like `query` or `add`, is a client span named after the operation and the collection, like `query articles`. Its requests, the batches of a large write too, are inside it.
- **Attributes**, from the OpenTelemetry semantic conventions for database clients:
  - `db.system.name` `chroma`, `db.operation.name` and `db.collection.name`;
  - `db.namespace`, the tenant and the database as `tenant|database`;
  - `server.address` and `server.port`;
  - on a failure, `error.type` and `db.response.status_code`, the HTTP status.
- **Metrics:** the duration of each operation in the `db.client.operation.duration` histogram, in seconds, with the same attributes.
- **Buckets of the histogram:** the boundaries the semantic conventions advise, 0.001, 0.005, 0.01, 0.05, 0.1, 0.5, 1, 5 and 10 seconds. OpenTelemetry 1.10 and later apply them. Without them, OpenTelemetry would use its default boundaries, made for milliseconds, and every operation under 5 seconds would fall in the same bucket. The netstandard2.0 build, used by applications on .NET Core 3.1 to 7, cannot advise them, so there the application adds a view with them:

  ```csharp
  metrics.AddView("db.client.operation.duration", new ExplicitBucketHistogramConfiguration { Boundaries = [0.001, 0.005, 0.01, 0.05, 0.1, 0.5, 1, 5, 10] });
  ```
- **Cost:** without a listener, nothing is measured. A missing collection in `CollectionExistsAsync` is an answer, not an error.

## Chroma Cloud

```csharp
var options = new ChromaConfigurationOptions(uri: "https://api.trychroma.com").WithChromaToken(apiKey);
var client = await new ChromaClient(options, httpClient).WithTenantAndDatabaseFromIdentityAsync();
```

From a connection string, as the settings of an application keep it:

```csharp
var options = ChromaConfigurationOptions.FromConnectionString("Endpoint=https://api.trychroma.com;Token=ck-...;Tenant=...;Database=...");
```

`Endpoint` is the URI of the server, and a connection string that is just a URI is the endpoint alone. `Token` goes in the `X-Chroma-Token` header. `Tenant` and `Database` are the ones of the requests.

`WithTenantAndDatabaseFromIdentityAsync` takes the tenant and the database from the credentials, as the `CloudClient` of the Python client does:

- an API key for one database gives both;
- an API key for a whole tenant gives only the tenant, so the database goes in the options;
- a tenant or database set in the options, other than the default ones, must match the one the key gives, when it gives one;
- a single Chroma server always answers with `default_tenant` and `default_database`.

These operations exist only on Chroma Cloud; a single Chroma server answers them with an error.

```csharp
var copy = await collectionClient.ForkAsync("my_collection_copy");
var forks = await collectionClient.ForkCountAsync();
var status = await collectionClient.GetIndexingStatusAsync();
```

`ForkAsync` copies a collection with its records under a new name. `GetIndexingStatusAsync` tells how many writes are indexed.

On every server, `CountAsync(ChromaReadLevel.IndexOnly)` counts only the records already indexed. Chroma Cloud indexes them later, so right after a write the count can be lower, even 0, while `CountAsync()` already sees them. A single server indexes them at once and gives the same count.

```csharp
await collectionClient.AddAsync(ids, embeddings: embeddings);                  // 6 records
var all = await collectionClient.CountAsync();                                 // 6
var indexed = await collectionClient.CountAsync(ChromaReadLevel.IndexOnly);    // on Chroma Cloud, from 0 to 6 until they are indexed
```

```csharp
var (attached, created) = await collectionClient.AttachFunctionAsync(ChromaFunctions.Statistics, "my_stats", "my_stats_output");
var function = await collectionClient.GetAttachedFunctionAsync("my_stats");
await collectionClient.DetachFunctionAsync("my_stats", deleteOutputCollection: true);
```

The functions of Chroma Cloud, `ChromaFunctions.Statistics` and `ChromaFunctions.RecordCounter`, run on the records of a collection and write their results to an output collection.

```csharp
await client.UpdateTenantAsync("my_tenant", resourceName: "my_org");
var collection = await client.GetCollectionByCrnAsync("my_org:my_database:my_collection");
```

`UpdateTenantAsync` sets the resource name of a tenant, which `GetTenantAsync` returns as `ResourceName`. `GetCollectionByCrnAsync` gets a collection by its Chroma Resource Name. A single Chroma server from 1.0.17 accepts `UpdateTenantAsync` but does not keep the name.

`GetCollectionByCrnAsync` is there as in the JavaScript client of Chroma, but the operation is hidden in the OpenAPI description of Chroma and missing from its documentation. On Chroma Cloud it got `403 Permission denied` with an API key limited to one database and with an API key for the whole tenant, also for a collection of that tenant.

## Search

Only Chroma Cloud serves the Search API of Chroma; a single Chroma server answers `501`.

```csharp
var results = await collectionClient.SearchAsync(new ChromaSearch
{
	Where = ChromaWhereOperator.GreaterThanOrEqual("year", 2021),
	WhereDocument = ChromaWhereDocumentOperator.Contains("apple"),
	Rank = ChromaRank.Knn(new([0.1f, 0.2f, 0.3f])),
	Limit = 10,
	Select = [ChromaSearchKeys.Document, ChromaSearchKeys.Score, "title"],
});
foreach (var result in results)
{
	Console.WriteLine($"{result.Id} {result.Score} {result.Document} {result.Metadata?["title"]}");
}
```

`ChromaSearch` holds the filters, which combine with `$and`, the ids, the ranking, the page and the fields to return. Without `Select`, a search returns only the ids. The records with the lowest score come first.

`ChromaRank` builds the ranking:

- `Knn` ranks by distance from a vector;
- `Value`, `Sum`, `Multiply`, `Max`, `Min`, `Abs`, `Exp`, `Log` and the operators `+ - * /` combine rankings;
- `Rrf` fuses several rankings, built as the Python client builds it.

```csharp
var fused = ChromaRank.Rrf([ChromaRank.Knn(vector1, returnRank: true), ChromaRank.Knn(vector2, returnRank: true)]);
var perCategory = new ChromaSearchGroupBy(ChromaSearchAggregate.MinK(3, ChromaSearchKeys.Score), "category");
var many = await collectionClient.SearchAsync([new ChromaSearch { Rank = fused, Limit = 5 }, new ChromaSearch { GroupBy = perCategory, Rank = fused }]);
```

`ChromaSearchGroupBy` keeps, for each value of the metadata keys, the records the aggregate chooses. Several searches go in one request, and their results come back in order. `ToString()` of a `ChromaRank` returns its JSON, where a text query of `SparseKnn` appears as text; `SearchAsync` sends its sparse vector.

`SearchAsync(search, ChromaReadLevel.IndexOnly)` leaves out the records not indexed yet. `ChromaSearchAggregate.MinK` keeps the records with the lowest values of the keys, the best ranked with `ChromaSearchKeys.Score`, and `MaxK` those with the highest.

## Sparse vectors and schema

Chroma Cloud keeps sparse vectors, like the BM25 vectors of the documents, in a metadata key with a sparse vector index, which the schema of the collection declares:

```csharp
var collection = await client.CreateCollectionAsync(new ChromaCollectionDefinition("articles")
{
	Schema = new ChromaCollectionSchema().WithSparseVectorIndex("doc_bm25", ChromaSearchKeys.Document, bm25: true, ChromaEmbeddingFunctionReference.ChromaBm25()),
});
var collectionClient = client.GetCollectionClient(collection);
await collectionClient.AddAsync(new ChromaRecords(["a"])
{
	Embeddings = [embedding],
	Documents = ["apple pie"],
	Metadatas = [new Dictionary<string, object> { ["doc_bm25"] = new ChromaSparseVector([17, 4242], [0.8f, 1.1f]) }],
});
var results = await collectionClient.SearchAsync(new ChromaSearch { Rank = ChromaRank.SparseKnn(queryVector, "doc_bm25"), Limit = 10 });
```

- `ChromaSparseVector` holds the indices, in strictly ascending order, their values, and optionally the tokens. It is a metadata value, written as the Python client writes it: `{"#type": "sparse_vector", "indices": [...], "values": [...]}`.
- **Reading it back:** a sparse vector comes back as a `ChromaSparseVector`, or as a `JsonElement` with `ChromaMetadataValues.Inferred`.
- **Where it works:**
  - only Chroma Cloud stores and indexes sparse vectors;
  - a single server from Chroma 1.0.0 rejects them;
  - Chroma 0.x would drop them without an error, so the client throws a `ChromaException` before sending them.
- **`ChromaCollectionSchema`** declares the indexes of a new collection:
  - With `bm25`, the server applies the inverse document frequency of BM25. A source key needs an embedding function, because Chroma Cloud rejects one without the other.
  - `ChromaEmbeddingFunctionReference.ChromaBm25()` declares the BM25 function of Chroma with the settings of its Python client, so the clients that know it compute the vectors.
  - `ChromaCollection.SparseVectorIndexes` and `ChromaCollection.SchemaJson` read it back. `EmbeddingFunctionConfig` of an index holds the settings of its function, and `Bm25Function` the `ChromaBm25` with those settings. `FindBm25Index(sourceKey)` returns the BM25 index on the text of a key, like `#document`, or null.
  - `ToString()` returns the JSON the client sends.
- **The indexes of the values**, like `create_index` and `delete_index` of the Python client. `WithIndex` and `WithoutIndex` turn on or off:
  - the index of the string, integer, floating-point or Boolean values (`ChromaSchemaIndex.StringInverted`, `IntInverted`, `FloatInverted`, `BoolInverted`) of a metadata key, or of every key without a setting of its own;
  - the full-text search index of the documents (`FullTextSearch`), on `#document` only.

  They are all on by default. A filter on a key without its index fails with a `ChromaException`, "indexing is disabled". Chroma 1.3.0 to 1.5.0 keep the full-text search index also when the schema turns it off; 1.5.1 and later turn it off.

  ```csharp
  var schema = new ChromaCollectionSchema()
  	.WithoutIndex(ChromaSchemaIndex.StringInverted, "body")
  	.WithoutIndex(ChromaSchemaIndex.FullTextSearch);
  ```
- **On Chroma Cloud only:**
  - the algorithm of a sparse vector index, `WithSparseVectorIndex(key, ChromaSparseIndexAlgorithm.MaxScore, ...)`, which `ChromaSparseVectorIndex.Algorithm` reads back, for the tenants that have it;
  - a customer-managed key of Google Cloud KMS, `WithGcpCmek("projects/.../locations/.../keyRings/.../cryptoKeys/...")`, like `set_cmek` of the Python client.
- **The space with a schema:** `Configuration = new() { Space = ... }` goes in the schema, on `#embedding`, as `create_index(VectorIndexConfig(space=...))` of the Python client writes it, and not in the `hnsw:space` metadata. Chroma rejects the two together: "Cannot set both collection config and schema simultaneously".
- **Where the schema works:**
  - Chroma 1.3.0 and later apply it;
  - a single server rejects a sparse vector index;
  - Chroma 1.0.0 to 1.2.2 and 0.6.3 create the collection without the schema. Then `CreateCollectionAsync` deletes it and throws a `ChromaException`, and `GetOrCreateCollectionAsync` throws and keeps it, since it may have existed before;
  - Chroma 1.3.0 ignores the space in the schema, which 1.3.2 and later apply, so the client does the same when the collection has another space.

## Hybrid search with BM25

```csharp
var collection = await client.CreateCollectionAsync(new ChromaCollectionDefinition("articles")
{
	Schema = new ChromaCollectionSchema().WithSparseVectorIndex("doc_bm25", ChromaSearchKeys.Document, bm25: true, new ChromaBm25().Reference),
});
var collectionClient = client.GetCollectionClient(collection);
await collectionClient.AddAsync(new ChromaRecords(ids) { Embeddings = embeddings, Documents = documents });
var results = await collectionClient.SearchAsync(new ChromaSearch
{
	Rank = ChromaRank.Rrf([ChromaRank.Knn(queryEmbedding, returnRank: true), ChromaRank.SparseKnn(queryText, "doc_bm25", returnRank: true)]),
	Limit = 10,
	Select = [ChromaSearchKeys.Document, ChromaSearchKeys.Score],
});
```

- **What `ChromaBm25` computes:** the BM25 vectors as the Python client computes them, `chroma_bm25` with the Snowball English stemmer of snowballstemmer 3.1.1. The same text gives the same indices and values in .NET and in Python, so a collection written by one is searched by the other.
- **How it was tested:** against the Python client on more than 5,000 texts, with the characters of every Unicode script that Python 3.13 knows, on .NET 8 and on .NET Framework, outside the CI. On every change, `ChromaBm25Tests` compares the vectors of a few texts with those of Python. It follows the Unicode rules of Python from its own tables, not those of the runtime.
- **What the client computes, as the Python client does:**
  - In `AddAsync`, `UpdateAsync` and `UpsertAsync`, the vectors of each sparse vector index of the schema with a source key and `chroma_bm25`, from the document or from the text in the metadata key, with the settings of the schema. A record whose metadata already has the key keeps its vector. The records and the metadata you pass do not change.
  - In `SearchAsync`, the vector of the text of `SparseKnn(queryText, key)`, with the function of the index of the key.
  - The schema comes with the collection, from `CreateCollectionAsync` or `GetCollectionAsync`. A collection client created from an id alone has no schema, so a text query throws a `ChromaException`. So does a function other than `chroma_bm25`, unless the metadata has the vectors.
- **By hand:** `new ChromaBm25()` with the same settings, or `Bm25Function` of the index, gives the vectors to put in the metadata or in `SparseKnn`. `Embed(text)` returns the vector of a text, and `Reference` declares the function in the schema.
- **Other functions:** `ChromaEmbeddingFunctionReference.Known(name, config)` declares another function that the clients of Chroma know. The client only declares it.
- **Records without the terms of the query:** Chroma Cloud ranks them too, among the `limit` of `SparseKnn`, with the score 1, one minus the dot product. With `returnRank` they take the next positions, so in `Rrf` they get points from the sparse part as well. `ChromaRank.HybridRrf(embedding, text, key, limit)` fuses the dense search and the BM25 search of the text so that only the records with a term of the text get points from it; the others keep the order of the dense search.
- **License:** the license of the stemmer is in [THIRD-PARTY-NOTICES.md](https://github.com/ChromaDotNet/ChromaDB.Client/blob/main/THIRD-PARTY-NOTICES.md).

## Authentication

```csharp
var options = new ChromaConfigurationOptions(uri: "http://localhost:8000");

// X-Chroma-Token: <token>, the header of Chroma Cloud.
var tokenOptions = options.WithChromaToken("token");

// Authorization: Bearer <token>, the default of the token authentication of the Chroma 0.x servers.
var bearerOptions = options.WithChromaToken("token", ChromaTokenTransportHeader.Authorization);

// Authorization: Basic, for the Chroma 0.x servers with basic authentication.
var basicOptions = options.WithBasicAuth("admin", "password");
```

The client adds the credentials to each of its requests, without changing the `HttpClient` you pass. Chroma 1.x servers have no built-in authentication.

## Trimming and NativeAOT

The client serializes with metadata generated at build time, so it works in applications published with trimming or NativeAOT, where serialization by reflection is off. The packages are marked `IsAotCompatible`.

The values of metadata and filters can be `string`, the numeric types, `bool`, `DateTime`, `DateTimeOffset`, `Guid`, `JsonElement`, arrays and lists of strings, numbers and booleans, `List<object>` and `object[]`. Other types work only where reflection is on, as without trimming.

The CI publishes `Samples/ChromaDB.Client.TrimmingTest` with trimming and with NativeAOT, with every warning as an error, and runs it against Chroma 1.5.9. With `CHROMA_HOST`, `CHROMA_API_KEY`, `CHROMA_TENANT` and `CHROMA_DATABASE`, it runs against Chroma Cloud.

## Tests

`dotnet test` starts a Chroma container for each test fixture: `chromadb/chroma:0.6.3`, unless `CHROMA_IMAGE` names another image. `CHROMA_TEST_API_VERSION=v1` runs the tests with the v1 API.

With `CHROMA_TEST_URI`, the tests run against a server that is already running, like Chroma Cloud, and take it for the latest Chroma unless `CHROMA_IMAGE` says otherwise:

- `CHROMA_TEST_TOKEN` goes in `X-Chroma-Token`;
- `CHROMA_TEST_TENANT` and `CHROMA_TEST_DATABASE` are used as they are, not created;
- `CHROMA_TEST_MAX_BATCH_SIZE` is a batch limit lower than the one the server declares, like 300 on Chroma Cloud.

Each fixture deletes the collections and the databases its requests created, and nothing else. The tests that reset the server, create or look up other tenants and databases, or query an id that does not exist are skipped.

## Migrating from ChromaDB.Client 1.x

- Replace the `ChromaDB.Client` package reference with `ChromaDotNet.Client`.
- The namespaces do not change: keep `using ChromaDB.Client;`, and `using ChromaDB.Client.Models;` for the models.
- Use the `/api/v2/` URI, or just the address of the server. Chroma 1.x answers the v1 routes with `410 Gone`.
- The asynchronous methods end in `Async`: `GetOrCreateCollection` → `GetOrCreateCollectionAsync`, `Add` → `AddAsync`, `Query` → `QueryAsync`, and so on.
- Every asynchronous method takes an optional `CancellationToken` as its last parameter. Code compiled against 1.x has to be rebuilt, and a method group like `client.Heartbeat` passed as a `Func<Task>` becomes `() => client.HeartbeatAsync()`.
- Parameters and results are `IReadOnlyList<T>` and `IReadOnlyDictionary<string, object>`. A `List<T>` or a collection expression still goes in, and metadata are written `new Dictionary<string, object> { ["key"] = value }`.
- A record has `Embedding` and `Uri` instead of `Embeddings` and `Uris`; `Data` is gone.
- `ChromaCollectionQueryEntry.Distance` is a `float?`, null when the query does not include `ChromaQueryInclude.Distances`.
- `new ChromaConfigurationOptions(uri, tenant: ..., database: ...)` replaces `defaultTenant:` and `defaultDatabase:`.
- Strings in metadata stay strings, and lists are `List<object>`. `WithMetadataValues(ChromaMetadataValues.Inferred)` reads them as 1.x did, with dates as `DateTime`.
- `ChromaWhere` and `ChromaWhereDocument` are gone: use `ChromaWhereOperator` and `ChromaWhereDocumentOperator`.
- Writes go in batches and `GetAsync` reads in pages. `WithBatchSplitting(false)` sends each of them in one request, as 1.x did.

The [migration guide](https://github.com/ChromaDotNet/ChromaDB.Client/blob/main/v2-migration/MIGRATION_GUIDE_V2.md) has the details, and the [release notes](https://github.com/ChromaDotNet/ChromaDB.Client/releases) list the changes of every version.

## About this fork

The original project was created by [ssone95](https://github.com/ssone95) and largely written by [cincuranet](https://github.com/cincuranet). Its last change dates from February 2025, and it still targets the Chroma v1 API, which Chroma 1.x no longer serves.

This fork:

- integrates the pending upstream pull requests [80](https://github.com/ssone95/ChromaDB.Client/pull/80), by [inlineHamed](https://github.com/inlineHamed), and [82](https://github.com/ssone95/ChromaDB.Client/pull/82), by [richlander](https://github.com/richlander), which move the client to the v2 API;
- tests the client against several Chroma versions, with its .NET 8 and .NET Standard 2.0 builds;
- works through the issues reported upstream, one pull request each.

The original commits of the upstream pull requests, which were squash-merged, are kept in the [`upstream-history`](https://github.com/ChromaDotNet/ChromaDB.Client/tree/upstream-history) branch.

The project is released under the MIT License, keeping the original copyright notice.
