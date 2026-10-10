# Index settings

## Distance of a collection

```csharp
var collection = await client.CreateCollectionAsync(new ChromaCollectionDefinition("my_collection")
{
	Configuration = new() { Space = ChromaSpace.Cosine },
});
Console.WriteLine(collection.Space);
```

`ChromaSpace` is `L2` (Chroma's default), `Cosine` or `InnerProduct`. The client sends it as the `hnsw:space` metadata (KD-44 in [docs/COMPATIBILITY.md](COMPATIBILITY.md)). `GetOrCreateCollectionAsync` takes a `ChromaCollectionDefinition` too. A collection that exists keeps its space, so `GetOrCreateCollectionAsync` throws a `ChromaException` when it has another space than the definition asks for; [docs/COMPATIBILITY.md](COMPATIBILITY.md) says how on the 0.x servers.

`ChromaCollection.Space` reads the space back from that metadata, or from the configuration that Chroma 1.0.6 and later and Chroma Cloud send. On Chroma 0.x, which keeps the space only in that metadata, a collection without it uses `l2`, the default of Chroma, and `Space` is `L2`: the client asks the version of the server for that. It is null for a collection created without the `hnsw:space` metadata on Chroma 1.0.0 to 1.0.5, which report `l2` also for the ones that use another space (KD-50 in [docs/COMPATIBILITY.md](COMPATIBILITY.md)): there the space checks let it pass, so use Chroma 1.0.6 or later. `ChromaCollection.ConfigurationJson` holds the configuration as the server sends it.

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

- **`Hnsw`**, the index of a single Chroma server: `EfConstruction`, `EfSearch`, `MaxNeighbors`, `ResizeFactor`, `SyncThreshold`, `BatchSize` and `NumThreads`. They go as `hnsw:` metadata, like the space, which every tested Chroma applies.
  - `MaxNeighbors` must be at least 2, here, in the `hnsw:M` metadata and in `ModifyConfigurationAsync`: the client throws an `ArgumentException` for 0 and 1 ([docs/COMPATIBILITY.md](COMPATIBILITY.md)).
  - In the metadata, `hnsw:M`, `hnsw:construction_ef`, `hnsw:search_ef`, `hnsw:num_threads`, `hnsw:batch_size` and `hnsw:sync_threshold` are integers. Chroma rejects `100.0` for them, so the client sends a whole `double`, `float` or `decimal` as an integer and throws an `ArgumentException` for any other number.
- **`Spann`**, the index of Chroma Cloud:
  - `SearchNprobe`, `WriteNprobe`, `EfConstruction`, `EfSearch`, `MaxNeighbors`, `SplitThreshold`, `MergeThreshold` and `ReassignNeighborCount` go in the `configuration` of the request, with the space, because Chroma ignores the `hnsw:space` metadata next to SPANN settings.
  - `SearchRngEpsilon`, `WriteRngEpsilon`, `NreplicaCount`, `NumSamplesKmeans`, `NumCentersToMergeTo` and `CenterDriftThreshold` go in a schema, the only place Chroma takes them.
- **`EmbeddingFunction`** goes in the `configuration` of the request; [docs/COMPATIBILITY.md](COMPATIBILITY.md) lists the versions that take it and report it.
- **What the client checks:**
  - A collection has one vector index. `Hnsw` and `Spann` together throw an `ArgumentException`, as Chroma rejects them.
  - Chroma Cloud ignores `Hnsw`, and a single server ignores `Spann`. Then `CreateCollectionAsync` deletes the collection and throws a `ChromaException`, and `GetOrCreateCollectionAsync` throws and keeps it. Chroma 1.0.0 to 1.0.5 report no configuration, so there the client cannot tell.
  - With `Spann` or `EmbeddingFunction`, the client throws a `ChromaException` before sending the `configuration` to the servers that fail on it or ignore it ([docs/COMPATIBILITY.md](COMPATIBILITY.md)).
- **With a schema**, every setting goes in the schema, on the vector index of `#embedding`, as `create_index(VectorIndexConfig(...))` of the Python client writes them.

`ChromaCollection.Dimension` is the number of dimensions of the embeddings, set by the first write. `Version` and `LogPosition` are the version of the collection and its position in the log, as the server reports them; [docs/COMPATIBILITY.md](COMPATIBILITY.md) lists the versions that send them.

## Settings of the index

```csharp
await collectionClient.ModifyConfigurationAsync(new() { Hnsw = new() { EfSearch = 200 } });
```

`ModifyConfigurationAsync` changes the index settings that Chroma allows after the creation:

- the HNSW settings, like `EfSearch`;
- the `EfSearch` and `SearchNprobe` of the SPANN index of Chroma Cloud;
- the embedding function the collection declares, `EmbeddingFunction`.

The versions that apply them are in [COMPATIBILITY.md](COMPATIBILITY.md).

The client recognizes those versions from the configuration they send with the collection, and throws a `ChromaException` without sending the request.

The settings must be those of the index of the collection: `Hnsw` on a single Chroma server, `Spann` on Chroma Cloud. Chroma Cloud answers `500` to `Hnsw` settings, and a single server accepts `Spann` settings without applying them. So for the settings of the other index, the client throws a `ChromaException` without sending them. On Chroma Cloud:

```csharp
await collectionClient.ModifyConfigurationAsync(new() { Spann = new() { SearchNprobe = 32 } });
```
