# Upsert strategies

[Records lost after an update of their embeddings](COMPATIBILITY.md#records-lost-after-an-update-of-their-embeddings), in COMPATIBILITY.md, describes the defect of the servers.

## In the client: upsert strategies

When the server cannot be changed, `WithUpsertStrategy` on the options says how `UpdateAsync` and `UpsertAsync` write the embeddings of records that exist:

```csharp
var options = new ChromaConfigurationOptions("http://localhost:8000").WithUpsertStrategy(ChromaUpsertStrategy.SkipUnchangedEmbeddings);
```

| Strategy | What it protects | What it costs |
|---|---|---|
| `Server`, the default | nothing: the update and the upsert of the server | nothing more |
| `SkipUnchangedEmbeddings` | the records written again with the same embedding, as with new metadata or a new document: they are updated without it, which does not touch the vector index | a get of the records first |

With `SkipUnchangedEmbeddings` no record is deleted, so there is no risk of data loss. A write with both unchanged and changed embeddings goes in two requests, the update of the unchanged records first. If it fails, the second request does not go, so the write can stay applied in part. The exception says how many records went in the requests that answered before the error. What the failed request did is not known: it can still be applied, as on Chroma 1.0.0, where an update answers `500` but goes through (KD-31). A record whose embedding changes still goes by the upsert of the server, and then the vector index can still lose a record, often another one. So does an id given more than once in the same write, in the order given, as Chroma applies the writes in order, so it stays exposed too. In a `cosine` collection, where Chroma 1.x gives an embedding back 1 or 2 ulp off (KD-8), values within 4 ulp count as the same; an embedding computed again by a model can differ by more, and then it goes as changed. On Chroma 0.x, which has not KD-49, the writes go as they are. Chroma 0.5.15, 0.5.16, 0.5.20 and 0.6.3, the versions tested, have another defect, KD-51, which the strategies do not change: after some upserts a query of all the records can miss one, until the next write. A `Microsoft.Extensions.VectorData` store built on a `ChromaClient` with these options, which writes by upserts with the embeddings, gets it too.

Turn it on for Chroma 1.0.21 to 1.5.9 installed on your own servers without `RAYON_NUM_THREADS=1`, where records are written again with the embeddings they had. Measured on Chroma 1.5.9, in a series of runs separate from the one of [Records lost after an update of their embeddings](COMPATIBILITY.md#records-lost-after-an-update-of-their-embeddings), so the numbers of `Server` differ a little from those of its table: in a `cosine` collection of 60 records with random vectors, of which 20 are written again with the same vectors and new metadata, 300 runs: a query of all the records missed one in 28 runs with `Server`, and in none with `SkipUnchangedEmbeddings`. With 120 records of which 20 get new vectors, 200 runs, they missed one in 22 runs with `Server` and in 25 with `SkipUnchangedEmbeddings`, which cannot help there.
