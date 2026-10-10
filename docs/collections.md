# Collections

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

`ModifyAsync` changes the name or the metadata of a collection. `PeekAsync` returns its first records. `DeleteCollectionIfExistsAsync` takes a missing collection as deleted already, which the client recognizes as `CollectionExistsAsync` does (KD-43 in [docs/COMPATIBILITY.md](COMPATIBILITY.md)).

`DeleteCollectionAsync`, `DeleteCollectionIfExistsAsync` and `DeleteDatabaseAsync` send one request, as Chroma does. If your records have lists in their metadata, pass `deleteRecordsFirst: true`: on Chroma 1.x, except Chroma Cloud, the client then deletes the records first, in batches. That takes about two requests for every 5,461 records, about 370 for a million. If it stops halfway, the collection keeps part of its records or none of them: delete it again. If the server returns records after their delete, the client throws a `ChromaException` and keeps the collection, instead of deleting it with them.

The lists of the records of a deleted collection: KD-12 in [COMPATIBILITY.md](COMPATIBILITY.md#known-defects-of-the-servers).

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

[docs/COMPATIBILITY.md](COMPATIBILITY.md) lists the servers that have `ListDatabasesAsync` and `DeleteDatabaseAsync`.

## Collections by id

```csharp
var collection = await client.GetCollectionByIdAsync(id);
```

`GetCollectionByIdAsync` looks for the id in the tenant and database of the options, or in the ones you pass. [docs/COMPATIBILITY.md](COMPATIBILITY.md) lists the servers that have it.

A `ChromaClient` hands out the clients for the records of its collections, with its options and `HttpClient`, without sending a request. The id and the name are enough:

```csharp
var collectionClient = client.GetCollectionClient(collection);
var sameCollection = client.GetCollectionClient(collectionId, "my_collection");
var byName = client.GetCollectionClient("my_collection");   // whichever collection has the name

// or without a ChromaClient; the tenant and database come from the options
var standalone = new ChromaCollectionClient(collectionId, "my_collection", options, httpClient);
```

A collection client made by name reads the collection before its first request, which `GetCollectionAsync` returns. When a request fails on that id, it reads the collection again: if the name has another id, as when the collection was deleted and created again elsewhere, the operation runs again, once, on that collection, and otherwise the failure stands. The servers tell a collection gone in their own ways ([docs/COMPATIBILITY.md](COMPATIBILITY.md)), so the client compares the ids.
