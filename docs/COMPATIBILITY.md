# Compatibility with Chroma

On 3 October 2026 the whole test suite of the client ran against each Chroma release listed on this page, using the official Docker image `chromadb/chroma`. The page lists only those runs: a version that is not here was not tested.

The suite covers collections (create, get, list, count, modify, delete), records (add, update, upsert, delete, get, query, count, peek, filters), tenants and databases, error messages, cancellation, and the built-in authentication of the server where noted.

## v2 API (the default)

All the tests pass on:

0.5.16 – 0.5.18, 0.5.20 – 0.5.21, 0.5.23, 0.6.0 – 0.6.3, 1.0.0, 1.0.2 – 1.0.10, 1.0.12 – 1.0.13, 1.0.15 – 1.0.21, 1.1.0 – 1.1.1, 1.2.0 – 1.2.2, 1.3.0, 1.3.2 – 1.3.3, 1.3.5 – 1.3.7, 1.4.0 – 1.4.1, 1.5.0 – 1.5.9

Differences between these servers, seen in the tests:

| Behavior | Versions |
|---|---|
| Rejects embeddings of different dimensions in the same request | 0.5.20 – 1.5.9; 0.5.16 – 0.5.18 accept them |
| Requires embeddings in `Add` and `Upsert` | 1.0.16 – 1.5.9; the earlier versions accept records without embeddings |
| Built-in authentication: token in `X-Chroma-Token` or `Authorization: Bearer`, and basic | 0.5.16 – 0.6.3; Chroma 1.5.9 accepts requests without credentials |
| Lists and deletes databases (`ListDatabases`, `DeleteDatabase`) | 0.6.3 – 1.5.9; 0.5.16 – 0.6.2 answer `405 Method Not Allowed` |
| Gets a collection by its id (`GetCollectionById`) | 1.5.7 – 1.5.9; 0.5.16 – 1.5.6 answer `404 Not Found` |
| Stores lists in metadata, and filters them with `ChromaWhereOperator.Contains` and `NotContains` | 1.5.0 – 1.5.9; 1.0.0 – 1.4.1 reject the lists with `422`; 0.5.16 – 0.6.3 drop the lists without an error, so the client throws a `ChromaException` before sending them, and reject `$contains` |
| Reports the space of a collection created without one (`ChromaCollection.Space`) | 1.0.6 – 1.5.9 report `L2`; 0.5.16 – 1.0.5 send `hnsw_configuration.space`, always "l2", so `Space` is null |
| Searches only the records with the ids of `ChromaQuery.Ids` | 1.0.0 – 1.5.9, which answer `500` with `Error finding id` when one of the ids does not exist; 0.5.16 – 0.6.3 ignore the ids: `Query` throws a `ChromaException` when a result falls outside them |

## v1 API (`ChromaApiVersion.V1`)

All the tests pass on, including the built-in authentication:

0.5.1 – 0.5.7, 0.5.9 – 0.5.18, 0.5.20 – 0.5.21, 0.5.23

On these older servers collections and records work; some features are missing on the server, and the client reports them with a `ChromaException`:

| Versions | Missing on the server |
|---|---|
| 0.4.23, 0.4.24, 0.5.0 | nothing among the features above; the built-in authentication was not tested |
| 0.4.15 | `CountCollections`; the `$not_contains` document filter; the tenant and database of the collections it returns; records in the collections of a tenant or database other than the default: the server answers that the collection does not exist; the `limit` and `offset` of `ListCollections`: the server returns all the collections; the URIs of the records: the server rejects `uris` in `include` |
| 0.4.10, 0.4.12 – 0.4.14 | tenants and databases; `CountCollections`; the `$not_contains` document filter; the `limit` and `offset` of `ListCollections`: the server returns all the collections; the URIs of the records: the server rejects `uris` in `include` |

Chroma 0.4.10 has no `pre-flight-checks` either, so `GetPreFlightChecks` answers `404 Not Found` there.

A missing endpoint gives a message that names the request, like `Not Found: POST /api/v1/tenants`. The v1 API has no `auth/identity`, so `GetUserIdentity` needs the v2 API: Chroma 0.5.15, 0.5.16 and 0.6.3 answer it with `404 Not Found` in v1. The v1 API does not list or delete databases either: `ListDatabases` and `DeleteDatabase` answer `405 Method Not Allowed` on the servers above from 0.4.15. `GetCollectionById` answers `404 Not Found` on all the servers above. They all ignore `ChromaQuery.Ids`, as Chroma 0.6.3 does with the v2 API, drop lists in metadata, so the client throws a `ChromaException` before sending them, and reject `$contains`.

A collection created with `ChromaCollectionConfiguration.Space` uses that space on all the servers on this page, and `ChromaCollection.Space` reads it back: the client sends it as the `hnsw:space` metadata. The `configuration` field of the request is not used: Chroma 0.4.10 – 0.5.3 ignore it and keep `l2`, and 0.5.4 – 0.6.3 answer it with `500`.

`pre-flight-checks` declares `supports_base64_encoding` from Chroma 1.0.13: there `Add`, `Update` and `Upsert` take the embeddings as base64 strings and store the same float32 values, and the client sends them so; queries take only numbers. Chroma 1.0.12 and earlier do not declare it, reject base64 embeddings with `422`, and get numbers.

`CollectionExists` recognizes a missing collection on all the servers on this page: Chroma 1.x answers `404`, Chroma 0.5.6 – 0.6.3 `400`, and Chroma 0.4.10 – 0.5.5 `500`, always with "does not exist" in the message.

The `max_batch_size` of `pre-flight-checks` is 41666 on Chroma 0.4.12 – 0.6.3 and 5461 on 1.x. A single request beyond it fails up to Chroma 1.0.13 (`400` or `500`); Chroma 1.0.15 – 1.5.9 accept it. With `WithBatchSplitting` the client sends batches within the limit, and writes beyond it work on all the servers that have `pre-flight-checks`: Chroma 0.4.10 has none, so its records go in one request.

All the servers on this page reject `$in` and `$nin` without values (`400` or `500`), so `ChromaWhereOperator.In` and `NotIn` without values throw an `ArgumentException`.

The v1 API of Chroma 0.6.3 fails on most requests, and Chroma 1.5.9 answers it with `410 Gone`: use the v2 API there.

## Chroma Cloud

Checked on 4 October 2026 against `api.trychroma.com`: the key goes in `X-Chroma-Token`, the default of `WithChromaToken`, since `Authorization: Bearer` gets `401`. `pre-flight-checks` declares a `max_batch_size` of 1000 and `supports_base64_encoding`, but a write of more than 300 records gets `422` with `Quota exceeded`, the default quota; `WithBatchSplitting(maxBatchSize: 300)` writes and deletes 301 records in two batches. Embeddings sent in base64 read back identical. A missing collection gets `404` with `NotFoundError`.

## Versions tested in the CI

Every change runs the whole suite against:

- v2 API: 0.5.16, 0.5.20, 0.6.3, 1.0.0, 1.5.9, and `latest` without blocking;
- v1 API: 0.4.10, 0.4.15, 0.4.23, 0.5.15.

On the older servers the tests of the features they miss are skipped, with the reason.
