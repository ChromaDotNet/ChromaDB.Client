# ChromaDB.Client

_ChromaDB.Client_ is a .NET SDK that offers a seamless connection to the Chroma database. It allows creating and managing collections, performing CRUD operations, and executing nearest neighbor search and filtering.

> This is the community-maintained continuation of [ssone95/ChromaDB.Client](https://github.com/ssone95/ChromaDB.Client), kept up to date with current Chroma versions. It is an independent project and is not affiliated with or endorsed by Chroma. See [About this fork](#about-this-fork).

## Compatibility

| Chroma server | API | Tested |
|---|---|---|
| 0.5.16 – 1.5.9 | v2, the default | all the tests pass on each release tested |
| 0.5.1 – 0.5.15 | v1, with `ChromaApiVersion.V1` | all the tests pass on each release tested |
| 0.4.10 – 0.5.0 | v1, with `ChromaApiVersion.V1` | collections and records work on each release tested; some of these servers miss tenants, `CountCollectionsAsync` or the `$not_contains` filter |
| Chroma Cloud | v2 | the tests pass against it, apart from the operations it does not allow to an API key, like `CreateTenantAsync` and `ResetAsync`; see [Chroma Cloud](#chroma-cloud) |

Each release tested, the differences between them and the versions in the CI are listed in [docs/COMPATIBILITY.md](https://github.com/ChromaDotNet/ChromaDB.Client/blob/main/docs/COMPATIBILITY.md).

Since Chroma 1.0.16 the server requires embeddings in `AddAsync` and `UpsertAsync`: the client does not compute them, so pass them explicitly.

The package targets .NET 8 and .NET Standard 2.0; the tests run against both builds.

## Installation

```
dotnet add package ChromaDotNet.Client
```

`ChromaDotNet.Client.DependencyInjection` adds the registration for `Microsoft.Extensions.DependencyInjection`: `AddChromaClient`, and `AddKeyedChromaClient` for more than one server, tenant or database under different keys. Both register the `ChromaClient` as a singleton, so also singletons can take it. Its `HttpClient` sends each request with the current handler of `IHttpClientFactory`, which the factory renews after its handler lifetime, two minutes by default, so a change of the address of the server in the DNS is seen on every target. What the client learns about the server, like its version, is asked again after two minutes. An overload configures that `HttpClient`, like a resilience handler, a proxy or a timeout: `services.AddChromaClient(_ => options, builder => builder.AddStandardResilienceHandler())`. A client kept elsewhere, like in an integration with an `HttpClient` name of its own, is made the same way by `serviceProvider.CreateChromaClient(options, httpClientName)`. Only the releases are published to NuGet: every change merged into `main` builds the packages, which its CI run keeps as artifacts.

## Example

The URI can be just the address of the server, like `http://localhost:8000`: the client adds `/api/v2/`. A URI with a path, like `http://localhost:8000/api/v2`, is used as it is, with or without the trailing slash.

Without an `HttpClient`, `new ChromaClient("http://localhost:8000")`, or `new ChromaClient(options)`, creates one of its own, which `Dispose` closes: `using var client = new ChromaClient("http://localhost:8000");`. The clients it returns, like the collection clients, use the same `HttpClient`. An `HttpClient` given to the constructor stays open.

```csharp
using ChromaDB.Client;

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

## API version

The client uses the v2 API. For the servers that have only the v1 API, like Chroma 0.5.15, choose it once in the options:

```csharp
var options = new ChromaConfigurationOptions(uri: "http://localhost:8000").WithApiVersion(ChromaApiVersion.V1);

// or, with dependency injection
services.AddChromaClient(options => options!.WithUri("http://localhost:8000").WithApiVersion(ChromaApiVersion.V1));
```

Chroma 0.5.16 to 0.5.20 serve both APIs. The v1 API of Chroma 0.6.3 fails on many requests, and Chroma 1.x answers it with `410 Gone`: use v2 there.

## Records with URIs

```csharp
await collectionClient.AddAsync(new ChromaRecords(["a"]) { Embeddings = [new([1f, 0.5f, 0f])], Uris = ["s3://bucket/a.png"] });

var entries = await collectionClient.GetAsync(["a"], include: ChromaGetInclude.Uris);
Console.WriteLine(entries[0].Uri);
```

`ChromaRecords` holds the ids, embeddings, metadatas, documents and URIs of the records for `AddAsync`, `UpdateAsync` and `UpsertAsync`.

## Querying some records only

```csharp
var results = await collectionClient.QueryAsync(new ChromaQuery([new([1f, 0.5f, 0f])]) { Ids = ["a", "c"], NResults = 1 });
```

`ChromaQuery` holds the query embeddings, the number of results, the filters, what to include and the ids to search among. Chroma 1.0.0 and later search only the records with those ids. Chroma 0.x ignores them and searches all the records: when a result falls outside the ids, `QueryAsync` throws a `ChromaException` instead of returning it.

## Metadata values

By default, `ChromaMetadataValues.Exact`, strings in metadata stay strings, and lists come back as `List<object>` of `string`, `long`, `double` and `bool`, like the single values. With `ChromaMetadataValues.Inferred`, as before 2.8.0, a string that looks like a date comes back as a `DateTime`, and a list as a `JsonElement`:

```csharp
var options = new ChromaConfigurationOptions(uri: "http://localhost:8000").WithMetadataValues(ChromaMetadataValues.Inferred);
```

A list in metadata, written and filtered:

```csharp
await collectionClient.AddAsync(new ChromaRecords(["a"]) { Embeddings = [new([1f, 0.5f, 0f])], Metadatas = [new() { ["tags"] = new[] { "red", "blue" } }] });
var tagged = await collectionClient.GetAsync(where: ChromaWhereOperator.Contains("tags", "red"));
```

Chroma 1.5.0 and later store lists in metadata and filter them with `Contains` and `NotContains`. Chroma 1.0.0 to 1.4.1 reject them. Chroma 0.x accepts them but drops them without an error, so `AddAsync`, `UpdateAsync` and `UpsertAsync` throw a `ChromaException` before sending them: the client asks the server its version once, only when a record has a list.

A `ChromaClient` that already exists, for example from dependency injection, gives one that reads the other way, with the same `HttpClient`, options and what it learned about the server; `Options` returns the options of a client:

```csharp
var inferred = client.WithMetadataValues(ChromaMetadataValues.Inferred);
Console.WriteLine(inferred.Options.MetadataValues); // Inferred
```

## Errors

A failed request throws a `ChromaException`: an error answer of the server, a network error, an answer that is not the expected JSON, or a timeout, with the original exception as `InnerException`. Other exceptions, like an assembly that does not load or a disposed `HttpClient`, go as they are. Its `StatusCode` is the status code of the answer of the server, or null when there was no answer, like on a timeout. Its `ErrorType` is the kind of error the server names: `NotFoundError` or `InvalidArgumentError` from Chroma 1.x, `InvalidCollection` from Chroma 0.5 and 0.6, `ValueError` from the v1 API of Chroma 0.4, null when it names none.

```csharp
if (!await client.CollectionExistsAsync("my_collection"))
{
	await client.CreateCollectionAsync("my_collection");
}
```

When the 0.x servers reject a request with validation errors, the message lists them, like `body.n_results: Input should be a valid integer`.

`CollectionExistsAsync` tells a missing collection from the other errors on every tested server: Chroma 1.x answers `404`, the 0.x servers `400` or `500`, always with "does not exist" in the message. Any other error, like a bare `404` from a wrong address, throws.

## Filters

`ToString()` of a `ChromaWhereOperator` or a `ChromaWhereDocumentOperator` is the JSON the client sends:

```csharp
var where = ChromaWhereOperator.Equal("year", 2026) & ChromaWhereOperator.In("lang", "en", "it");
Console.WriteLine(where); // {"$and":[{"year":{"$eq":2026}},{"lang":{"$in":["en","it"]}}]}
```

`In` and `NotIn` without values throw an `ArgumentException`: every tested Chroma rejects `$in` and `$nin` without values.

`ChromaWhereDocumentOperator.Regex` and `NotRegex` filter the documents with a regular expression, from Chroma 1.0.12; the earlier versions fail on them.

```csharp
var apples = await collectionClient.GetAsync(whereDocument: ChromaWhereDocumentOperator.Regex("^apple"));
```

## Large writes

By default `AddAsync`, `UpdateAsync`, `UpsertAsync` and `DeleteAsync` send their records in batches of the `max_batch_size` of the server, one request after the other; the client asks `pre-flight-checks` once. If a batch fails, the earlier ones stay written. Chroma 0.4.10 has no `pre-flight-checks`, and a server whose answer the client cannot read gets the records in one request too. `WithBatchSplitting(false)` sends them in one request, as before 2.8.0: up to Chroma 1.0.13 a request beyond the limit fails, later versions accept it.

```csharp
var options = new ChromaConfigurationOptions(uri: "http://localhost:8000").WithBatchSplitting(false);
```

`GetAsync` reads more records than the batch size in pages: pages of the batch size from the offset, until the limit or the last record, and ids beyond the batch size in batches, with the limit and the offset applied to all of them together. The pages are separate requests: records written in between can be read twice or missed.

`WithBatchSplitting(maxBatchSize)` uses the smaller of that limit and the one of the server, or that limit alone where the server declares none. Chroma Cloud declares 1000, but takes 300 records per write and answers at most 300 records per read, without an error, unless the quota is raised. So on Chroma Cloud, at `*.trychroma.com`, the client uses 300 when no limit is given. A server that rejects a batch beyond its quota of records, "current usage of 301 exceeds limit of 300" as Chroma Cloud answers before writing any of it, gets that batch and the rest in batches of the quota, and the next writes too. A raised quota needs its limit:

```csharp
var options = new ChromaConfigurationOptions(uri: "https://api.trychroma.com").WithChromaToken(apiKey)
	.WithTenant(tenant).WithDatabase(database)
	.WithBatchSplitting(maxBatchSize: 1000); // a quota raised to 1000 records
```

## Deleting records

```csharp
var deleted = await collectionClient.DeleteAsync(new ChromaDelete { WhereDocument = ChromaWhereDocumentOperator.Contains("draft"), Limit = 100 });
```

`ChromaDelete` holds the ids, the filters and the limit of a delete. Without ids it deletes by the filters only; without ids and filters it throws an `ArgumentException`, since it would select every record. As in Chroma and its Python client, the ids cannot be an empty list, and the limit needs a `where` or `where_document` filter and cannot be negative. Chroma 1.5.3 and later apply `Limit` and answer how many records they deleted, which `DeleteAsync` returns; on the earlier servers it returns null. Those servers ignore the limit and would delete every matching record: before a delete with a limit the client reads the OpenAPI description of the server, once, and throws a `ChromaException` without sending the delete if it does not declare the limit.

## Embeddings in base64

Where `pre-flight-checks` declares `supports_base64_encoding`, from Chroma 1.0.13, `AddAsync`, `UpdateAsync` and `UpsertAsync` send the embeddings as base64 strings of their float32 values, about half the size of the numbers; the server stores the same values. Queries always send numbers, since the servers reject base64 there. Elsewhere, or when `pre-flight-checks` does not answer, the embeddings go as numbers.

## Tenants and databases

```csharp
await client.CreateTenantAsync("my_tenant");
await client.CreateDatabaseAsync("my_database", tenant: "my_tenant");

var options = new ChromaConfigurationOptions(uri: "http://localhost:8000", defaultTenant: "my_tenant", defaultDatabase: "my_database");
```

The collections created with these options belong to that tenant and database.

```csharp
var databases = await client.ListDatabasesAsync(tenant: "my_tenant");
var page = await client.ListDatabasesAsync(limit: 10, offset: 20, tenant: "my_tenant");
await client.DeleteDatabaseAsync("my_database", tenant: "my_tenant");
```

`ListDatabasesAsync` and `DeleteDatabaseAsync` need the v2 API of Chroma 0.6.3 or later: the older servers answer `405 Method Not Allowed`.

## Collections by id

```csharp
var collection = await client.GetCollectionByIdAsync(id);
```

`GetCollectionByIdAsync` looks for the id in the tenant and database of the options, or in the ones it is given. It needs the v2 API of Chroma 1.5.7 or later: the older servers answer `404 Not Found`.

A `ChromaClient` hands out the clients for the records of its collections, with its options and `HttpClient` and without sending a request, also from the id and the name alone:

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

`ChromaSpace` is `L2` (the default of Chroma), `Cosine` or `InnerProduct`. The client sends it as the `hnsw:space` metadata, which every tested Chroma applies; `GetOrCreateCollectionAsync` takes a `ChromaCollectionDefinition` too. `ChromaCollection.Space` reads it back from that metadata, or from the configuration that Chroma 1.0.6 and later and Chroma Cloud send; it is null for a collection created without a space on the older servers, which do not report it reliably. `ChromaCollection.ConfigurationJson` holds the configuration as the server sends it.

## Settings of the index

```csharp
await collectionClient.ModifyConfigurationAsync(new() { Hnsw = new() { EfSearch = 200 } });
```

`ModifyConfigurationAsync` changes the settings of the index that Chroma lets change after the creation: those of HNSW, like `EfSearch`, and those of the SPANN index of Chroma Cloud, `EfSearch` and `SearchNprobe`. Chroma 1.0.6 and later apply them. The earlier versions answer without applying them: the client tells them by the configuration they send with the collection, and throws a `ChromaException` without sending the request.

The settings must be those of the index of the collection: `Hnsw` on a single Chroma server, `Spann` on Chroma Cloud. Chroma Cloud answers `500` to `Hnsw` settings, and a single server answers without applying `Spann` settings: for the settings of the other index the client throws a `ChromaException` without sending them. On Chroma Cloud:

```csharp
await collectionClient.ModifyConfigurationAsync(new() { Spann = new() { SearchNprobe = 32 } });
```

## Health of the server

```csharp
var health = await client.HealthcheckAsync();
Console.WriteLine(health.IsExecutorReady);
```

`HealthcheckAsync` needs Chroma 1.0.0 or later: the 0.x servers answer `404 Not Found`. A server that is not ready answers `503`, a `ChromaException`.

## Traces and metrics

```csharp
builder.Services.AddOpenTelemetry()
	.WithTracing(tracing => tracing.AddSource(ChromaTelemetry.ActivitySourceName))
	.WithMetrics(metrics => metrics.AddMeter(ChromaTelemetry.MeterName));
```

- **Spans:** each operation, like `query` or `add`, is a client span named after the operation and the collection, like `query articles`. Its requests, the batches of a large write too, are inside it.
- **Attributes:** those of the OpenTelemetry semantic conventions for database clients:
  - `db.system.name` `chroma`, `db.operation.name` and `db.collection.name`;
  - `db.namespace`, the tenant and the database as `tenant|database`;
  - `server.address` and `server.port`;
  - on a failure, `error.type` and `db.response.status_code`, the HTTP status.
- **Metrics:** the duration of each operation in the histogram `db.client.operation.duration`, in seconds, with the same attributes.
- **Cost:** without a listener nothing is measured. A missing collection in `CollectionExistsAsync` is an answer, not an error.

## Chroma Cloud

```csharp
var options = new ChromaConfigurationOptions(uri: "https://api.trychroma.com").WithChromaToken(apiKey);
var client = await new ChromaClient(options, httpClient).WithTenantAndDatabaseFromIdentityAsync();
```

From a connection string, as the settings of an application keep it:

```csharp
var options = ChromaConfigurationOptions.FromConnectionString("Endpoint=https://api.trychroma.com;Token=ck-...;Tenant=...;Database=...");
```

`Endpoint` is the URI of the server, and a connection string that is just a URI is the endpoint alone. `Token` goes in the `X-Chroma-Token` header; `Tenant` and `Database` are the ones of the requests.

`WithTenantAndDatabaseFromIdentityAsync` takes the tenant and the database from the credentials, as the `CloudClient` of the Python client of Chroma does: an API key for one database gives both, an API key for a whole tenant gives only the tenant, so the database goes in the options. A tenant or a database set in the options, other than the default ones, must match the one the key gives, when it gives one. A single Chroma server always answers with `default_tenant` and `default_database`.

These operations exist on Chroma Cloud only; a single Chroma server answers them with an error.

```csharp
var copy = await collectionClient.ForkAsync("my_collection_copy");
var forks = await collectionClient.ForkCountAsync();
var status = await collectionClient.GetIndexingStatusAsync();
var indexed = await collectionClient.CountAsync(ChromaReadLevel.IndexOnly);
```

`ForkAsync` copies a collection with its records under a new name. `GetIndexingStatusAsync` tells how many writes are indexed. `CountAsync(ChromaReadLevel.IndexOnly)` counts only the records already indexed: Chroma Cloud indexes them later, so right after a write the count can be lower, even 0, while `CountAsync()` already sees them. A single server indexes them at once and gives the same count.

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

`UpdateTenantAsync` sets the resource name of a tenant, which `GetTenantAsync` returns as `ResourceName`, and `GetCollectionByCrnAsync` gets a collection by its Chroma Resource Name. A single Chroma server from 1.0.17 accepts `UpdateTenantAsync` but does not keep the name.

`GetCollectionByCrnAsync` is there as in the official JavaScript client of Chroma, but the operation is hidden in the OpenAPI description of Chroma and missing from its documentation. On Chroma Cloud, `GetCollectionByCrnAsync` sent with an API key limited to one database, and with an API key for the whole tenant, got `403 Permission denied`, also for a collection of that tenant.

## Search

The Search API of Chroma, which only Chroma Cloud serves; a single Chroma server answers `501`.

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

`ChromaSearch` holds the filters, which combine with `$and`, the ids, the ranking, the page and the fields to return. Without `Select` a search returns only the ids. The records with the lowest score come first.

`ChromaRank` builds the ranking:
- `Knn` ranks by distance from a vector;
- `Value`, `Sum`, `Multiply`, `Max`, `Min`, `Abs`, `Exp`, `Log` and the operators `+ - * /` combine rankings;
- `Rrf` fuses several rankings, built as the Python client of Chroma builds it.

```csharp
var fused = ChromaRank.Rrf([ChromaRank.Knn(vector1, returnRank: true), ChromaRank.Knn(vector2, returnRank: true)]);
var perCategory = new ChromaSearchGroupBy(ChromaSearchAggregate.MinK(3, ChromaSearchKeys.Score), "category");
var many = await collectionClient.SearchAsync([new ChromaSearch { Rank = fused, Limit = 5 }, new ChromaSearch { GroupBy = perCategory, Rank = fused }]);
```

`ChromaSearchGroupBy` keeps, for each value of the metadata keys, the records the aggregate chooses. Several searches go in one request, and their results come in order. `ToString()` of a `ChromaRank` gives its JSON; a text query of `SparseKnn` is in it as the text, where `SearchAsync` sends its sparse vector.

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
	Metadatas = [new() { ["doc_bm25"] = new ChromaSparseVector([17, 4242], [0.8f, 1.1f]) }],
});
var results = await collectionClient.SearchAsync(new ChromaSearch { Rank = ChromaRank.SparseKnn(queryVector, "doc_bm25"), Limit = 10 });
```

- `ChromaSparseVector` holds the indices, in strictly ascending order, and their values, and optionally the tokens. It is a metadata value; it is written as the Python client of Chroma writes it, `{"#type": "sparse_vector", "indices": [...], "values": [...]}`.
- **Reading it back:** a sparse vector comes back as a `ChromaSparseVector`; with `ChromaMetadataValues.Inferred`, as a `JsonElement`.
- **Where it works:**
  - only Chroma Cloud stores sparse vectors and indexes them;
  - a single server from Chroma 1.0.0 rejects them;
  - Chroma 0.x would drop them without an error, so the client throws a `ChromaException` before sending them.
- **`ChromaCollectionSchema`** declares the indexes of a new collection:
  - With `bm25` the server applies the inverse document frequency of BM25. A source key needs an embedding function, as Chroma Cloud rejects one without the other.
  - `ChromaEmbeddingFunctionReference.ChromaBm25()` declares the BM25 function of Chroma with the settings of its Python client, so that the clients that know it compute the vectors.
  - `ChromaCollection.SparseVectorIndexes` and `ChromaCollection.SchemaJson` read it back. `EmbeddingFunctionConfig` of an index holds the settings of its function, and `Bm25Function` the `ChromaBm25` with those settings.
  - `ToString()` gives the JSON the client sends.
- **The space with a schema:** `Configuration = new() { Space = ... }` goes in the schema, on `#embedding`, as `create_index(VectorIndexConfig(space=...))` of the Python client writes it, and not in the `hnsw:space` metadata. Chroma rejects the two together: "Cannot set both collection config and schema simultaneously".
- **Where the schema works:**
  - Chroma 1.3.0 and later apply it;
  - a single server rejects a sparse vector index;
  - Chroma 1.0.0 to 1.2.2 and 0.6.3 create the collection without the schema: `CreateCollectionAsync` then deletes it and throws a `ChromaException`, and `GetOrCreateCollectionAsync` throws and keeps it, since it may have existed before.
  - Chroma 1.3.0 ignores the space in the schema, which 1.3.2 and later apply: the same then, when the collection has another space.

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

- **What `ChromaBm25` computes:** the BM25 vectors as the Python client of Chroma computes them, `chroma_bm25` with the Snowball English stemmer of snowballstemmer 3.1.1. The same text gives the same indices and values in .NET and in Python, so a collection written by one is searched by the other.
- **How it was tested:** against the Python client on more than 5,000 texts, with the characters of every Unicode script that Python 3.13 knows, on .NET 8 and on .NET Framework. It follows the Unicode rules of Python from its own tables, not those of the runtime.
- **What the client computes, as the Python client of Chroma does:**
  - In `AddAsync`, `UpdateAsync` and `UpsertAsync`, the vectors of each sparse vector index of the schema with a source key and `chroma_bm25`, from the document or from the text in the metadata key, with the settings of the schema. A record whose metadata already has the key keeps its vector. The records and the metadata you pass do not change.
  - In `SearchAsync`, the vector of the text of `SparseKnn(queryText, key)`, with the function of the index of the key.
  - The schema comes with the collection, from `CreateCollectionAsync` or `GetCollectionAsync`. A collection client created from an id alone has none, so a text query throws a `ChromaException`; with another function than `chroma_bm25` too, unless the metadata has the vectors.
- **By hand:** `new ChromaBm25()` with the same settings, or `Bm25Function` of the index, gives the vectors to put in the metadata or in `SparseKnn`. `Reference` declares the function in the schema.
- **Records without the terms of the query:** Chroma Cloud ranks them too, among the `limit` of `SparseKnn`, with the score 1, one minus the dot product. With `returnRank` they take the next positions, so in `Rrf` they get points from the sparse part as well.
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

The client adds the credentials to each of its requests, without changing the `HttpClient` it is given. Chroma 1.x servers have no built-in authentication.

## Trimming and NativeAOT

The client serializes with metadata generated at build time, so it works in applications published with trimming or NativeAOT, where serialization by reflection is off; the packages are marked `IsAotCompatible`. The values of metadata and filters can be `string`, the numeric types, `bool`, `DateTime`, `DateTimeOffset`, `Guid`, `JsonElement`, and arrays and lists of strings, numbers and booleans, as well as `List<object>` and `object[]`. Other types work only where reflection is on, as without trimming.

The CI publishes `Samples/ChromaDB.Client.TrimmingTest` with trimming and with NativeAOT, with every warning as an error, and runs it against Chroma 1.5.9. With `CHROMA_HOST`, `CHROMA_API_KEY`, `CHROMA_TENANT` and `CHROMA_DATABASE` it runs against Chroma Cloud.

## Tests

`dotnet test` starts a Chroma container for each test fixture, `chromadb/chroma:0.6.3` unless `CHROMA_IMAGE` names another image; `CHROMA_TEST_API_VERSION=v1` runs the tests with the v1 API.

With `CHROMA_TEST_URI` the tests run against a server already running, like Chroma Cloud, and take the server for the latest Chroma unless `CHROMA_IMAGE` says otherwise:

- `CHROMA_TEST_TOKEN` goes in `X-Chroma-Token`;
- `CHROMA_TEST_TENANT` and `CHROMA_TEST_DATABASE` are used as they are, not created;
- `CHROMA_TEST_MAX_BATCH_SIZE` is a batch limit lower than the one the server declares, like 300 on Chroma Cloud.

Each fixture deletes the collections and the databases its requests created, and nothing else. The tests that reset the server, create or look up other tenants and databases, or query an id that does not exist, are skipped.

## Migrating from ChromaDB.Client 1.x

- Use the `/api/v2/` URI, or just the address of the server. Chroma 1.x answers the v1 routes with `410 Gone`.
- Namespaces do not change: the code keeps `using ChromaDB.Client;`.
- Replace the `ChromaDB.Client` package reference with `ChromaDotNet.Client`.
- `ChromaCollectionQueryEntry.Distance` is a `float?`: it is `null` when the query does not include `ChromaQueryInclude.Distances`.
- Every async method takes an optional `CancellationToken` as its last parameter. Code compiled against 1.x has to be rebuilt, and a method group like `client.HeartbeatAsync` passed as a `Func<Task>` becomes `() => client.Heartbeat()`.

The [migration guide](https://github.com/ChromaDotNet/ChromaDB.Client/blob/main/v2-migration/MIGRATION_GUIDE_V2.md) has the details.

## Status

[![NuGet](https://img.shields.io/nuget/v/ChromaDotNet.Client)](https://www.nuget.org/packages/ChromaDotNet.Client/)
[![CI](https://img.shields.io/github/actions/workflow/status/ChromaDotNet/ChromaDB.Client/ci.yml?branch=main)](https://github.com/ChromaDotNet/ChromaDB.Client/actions/workflows/ci.yml)
[![License](https://img.shields.io/github/license/ChromaDotNet/ChromaDB.Client)](https://github.com/ChromaDotNet/ChromaDB.Client/blob/main/LICENSE)

## About this fork

The original project was created by [ssone95](https://github.com/ssone95) and largely written by [cincuranet](https://github.com/cincuranet). Its last change dates from February 2025, and it still targets the Chroma v1 API, which Chroma 1.x no longer serves.

This fork:

- integrates the pending upstream pull requests [80](https://github.com/ssone95/ChromaDB.Client/pull/80), by [inlineHamed](https://github.com/inlineHamed), and [82](https://github.com/ssone95/ChromaDB.Client/pull/82), by [richlander](https://github.com/richlander), which migrate the client to the v2 API;
- tests the client against several Chroma versions and against both target frameworks;
- works through the issues reported upstream, one pull request each.

The original commits of the upstream pull requests, which were squash-merged, are kept in the [`upstream-history`](https://github.com/ChromaDotNet/ChromaDB.Client/tree/upstream-history) branch.

The project is released under the MIT License, keeping the original copyright notice.
