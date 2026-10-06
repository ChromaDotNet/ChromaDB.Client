# Compatibility with Chroma

On 3 October 2026 the whole test suite of the client ran against each Chroma release listed on this page, using the `chromadb/chroma` Docker image published by Chroma. The page lists only those runs: a version that is not here was not tested.

The suite covers collections (create, get, list, count, modify, delete), records (add, update, upsert, delete, get, query, count, peek, filters), tenants and databases, error messages, cancellation, and the built-in authentication of the server where noted.

## v2 API (the default)

All the tests pass on:

0.5.16 – 0.5.18, 0.5.20 – 0.5.21, 0.5.23, 0.6.0 – 0.6.3, 1.0.0, 1.0.2 – 1.0.10, 1.0.12 – 1.0.13, 1.0.15 – 1.0.21, 1.1.0 – 1.1.1, 1.2.0 – 1.2.2, 1.3.0, 1.3.2 – 1.3.3, 1.3.5 – 1.3.7, 1.4.0 – 1.4.1, 1.5.0 – 1.5.9

Differences between these servers, seen in the tests:

| Behavior | Versions |
|---|---|
| Rejects embeddings of different dimensions in the same request | 0.5.20 – 1.5.9; 0.5.16 – 0.5.18 accept them |
| Requires embeddings in `AddAsync` and `UpsertAsync` | 1.0.16 – 1.5.9; the earlier versions accept records without embeddings |
| Built-in authentication: token in `X-Chroma-Token` or `Authorization: Bearer`, and basic | 0.5.16 – 0.6.3; Chroma 1.5.9 accepts requests without credentials |
| Lists and deletes databases (`ListDatabasesAsync`, `DeleteDatabaseAsync`) | 0.6.3 – 1.5.9; 0.5.16 – 0.6.2 answer `405 Method Not Allowed` |
| Gets a collection by its id (`GetCollectionByIdAsync`) | 1.5.7 – 1.5.9; 0.5.16 – 1.5.6 answer `404 Not Found` |
| Stores lists in metadata, and filters them with `ChromaWhereOperator.Contains` and `NotContains` | 1.5.0 – 1.5.9; 1.0.0 – 1.4.1 reject the lists with `422`; 0.5.16 – 0.6.3 drop the lists without an error, so the client throws a `ChromaException` before sending them, and reject `$contains` |
| Reports the space of a collection created without one (`ChromaCollection.Space`) | 1.0.6 – 1.5.9 report `L2`; 0.5.16 – 1.0.5 send `hnsw_configuration.space`, always "l2", so `Space` is null |
| Rejects sparse vectors in metadata (only Chroma Cloud stores them) | 1.0.0 – 1.5.9; 1.0.21 – 1.1.1 then fail the next write on the server, also to another collection, with `Error sending message to compactor`, so the tests leave that write out there |
| Searches only the records with the ids of `ChromaQuery.Ids` | 1.0.0 – 1.5.9, which answer `500` with `Error finding id` when one of the ids does not exist; 0.5.16 – 0.6.3 ignore the ids: `QueryAsync` throws a `ChromaException` when a result falls outside them |
| Has the healthcheck (`HealthcheckAsync`) | 1.0.0 – 1.5.9; 0.5.16 – 0.6.3 answer `404 Not Found` |
| Applies a new configuration of the index (`ModifyConfigurationAsync`) | 1.0.6 – 1.5.9; the earlier versions answer without applying it, so the client throws a `ChromaException` |
| Filters documents with `$regex` and `$not_regex` (`ChromaWhereDocumentOperator.Regex` and `NotRegex`) | 1.0.12 – 1.5.9; the earlier versions fail with a `ChromaException`: 1.0.0 – 1.0.6 reject them, 1.0.10 closes the connection |
| Applies the space of a collection created with a schema | 1.3.2 – 1.5.9; 1.3.0 accepts it and ignores it; 1.0.0 – 1.2.2 create the collection without the schema |
| Applies the limit of a delete (`ChromaDelete.Limit`) and answers how many records it deleted | 1.5.3 – 1.5.9; on the earlier versions the client throws a `ChromaException` before a delete with a limit, and `DeleteAsync` returns null |
| Reports the HNSW settings of a new collection (`ChromaCollectionConfiguration.Hnsw`), sent as the `hnsw:` metadata | 1.0.6 – 1.5.9; the earlier versions take the metadata without reporting it |
| Applies the SPANN settings of a new collection (`ChromaCollectionConfiguration.Spann`) | Chroma Cloud only; 1.0.6 – 1.5.9 ignore them and report an HNSW index, so the client deletes the collection and throws a `ChromaException`; 1.0.0 – 1.0.5 report no configuration; 0.5.16 – 0.6.3 fail on the configuration, so the client throws before sending it |
| Applies the embedding function of a new collection (`ChromaCollectionConfiguration.EmbeddingFunction`) and reports it | 1.0.6 – 1.5.9; 1.0.0 – 1.0.5 take it without reporting it; 0.5.16 – 0.6.3 fail on the configuration, so the client throws before sending it |
| Applies the embedding function of `ModifyConfigurationAsync` | 1.0.6 – 1.5.9, as the other settings of the index |
| Applies the indexes of a schema turned on or off (`ChromaCollectionSchema.WithIndex` and `WithoutIndex`), and rejects a filter on a key without its index ("indexing is disabled") | 1.3.0 – 1.5.9, but the full-text search index of the documents only from 1.5.1: 1.3.0 – 1.5.0 keep it on; 1.0.0 – 1.2.2 create the collection without the schema |
| Takes an HNSW index with fewer than 2 neighbors (`max_neighbors`, `hnsw:M`) | 1.5.9 crashes on the first write with 0 and misses the nearest records with 1, also after `ModifyConfigurationAsync`: the client throws an `ArgumentException` before the request |
| Sends the dimension and the version of a collection (`ChromaCollection.Dimension` and `Version`) | 0.5.16 – 1.5.9, and on the v1 API from 0.5.1; the log position (`LogPosition`) from 0.5.9 |

