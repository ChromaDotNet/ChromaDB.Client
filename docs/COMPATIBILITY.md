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
| Keeps the stored document when an update or an upsert sends a null document | 1.0.0 – 1.5.9 and Chroma Cloud; every 0.x server deletes it, from 0.4.10 to 0.6.3 on the v1 and the v2 API, and the client sends the null as it is |
| Built-in authentication: token in `X-Chroma-Token` or `Authorization: Bearer`, and basic | 0.5.16 – 0.6.3; Chroma 1.0.0 – 1.5.9 accept requests without credentials, also with the token authentication of 0.x configured |
| Lists and deletes databases (`ListDatabasesAsync`, `DeleteDatabaseAsync`) | 0.6.3 – 1.5.9; 0.5.16 – 0.6.2 answer `405 Method Not Allowed` |
| Gets a collection by its id (`GetCollectionByIdAsync`) | 1.5.7 – 1.5.9; 0.5.16 – 1.5.6 answer `404 Not Found` |
| Stores lists in metadata, and filters them with `ChromaWhereOperator.Contains` and `NotContains` | 1.5.0 – 1.5.9; 1.0.0 – 1.4.1 reject the lists with `422`; 0.5.16 – 0.6.3 drop the lists without an error, so the client throws a `ChromaException` before sending them, and reject `$contains` |
| Reports the space of a collection created without one (`ChromaCollection.Space`) | 1.0.6 – 1.5.9 report `L2`; 0.5.16 – 1.0.5 send `hnsw_configuration.space`, always "l2": `Space` is null on 1.0.0 – 1.0.5, and `L2` on 0.5.16 – 0.6.3, where the client asks the version of the server |
| Rejects sparse vectors in metadata (only Chroma Cloud stores them) | 1.0.0 – 1.5.9; 1.0.21 – 1.1.1 then fail the next write on the server, also to another collection, with `Error sending message to compactor`, so the tests leave that write out there |
| Searches only the records with the ids of `ChromaQuery.Ids` | 1.0.0 – 1.5.9, which answer `500` with `Error finding id` when one of the ids does not exist and the query has no filter: `QueryAsync` then asks again with the ids that exist; 0.5.16 – 0.6.3 ignore the ids: `QueryAsync` throws a `ChromaException` when a result falls outside them |
| Keeps the lists in the metadata of the records of a deleted collection or database, and gives them to the next records it stores, in any collection and database | 1.5.0 – 1.5.9: with `deleteRecordsFirst: true`, `DeleteCollectionAsync`, `DeleteCollectionIfExistsAsync` and `DeleteDatabaseAsync` delete the records first on Chroma 1.x, which leaves no lists |
| Has the healthcheck (`HealthcheckAsync`) | 1.0.0 – 1.5.9; 0.5.16 – 0.6.3 answer `404 Not Found` |
| Applies a new configuration of the index (`ModifyConfigurationAsync`) | 1.0.6 – 1.5.9; the earlier versions answer without applying it, so the client throws a `ChromaException` |
| Filters documents with `$regex` and `$not_regex` (`ChromaWhereDocumentOperator.Regex` and `NotRegex`) | 1.0.12 – 1.5.9; the earlier versions fail with a `ChromaException`: 1.0.0 – 1.0.6 reject them, 1.0.10 closes the connection |
| Applies the space of a collection created with a schema | 1.3.2 – 1.5.9; 1.3.0 accepts it and ignores it; 1.0.0 – 1.2.2 create the collection without the schema |
| Applies the limit of a delete (`ChromaDelete.Limit`) and answers a count of the deleted records, which counts the ids given (KD-53) | 1.5.3 – 1.5.9; on the earlier versions the client throws a `ChromaException` before a delete with a limit, and `DeleteAsync` returns null |
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

Chroma 0.4.23 writes the space of a `get_or_create` into the metadata of a collection that exists, whose index keeps its space: the collection then reports a space it does not use. On the 0.x servers `GetOrCreateCollectionAsync` reads the collection first and throws before the request when it has another space than the definition; on the others the answer tells it.

`CollectionExistsAsync` recognizes a missing collection on all the servers on this page: Chroma 1.x answers `404`, Chroma 0.5.6 – 0.6.3 `400`, and Chroma 0.4.10 – 0.5.5 `500`, always with "does not exist" in the message.

