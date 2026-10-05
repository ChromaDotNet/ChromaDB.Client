# Migration Guide: 1.x to 2.x

## Overview

ChromaDotNet.Client 2.x uses the v2 API of Chroma, and from 2.8.0 its API follows the .NET conventions. Both are **breaking changes**: the URI, the package id and the method names change.

This fork publishes the package as `ChromaDotNet.Client`; the namespaces stay `ChromaDB.Client`.

## What Changed?

ChromaDB has migrated from API v1 to v2, with the primary change being the URL structure:

**v1 API:**
```
http://localhost:8000/api/v1/
```

**v2 API:**
```
http://localhost:8000/api/v2/
```

The v2 API uses a hierarchical URL structure where tenant and database are part of the path instead of query parameters:

- **v1**: `/api/v1/collections?tenant={t}&database={d}`
- **v2**: `/api/v2/tenants/{t}/databases/{d}/collections`

## Migration Steps

### Step 1: Update Your ChromaDB Server

Ensure your ChromaDB server supports the v2 API. The v2 API is available from Chroma 0.5.16: 0.5.16 to 0.6.x serve both v1 and v2, 1.0 and later only v2.

### Step 2: Update Your Configuration

Change your `ChromaConfigurationOptions` URI from `/api/v1/` to `/api/v2/`:

**Before (v1.x):**
```csharp
var configOptions = new ChromaConfigurationOptions(uri: "http://localhost:8000/api/v1/");
```

**After (2.x):**
```csharp
var configOptions = new ChromaConfigurationOptions(uri: "http://localhost:8000/api/v2/");
```

### Step 3: Update Package Reference

Reference `ChromaDotNet.Client`, the latest version:

```bash
dotnet add package ChromaDotNet.Client
```

## What Else Changes?

✅ **Namespaces** - The code keeps `using ChromaDB.Client;`, and `using ChromaDB.Client.Models;` for the models  
✅ **Functionality** - All the features of 1.x work, with many more: see the README  
🔁 **API methods** - From 2.8.0 they follow the .NET conventions: the names end in `Async` (`GetOrCreateCollectionAsync`, `AddAsync`, `QueryAsync`...), they take an optional `CancellationToken`, and they take and return read-only lists and dictionaries: see the [Breaking Changes Summary](#breaking-changes-summary)  
🔁 **Request/response models** - The same data, with `ChromaCollectionQueryEntry.Distance` as `float?`, and `Embedding` and `Uri` for the embedding and the URI of a record  

## Example: Complete Migration

**Before (v1.x):**
```csharp
using ChromaDB.Client;

var configOptions = new ChromaConfigurationOptions(uri: "http://localhost:8000/api/v1/");
using var httpClient = new HttpClient();
var client = new ChromaClient(configOptions, httpClient);

var collection = await client.GetOrCreateCollection("my_collection");
var collectionClient = new ChromaCollectionClient(collection, configOptions, httpClient);

await collectionClient.Add(
    ["doc1"], 
    embeddings: [new([1f, 0.5f, 0f, -0.5f, -1f])]
);
```

**After (2.8.0 and later):**
```csharp
using ChromaDB.Client;

// /api/v1/ → /api/v2/
var configOptions = new ChromaConfigurationOptions(uri: "http://localhost:8000/api/v2/");
using var httpClient = new HttpClient();
var client = new ChromaClient(configOptions, httpClient);

// The methods end in Async
var collection = await client.GetOrCreateCollectionAsync("my_collection");
var collectionClient = new ChromaCollectionClient(collection, configOptions, httpClient);

await collectionClient.AddAsync(
    ["doc1"], 
    embeddings: [new([1f, 0.5f, 0f, -0.5f, -1f])]
);
```

## Troubleshooting

### "404 Not Found" Errors

If you get 404 errors after upgrading, you're likely still pointing to the v1 API endpoint:
- ✅ Check your URI contains `/api/v2/` (not `/api/v1/`)
- ✅ Verify your ChromaDB server supports v2 API

Chroma 1.0 and later answer the v1 routes with `410 Gone` and the message "The v1 API is deprecated. Please use /v2 apis".

### Server Version Compatibility

The v2 API is supported in ChromaDB server versions 0.5.16 and later. Check your server version:

```csharp
var version = await client.GetVersionAsync();
Console.WriteLine($"ChromaDB Server Version: {version}");
```

## Breaking Changes Summary

| Change | Impact | Action Required |
|--------|--------|-----------------|
| API endpoint | High | Update URI from `/api/v1/` to `/api/v2/` |
| Package id | High | Reference `ChromaDotNet.Client` instead of `ChromaDB.Client` |
| URL structure | None | Handled internally by the client |
| Request/response | Low | `ChromaCollectionQueryEntry.Distance` is `float?`, `null` when the query does not include `ChromaQueryInclude.Distances` |
| Method names | Medium | Add `Async` to the name of each asynchronous method: `GetOrCreateCollection` → `GetOrCreateCollectionAsync`, `Add` → `AddAsync`, `Query` → `QueryAsync` |
| Cancellation | Low | Every async method takes an optional `CancellationToken`: rebuild, and turn a method group like `client.Heartbeat` passed as a `Func<Task>` into `() => client.HeartbeatAsync()` |
| Lists and dictionaries | Low | Parameters and results are `IReadOnlyList<T>` and `IReadOnlyDictionary<string, object>`: a `List<T>` or a collection expression still goes in, and metadata are written `new Dictionary<string, object> { ["key"] = value }` |
| Records | Low | The embedding of a record is `Embedding`, its URI `Uri`; `Data` is gone, as no Chroma server fills it |
| Metadata values | Low | Strings come back as strings, lists as `List<object>`: `WithMetadataValues(ChromaMetadataValues.Inferred)` reads them as 1.x, dates as `DateTime` |
| Filters | Low | `ChromaWhere` and `ChromaWhereDocument` are gone: use `ChromaWhereOperator` and `ChromaWhereDocumentOperator`, the types the methods take |
| Options constructor | Low | `defaultTenant:` and `defaultDatabase:` are now `tenant:` and `database:`: `new ChromaConfigurationOptions(uri, tenant: ..., database: ...)` |
| Batches | Low | Writes go in batches of the `max_batch_size` of the server and `GetAsync` reads in pages; `WithBatchSplitting(false)` sends each of them in one request |
| Errors | Low | Network errors, answers that are not the expected JSON and timeouts throw a `ChromaException`, with the original exception as `InnerException` |

## Need Help?

If you encounter issues during migration:

1. Verify your ChromaDB server version supports v2 API
2. Double-check your configuration URI uses `/api/v2/`
3. Review the error messages - they will indicate if there's a server compatibility issue
4. Open an issue on GitHub if you need assistance

## Rollback

If you need to rollback to v1 API:

1. Go back to the original package, ChromaDB.Client 1.x: remove `ChromaDotNet.Client` and run `dotnet add package ChromaDB.Client --version 1.0.0`
2. Revert your configuration URI to `/api/v1/`