## v1 API (`ChromaApiVersion.V1`)

All the tests pass on, including the built-in authentication:

0.5.1 – 0.5.7, 0.5.9 – 0.5.18, 0.5.20 – 0.5.21, 0.5.23

On these older servers collections and records work; some features are missing on the server, and the client reports them with a `ChromaException`:

| Versions | Missing on the server |
|---|---|
| 0.4.23, 0.4.24, 0.5.0 | nothing among the features above; the built-in authentication was not tested |
| 0.4.15 | `CountCollectionsAsync`; the `$not_contains` document filter; the tenant and database of the collections it returns; records in the collections of a tenant or database other than the default: the server answers that the collection does not exist; the `limit` and `offset` of `ListCollectionsAsync`: the server returns all the collections; the URIs of the records: the server rejects `uris` in `include` |
| 0.4.10, 0.4.12 – 0.4.14 | tenants and databases; `CountCollectionsAsync`; the `$not_contains` document filter; the `limit` and `offset` of `ListCollectionsAsync`: the server returns all the collections; the URIs of the records: the server rejects `uris` in `include` |

Chroma 0.4.10 has no `pre-flight-checks` either, so `GetPreFlightChecksAsync` answers `404 Not Found` there.

A missing endpoint gives a message that names the request, like `Not Found: POST /api/v1/tenants`. The v1 API has no `auth/identity`, so `GetUserIdentityAsync` needs the v2 API: Chroma 0.5.15, 0.5.16 and 0.6.3 answer it with `404 Not Found` in v1. The v1 API does not list or delete databases either: `ListDatabasesAsync` and `DeleteDatabaseAsync` answer `405 Method Not Allowed` on the servers above from 0.4.15. `GetCollectionByIdAsync` answers `404 Not Found` on all the servers above. They all ignore `ChromaQuery.Ids`, as Chroma 0.6.3 does with the v2 API, drop lists in metadata, so the client throws a `ChromaException` before sending them, and reject `$contains`.

A collection created with `ChromaCollectionConfiguration.Space` uses that space on all the servers on this page, and `ChromaCollection.Space` reads it back: the client sends it as the `hnsw:space` metadata. The `configuration` field of the request is not used: Chroma 0.4.10 – 0.5.3 ignore it and keep `l2`, and 0.5.4 – 0.6.3 answer it with `500`.

`pre-flight-checks` declares `supports_base64_encoding` from Chroma 1.0.13: there `AddAsync`, `UpdateAsync` and `UpsertAsync` take the embeddings as base64 strings and store the same float32 values, and the client sends them so; queries take only numbers. Chroma 1.0.12 and earlier do not declare it, reject base64 embeddings with `422`, and get numbers.

`CollectionExistsAsync` recognizes a missing collection on all the servers on this page: Chroma 1.x answers `404`, Chroma 0.5.6 – 0.6.3 `400`, and Chroma 0.4.10 – 0.5.5 `500`, always with "does not exist" in the message.

The `max_batch_size` of `pre-flight-checks` is 41666 on Chroma 0.4.12 – 0.6.3 and 5461 on 1.x. A single request beyond it fails up to Chroma 1.0.13 (`400` or `500`); Chroma 1.0.15 – 1.5.9 accept it. With `WithBatchSplitting` the client sends batches within the limit, and writes beyond it work on all the servers that have `pre-flight-checks`: Chroma 0.4.10 has none, so its records go in one request.