The `max_batch_size` of `pre-flight-checks` is 41666 on Chroma 0.4.12 – 0.6.3 and 5461 on 1.x. A single request beyond it fails up to Chroma 1.0.13 (`400` or `500`); Chroma 1.0.15 – 1.5.9 accept it. With `WithBatchSplitting` the client sends batches within the limit, and writes beyond it work on all the servers that have `pre-flight-checks`: Chroma 0.4.10 has none, so its records go in one request.

All the servers on this page reject `$in` and `$nin` without values (`400` or `500`), so `ChromaWhereOperator.In` without values is `None`, which sends no request, and `NotIn` without values is `All`, which sends no `where`. `$ne` and `$nin` match the records without the key on 0.5.15, and on the v2 API of 0.6.3, 1.0.0 and 1.5.9; 0.4.10 to 0.4.23 leave them out. `$lte` leaves them out on all of them, as `ChromaWhereOperator.Not` describes. A request on the id of a deleted collection gets `500` "coroutine raised StopIteration" from Chroma 0.4.23, without "does not exist", so a collection client made by name compares the ids to find a collection created again.

The v1 API of Chroma 0.6.3 fails on most requests, and Chroma 1.0.0 – 1.5.9 answer it with `410 Gone`: use the v2 API there.

## Known defects of the servers

Each defect has a number, KD-n, to refer to it. Reproduced with plain HTTP, without the client, from 3 to 10 October 2026; KD-51 with the client, as plain HTTP with a single upsert did not show it. The first ones, up to KD-11, the client cannot fully work around: KD-4 says how far its split of long lists goes. The column on the right says what it does for the others.

| KD | Defect | Versions | The client |
|---|---|---|---|
| KD-1 | `$contains` and `$not_contains` on documents read `_` and `%` as SQL wildcards: `ChromaWhereDocumentOperator.Contains("a_b")` also finds `xacby` and `a b`, `Contains("50%")` also finds `sale 500 off`, and `NotContains("a_b")` leaves them out. On 1.0.0 – 1.0.12 a text with `%` between spaces, like `" 50% "`, finds nothing. The server puts the text in an SQLite `LIKE` without an escape character, so the client cannot escape them | all the tested versions from 0.4.10 to 1.0.12; right from 1.0.13 | — |
| KD-2 | While 24 threads add, update and delete records of their own, a query with `n_results` 3 and a `where` filter on a thread's records returns 4 of them, on a server just started | 0.4.24, in 5 runs out of 10 | — |
| KD-3 | The same query returns no record | 0.6.3, in 1 run out of 10 | — |
| KD-4 | A list of `$and` or `$or` becomes an SQLite expression as deep as the list: from 988 filters in one list Chroma 1.5.9 answers `500`, and from about 4,400 it crashes. The client splits long lists, as [queries-and-filters.md](queries-and-filters.md#very-long-filters) says; beyond 8,167 filters Chroma 1.5.9 answers `500` with `too many SQL variables` | single servers 1.0.0 – 1.5.9; 0.6.3 answers `500` from about 490 filters however they go | — |
| KD-5 | `$contains` and `$not_contains` on documents do not see the text after a NUL character (`\u0000`): `Contains("after")` does not find `"before\u0000after"`, which comes back whole | all the tested versions from 0.4.24 to 1.5.9 | — |
| KD-6 | A float compared with int metadata is truncated: `GreaterThanOrEqual("a", 2.25)` also finds `a = 2`, and `GreaterThan("a", -1.5)` leaves out `a = -1` | 1.0.0 – 1.5.9; right on 0.4.24 – 0.6.3 | — |
| KD-7 | Some doubles are read with an error in the last digit: `-95.41757424465169`, the shortest text of the double, comes back `-95.41757424465168`, and `1e-28` comes back `9.999999999999999e-29`; `Equal("x", 1e-30)` does not find the record stored with `1e-30`. The client sends the shortest text that reads back as the same double | 1.0.0 – 1.5.9; right on 0.4.24 – 0.6.3 | — |
| KD-8 | The embeddings of a collection with the `cosine` space come back different in the last bit: `0.91782147` as `0.9178214`, `-0.4` as `-0.39999998` | 1.0.0 – 1.5.9; identical on 0.4.24 – 0.6.3, and with the other spaces | — |
| KD-9, KD-10 | In a collection with the `cosine` space, an embedding beyond the range of a float, like `[float.MaxValue, float.MaxValue]`, comes back as `null`, which the client reads as `NaN`, and one near zero, like `[1e-30, 1e-30]`, comes back as `[0, 0]`, at a distance of `-0.4` from `[3, 4]`. In an `l2` or `ip` collection the distance to the first comes back as `null`, which the client reads as `NaN` | 1.0.0 and 1.5.9; 0.6.3 sends the values as they are, and the `l2` and `ip` distances as numbers beyond the range of a float, like `2.3e77` and `-2.38e39`, which the client reads as infinities | — |
| KD-11 | `-0.0` in metadata comes back as `0.0`; in embeddings it keeps its sign | 0.6.3, 1.0.0 and 1.5.9 | — |
| KD-12 | The lists in the metadata of the records of a deleted collection or database go to the records of the collections created after it: 6 records of 60 without the key came back with the lists of the deleted ones | 1.5.0 – 1.5.9; a deleted database measured on 1.5.0 and 1.5.9 | with `deleteRecordsFirst: true` the deletions delete the records first, in batches |
| KD-13 | A get with `limit: 0` returns every record, from the offset | 0.6.3; 1.0.21 and 1.5.9 return none | returns no record for a limit of 0, without a request |
| KD-14 | `/api/v2/version` answers `1.0.0`: the value is fixed in the server | every 1.x | tells 0.x from 1.x by the version, and the features of 1.x by `pre-flight-checks` and `openapi.json` |
| KD-18 | A list in the metadata of a record is accepted, with `201`, and dropped: the record comes back with null metadata | 0.6.3; 1.0.0 rejects it with `422`, 1.5.9 keeps it | throws a `ChromaException` before sending a list to 0.5.16 – 0.6.3 |
| KD-19 | A tenant or a database with `/` in the name is created, and then a get of it, or of a collection in it, answers `404` | 0.6.3 | sends `%2F` in the path |
| KD-20 | A list in the metadata of a collection makes the server panic and close the connection, without an answer | 1.5.9; 0.6.3 answers `400`, 1.0.0 and 1.3.7 `422`, Chroma Cloud `500` | throws an `ArgumentException` before the request |
| KD-21 | `pre-flight-checks` declares a `max_batch_size` of 1000, but a write of 301 records gets `422` "Quota exceeded" | Chroma Cloud | batches of 300 by default, and of the quota the error names |
| KD-22 | An empty list in the metadata of a record is accepted and dropped without an error | 1.5.9; Chroma Cloud keeps it | `AddAsync` throws an `ArgumentException`; in an update or an upsert an empty list deletes the key |
| KD-23 | In a `cosine` collection of 1000 nearly parallel vectors, a query with `n_results: 1000` returns 998 records in 4 runs of 5, and pages of 10 repeat some records and miss others; `ef_search` 1000 or 2000 does not help, `l2` returns all | 1.5.9 | — |
| KD-24 | A query whose ids include one without a record answers `500` "Error finding id" | 1.0.0 and 1.5.9; Chroma Cloud leaves the id out | asks again with the ids that have a record |
| KD-25 | A change of the configuration with `hnsw` settings answers `500` "failed to merge config into schema" | Chroma Cloud, whose index is SPANN | throws a `ChromaException` before sending `Hnsw` settings to Chroma Cloud |
| KD-26 | `GET /api/v2/collections/{crn}` answers `403` "Permission denied." with a key of the database and with one of the whole tenant, also for a malformed CRN | Chroma Cloud | reports the error of the server |
| KD-27 | Attaching a function the tenant has not enabled answers `429` "Too many requests", also on the first call; detaching too | Chroma Cloud | reports the error of the server, without retrying |
| KD-28 | In the image `chromadb/chroma:1.5.9`, `chroma --version` answers `chroma 1.4.4` | the image of 1.5.9, the same as `latest` | — |
| KD-29 | The v1 API fails with "cannot unpack non-iterable coroutine object" | 0.6.x | use the v2 API there |
| KD-30 | With `CHROMA_ALLOW_RESET=TRUE` the server stops at the start, "expected a boolean"; `ALLOW_RESET=TRUE` is ignored. Only `CHROMA_ALLOW_RESET=true` enables the reset | 1.5.9 | — |
| KD-31 | Updates answer `500` "Error in compaction", and are applied all the same | 1.0.0; not 0.5.20, 0.6.3 and 1.5.9 | — |
| KD-32 | Right after an add, the count is 0 and the peek empty, without an error | 1.0.0; not 0.5.20, 0.6.3 and 1.5.9 | — |
| KD-33 | Embeddings of different dimensions in the same request are accepted | 0.5.16 – 0.5.18; rejected from 0.5.20 | — |
| KD-34 | The Docker images do not start: they install NumPy 2.2.6, and the server exits with "np.float_ was removed in the NumPy 2.0 release" | images 0.4.16 – 0.4.22 | — |
| KD-35 | The Docker image does not start: "Path 'log_config.yml' does not exist" | image 0.4.11 | — |
| KD-36 | A query ignores its ids, without an error, and searches the whole collection | 0.5.16 – 0.6.3 on the v2 API, and every version on the v1 API; 1.0.0 – 1.5.9 apply them | throws a `ChromaException` when a result falls outside the ids |
| KD-37 | `indexing_status` answers `500` "Method scout_logs is not implemented", while the other operations of Chroma Cloud only answer `501` | 1.5.9 single server | reports the error of the server |
| KD-38 | An add to a collection of a tenant or database other than the default ones answers that the collection does not exist | 0.4.15 on the v1 API; right from 0.4.23 | — |
| KD-39 | Collections come back without tenant and database | 0.4.15 on the v1 API; right from 0.4.23 | — |
| KD-40 | The list of the collections ignores `limit` and `offset` and returns every collection | 0.4.10 and 0.4.12 – 0.4.15 on the v1 API; right from 0.4.23 | — |
| KD-41 | A path that does not exist answers `404` with an empty body | 1.0.0 and 1.5.9; the 0.x servers answer with a message | writes the request in the message of the error |
| KD-42 | A missing collection answers "Collection [missing] does not exists" | 1.0.0 | looks for "does not exist", which the text holds |
| KD-43 | A missing collection answers `404` on 1.x, `400` on 0.5.6 – 0.6.3 and `500` on 0.4.10 – 0.5.5 | 0.4.10 – 1.5.9 | `CollectionExistsAsync` recognizes the three, by "does not exist" in the message |
| KD-44 | `configuration.hnsw.space` at the creation is ignored on 0.4.10 – 0.5.3, answers `500` on 0.5.4 – 0.6.3, and is reported as `l2` on 1.0.0 – 1.0.5 | 0.4.10 – 1.0.5 | sends the space as the `hnsw:space` metadata, which every version applies |
| KD-45 | The `max_batch_size` that `pre-flight-checks` declares is not enforced: a write of 5462 records goes | 1.0.15 – 1.5.9; up to 1.0.13 it fails | sends batches within the declared limit |
| KD-46 | `$in` and `$nin` without values answer `500` | 0.4.10 – 0.5.16; `400` from 0.5.17 | sends no request for `In` without values, and no `where` for `NotIn` without values |
| KD-47 | In the OpenAPI description the parameter of `GET` and `DELETE .../collections/{collection_id}` is named as an id, but the server reads a name: with an id it answers `404` (chroma-core/chroma#4456) | 0.5.16 – 1.5.9 | sends the name |
| KD-48 | After the records of a collection are deleted and written again, a query returns wrong neighbors: in a `cosine` collection with three records deleted and added again, `n_results: 1` on the vector of one of them returns the farthest of the three, and `n_results: 2` misses one of the two nearest, and which one changes from run to run; `n_results: 3` returns all three in the right order | 1.0.0 – 1.0.5, also with this client and with the Python client 1.0.0 on 1.0.0 and 1.0.5; right from 1.0.6 | — |
| KD-49 | After an upsert or an update with embeddings of records that exist, also with the embeddings they had, a query can miss a record for good, while `get` and `count` find it: of 60 records with random vectors, after an upsert of 20 of them with new vectors, `n_results: 60` returns 59 in 9 runs of 400 on 1.5.9 with `cosine`, and a query with the vector of the missing record does not find it. The same case with this client and with the Python client 1.5.9 lost a record on 1.5.9 in 11 and in 9 runs of 300, and on Chroma Cloud in none of 300 with either client. Upstream: chroma-core/chroma#7758 | 1.5.9, with this client and with the Python client; 1.0.21, 1.3.7 and 1.5.0 with plain HTTP, with `l2`, `cosine` and `ip`; none in 100 runs on 0.6.3; not seen on Chroma Cloud; the other versions not verified yet | on the server, the environment variable `RAYON_NUM_THREADS=1` made it lose no record in our measurements on 1.5.9; in the client, `WithUpsertStrategy(ChromaUpsertStrategy.SkipUnchangedEmbeddings)` updates without the embedding a record whose embedding does not change, and a record whose embedding changes stays exposed. See [Records lost after an update of their embeddings](#records-lost-after-an-update-of-their-embeddings) |
| KD-50 | A collection created without a space and one created with `configuration.hnsw.space` come back the same: no metadata, and `hnsw_configuration.space` `l2`. The first uses `l2`, the second its space: a query of `[0, 0.1]` on `[3, 4]` gives the distance 24.21 in the first and 0.2 in a `cosine` one | 1.0.0 – 1.0.5, also with this client and with the Python client 1.0.0 on 1.0.0 and 1.0.5; right from 1.0.6 | `Space` is null for a collection without the `hnsw:space` metadata, and `ExpectedSpace` lets it pass, without an exception: use Chroma 1.0.6 or later. The collections the client creates have the metadata, so their space is known |
| KD-51 | After some upserts with the same embeddings, a query with `n_results` equal to the count of the records returns one less, while `get` and `count` find it; after the next write it comes back. On 0.6.3, in a `cosine` collection of 60 records, records were missing in 28 queries of 300 and in 25 of 150, two series of runs; a single upsert, 30 runs with plain HTTP, missed none | 0.5.15, 0.5.16, 0.5.20 and 0.6.3, on the v2 and the v1 API; not on 0.4.10, 0.4.15 and 0.4.23 | — the same with every upsert strategy |
| KD-52 | A query while a record is deleted and added again can return that record with no metadata, which the client reads as an empty `Metadata`; an upsert of the record instead does not do it. In a `cosine` collection of 8 records, one task deleted a record and added it again, with the same metadata, for 15 seconds, while four tasks queried all 8. On 1.5.9: with this client, 14 results in 442 queries, 6 in 278 and 3 in 264; with the Python client 1.5.9, 5 in 337 and 6 in 735; with upserts, none with either client. On Chroma Cloud, none: 450 queries with this client and 387 with the Python client | 1.5.9, with this client and with the Python client; not seen on Chroma Cloud; the other versions not verified yet | — |
| KD-53 | A delete by ids answers as deleted the number of ids it was given, also the ids without a record: in a collection with `a` and `b`, a delete of `zz` answers `{"deleted": 1}` and the count stays 2; a delete of `a` and `zz` answers 2 and the count goes to 1; a delete of three ids without a record answers 3. `DeleteAsync(ChromaDelete)` of this client and `delete` of the Python client 1.5.9 return that number | 1.5.9 and Chroma Cloud, with this client and with the Python client; the other versions not verified yet | returns the number the server answers |
| KD-54 | A filter that compares a number with metadata of the other numeric type gives wrong results. With `n` 2 and 3 as integers and 3.0 and 2.5 as floats: `n >= 2.25` finds 2.5 and 3.0 but not 3, `n >= 2.0` leaves out 2 and 3, and `n == 3.0` finds only 3.0, as a float in a filter does not match integer metadata; `n == 3` finds only 3, and `n >= 3` finds 2.5 too. `n >= 2` finds all four | Chroma Cloud, with this client and with the Python client 1.5.9; on 1.5.9 the same filters are right, except `n >= 2.25`, which also finds 2 (KD-6); the other versions not verified yet | writes `2.0` as a float and `2` as an integer, so each number keeps its type |
| KD-55 | A `$contains` filter on the documents longer than 130 characters gets `422` "Quota exceeded: 'Length of where document value' exceeded quota limit for action 'Get': current usage of 131 exceeds limit of 130", while the Quotas & Limits page of Chroma Cloud gives 256 for the size of a full-text or regex search. 130 characters find the document | Chroma Cloud, a tenant with the default quotas, with this client and with the Python client 1.5.9; 1.5.9 takes 131 and 149 characters; the other versions not verified yet | reports the error of the server |

The query of KD-2 and KD-3 returned 3 records in every run on Chroma 0.5.20, 1.0.0 and 1.5.9, 10 runs each on a server just started.

## Records lost after an update of their embeddings

Chroma 1.0.21 to 1.5.9, installed on your own servers, may lose a record from the vector index after an `UpdateAsync` or an `UpsertAsync` with embeddings of records that exist, also with the embeddings they had. A query no longer finds it, not even with its own embedding, while `GetAsync` and `CountAsync` do. The record lost is often not one of those written. Chroma applies the records of a write to the vector index in parallel, and the update of a record in place can leave another one without links. Chroma Cloud has not shown it, and Chroma 0.x has not the defect. It is KD-49 in [docs/COMPATIBILITY.md](#known-defects-of-the-servers).

### On the server: `RAYON_NUM_THREADS=1`

With the environment variable `RAYON_NUM_THREADS=1` on the server, the parallel work of Chroma runs on one thread, and no record was lost in our measurements, whatever the client does. It costs time on upserts of records that exist:

```bash
docker run -e RAYON_NUM_THREADS=1 -p 8000:8000 chromadb/chroma:1.5.9
```

Measured on Chroma 1.5.9, in a `cosine` collection, two containers started the same way, with and without the variable:

| Case | Without | With `RAYON_NUM_THREADS=1` |
|---|---|---|
| 120 records, 20 written again with new vectors | 23 runs of 200 miss a record | 0 of 200, and 0 of 600 more |
| 60 records, 20 written again with the same vectors and new metadata | 27 runs of 300 | 0 of 300 |
| add of 5,000 records of 384 dimensions | 0.70 to 1.35 s | 1.29 to 1.69 s |
| upsert of the same 5,000 records with new vectors | 1.34 to 2.06 s | 7.32 to 7.79 s |
| 100 queries of 10 results | 0.23 to 0.35 s | 0.23 to 0.35 s |

The times are of 6 runs each. One thread makes the defect much rarer, but it may not remove it all: with the code of Chroma changed to apply the records in sequence, another measurement lost a record in 1 run of 600, with 60 records. Not measured yet: a long run of many updates, and the versions from 1.0.21 to 1.5.0. Writing the records one per request from the client does not help: the runs that miss a record stay the same.

The strategies of the client: [upsert-strategies.md](upsert-strategies.md).

## Other behaviors of the servers

**Very long filters.** A single Chroma server turns a list of filters into an SQLite expression as deep as the list, and SQLite stops at 1000. Chroma 1.5.9 takes 987 to 994 filters in one list, depending on the operator. Split this way, Chroma 1.5.9 takes up to 8,167 filters, and Chroma 1.0.0 about 4,090. Beyond that, SQLite answers "too many SQL variables", the client throws a `ChromaException`, and the server stays up. That limit counts the values of the query, not the filters: on Chroma 1.5.9 an `In` or a `NotIn` takes about 16,000 values.

A single Chroma server from 1.0.17 accepts `UpdateTenantAsync` but does not keep the name.

## Chroma Cloud

Checked on 4 October 2026 against `api.trychroma.com`: the key goes in `X-Chroma-Token`, the default of `WithChromaToken`, since `Authorization: Bearer` gets `401`. `pre-flight-checks` declares a `max_batch_size` of 1000 and `supports_base64_encoding`, but a write of more than 300 records gets `422` with `Quota exceeded`, the default quota; `WithBatchSplitting(maxBatchSize: 300)` writes and deletes 301 records in two batches. Embeddings sent in base64 read back identical. A missing collection gets `404` with `NotFoundError`.

A metadata value has at most 8,182 bytes, and a document 16,384: a value of 8,183 bytes gets `422` with `Quota exceeded`, and so does every write of a metadata key beyond 36 bytes, also when a collection whose schema names that key was created without an error (checked on 6 October 2026).

A filter has at most 8 predicates, the default quota of a tenant: a `where` with 9, nested ones included, gets `422` with `Quota exceeded: 'Number of where clause predicates'`, and a link to ask for more; the values of an `In` do not count (checked on 5 October 2026).

| Chroma server | API | Tested |
|---|---|---|
| Chroma Cloud | v2 | the tests pass, except the operations an API key cannot run, like `CreateTenantAsync` and `ResetAsync`; see [Chroma Cloud](chroma-cloud.md) |

Chroma Cloud declares 1000, but takes 300 records per write and answers at most 300 records per read, without an error, unless the quota is raised.

Chroma Cloud keeps the other SPANN settings fixed: the RNG factors at 1, `initial_lambda` at 100, and the quantization, which users cannot set.

`QueryAsync` with more than 300 results, `Offset` included, gets the quota error of Chroma Cloud, "'Number of results' exceeded quota limit", unless the quota is raised. `GetAsync` reads in pages, and `SearchAsync` returns more than 300 results.

`GetCollectionByCrnAsync` is there as in the JavaScript client of Chroma, but the operation is hidden in the OpenAPI description of Chroma and missing from its documentation.

Only Chroma Cloud serves the Search API of Chroma; a single Chroma server answers `501`.

## Versions tested in the CI

Every change runs the whole suite against:

- v2 API: 0.5.16, 0.5.20, 0.6.3, 1.0.0, 1.5.9, and `latest` without blocking;
- v1 API: 0.4.10, 0.4.15, 0.4.23, 0.5.15.

On the older servers the tests of the features they miss are skipped, with the reason.