All the servers on this page reject `$in` and `$nin` without values (`400` or `500`), so `ChromaWhereOperator.In` and `NotIn` without values throw an `ArgumentException`.

The v1 API of Chroma 0.6.3 fails on most requests, and Chroma 1.5.9 answers it with `410 Gone`: use the v2 API there.

## Known defects of the servers

Reproduced with plain HTTP, without the client, on 5 October 2026. The client cannot work around them.

| Defect | Versions |
|---|---|
| `$contains` and `$not_contains` on documents read `_` and `%` as SQL wildcards: `ChromaWhereDocumentOperator.Contains("a_b")` also finds `xacby` and `a b`, `Contains("50%")` also finds `sale 500 off`, and `NotContains("a_b")` leaves them out. On 1.0.0 – 1.0.12 a text with `%` between spaces, like `" 50% "`, finds nothing. The server puts the text in an SQLite `LIKE` without an escape character, so the client cannot escape them | all the tested versions from 0.4.10 to 1.0.12; right from 1.0.13 |
| While 24 threads add, update and delete records of their own, a query with `n_results` 3 and a `where` filter on a thread's records returns 4 of them, on a server just started | 0.4.24, in 5 runs out of 10 |
| The same query returns no record | 0.6.3, in 1 run out of 10 |
| A list of `$and` or `$or` becomes an SQLite expression as deep as the list: from 988 filters in one list Chroma 1.5.9 answers `500`, and from about 4,400 it crashes. The client splits long lists, as the README says; beyond 8,167 filters Chroma 1.5.9 answers `500` with `too many SQL variables` | single servers 1.0.0 – 1.5.9; 0.6.3 answers `500` from about 490 filters however they go |

| `$contains` and `$not_contains` on documents do not see the text after a NUL character (`\u0000`): `Contains("after")` does not find `"before\u0000after"`, which comes back whole | all the tested versions from 0.4.24 to 1.5.9 |
| A float compared with int metadata is truncated: `GreaterThanOrEqual("a", 2.25)` also finds `a = 2`, and `GreaterThan("a", -1.5)` leaves out `a = -1` | 1.0.0 – 1.5.9; right on 0.4.24 – 0.6.3 |
| Some doubles are read with an error in the last digit: `-95.41757424465169`, the shortest text of the double, comes back `-95.41757424465168`, and `1e-28` comes back `9.999999999999999e-29`; `Equal("x", 1e-30)` does not find the record stored with `1e-30`. The client sends the shortest text that reads back as the same double | 1.0.0 – 1.5.9; right on 0.4.24 – 0.6.3 |
| The embeddings of a collection with the `cosine` space come back different in the last bit: `0.91782147` as `0.9178214`, `-0.4` as `-0.39999998` | 1.0.0 – 1.5.9; identical on 0.4.24 – 0.6.3, and with the other spaces |
| In a collection with the `cosine` space, an embedding beyond the range of a float, like `[float.MaxValue, float.MaxValue]`, comes back as `null`, which the client reads as `NaN`, and one near zero, like `[1e-30, 1e-30]`, comes back as `[0, 0]`, at a distance of `-0.4` from `[3, 4]`. In an `l2` collection the distance to the first comes back as `null`, which the client reads as `NaN` | 1.0.0 and 1.5.9; 0.6.3 sends the values as they are, and the `l2` distance as a number beyond the range of a float, which the client reads as an infinity |
| `-0.0` in metadata comes back as `0.0`; in embeddings it keeps its sign | 0.6.3 and 1.5.9 |

The query of the second and third rows returned 3 records in every run on Chroma 0.5.20, 1.0.0 and 1.5.9, 10 runs each on a server just started.

## Chroma Cloud

Checked on 4 October 2026 against `api.trychroma.com`: the key goes in `X-Chroma-Token`, the default of `WithChromaToken`, since `Authorization: Bearer` gets `401`. `pre-flight-checks` declares a `max_batch_size` of 1000 and `supports_base64_encoding`, but a write of more than 300 records gets `422` with `Quota exceeded`, the default quota; `WithBatchSplitting(maxBatchSize: 300)` writes and deletes 301 records in two batches. Embeddings sent in base64 read back identical. A missing collection gets `404` with `NotFoundError`.

A filter has at most 8 predicates, the default quota of a tenant: a `where` with 9, nested ones included, gets `422` with `Quota exceeded: 'Number of where clause predicates'`, and a link to ask for more; the values of an `In` do not count (checked on 5 October 2026).

## Versions tested in the CI

Every change runs the whole suite against:

- v2 API: 0.5.16, 0.5.20, 0.6.3, 1.0.0, 1.5.9, and `latest` without blocking;
- v1 API: 0.4.10, 0.4.15, 0.4.23, 0.5.15.

On the older servers the tests of the features they miss are skipped, with the reason.
