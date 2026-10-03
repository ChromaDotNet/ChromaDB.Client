# ChromaDB.Client

_ChromaDB.Client_ is a .NET SDK that offers a seamless connection to the Chroma database. It allows creating and managing collections, performing CRUD operations, and executing nearest neighbor search and filtering.

> This is the community-maintained continuation of [ssone95/ChromaDB.Client](https://github.com/ssone95/ChromaDB.Client), kept up to date with current Chroma versions. It is an independent project and is not affiliated with or endorsed by Chroma. See [About this fork](#about-this-fork).

## Compatibility

| Chroma server | API | Tested |
|---|---|---|
| 0.5.16 – 1.5.9 | v2, the default | all the tests pass on each release tested |
| 0.5.1 – 0.5.15 | v1, with `ChromaApiVersion.V1` | all the tests pass on each release tested |
| 0.4.10 – 0.5.0 | v1, with `ChromaApiVersion.V1` | collections and records work on each release tested; some of these servers miss tenants, `CountCollections` or the `$not_contains` filter |

Each release tested, the differences between them and the versions in the CI are listed in [docs/COMPATIBILITY.md](https://github.com/ChromaDotNet/ChromaDB.Client/blob/main/docs/COMPATIBILITY.md).

Since Chroma 1.0.16 the server requires embeddings in `Add` and `Upsert`: the client does not compute them, so pass them explicitly.

The package targets .NET 8 and .NET Standard 2.0; the tests run against both builds.

## Installation

```
dotnet add package ChromaDotNet.Client
```

`ChromaDotNet.Client.DependencyInjection` adds the registration for `Microsoft.Extensions.DependencyInjection`. Every change merged into `main` is also published as a preview version, installed with `--prerelease`.

## Example

The URI can be just the address of the server, like `http://localhost:8000`: the client adds `/api/v2/`. A URI with a path, like `http://localhost:8000/api/v2`, is used as it is, with or without the trailing slash.

```csharp
using ChromaDB.Client;

var configOptions = new ChromaConfigurationOptions(uri: "http://localhost:8000/api/v2/");
using var httpClient = new HttpClient();
var client = new ChromaClient(configOptions, httpClient);

Console.WriteLine(await client.GetVersion());

var string5Collection = await client.GetOrCreateCollection("string5");
var string5Client = new ChromaCollectionClient(string5Collection, configOptions, httpClient);

await string5Client.Add(["340a36ad-c38a-406c-be38-250174aee5a4"], embeddings: [new([1f, 0.5f, 0f, -0.5f, -1f])]);

var getResult = await string5Client.Get("340a36ad-c38a-406c-be38-250174aee5a4", include: ChromaGetInclude.Metadatas | ChromaGetInclude.Documents | ChromaGetInclude.Embeddings);
Console.WriteLine($"ID: {getResult!.Id}");

var queryData = await string5Client.Query([new([1f, 0.5f, 0f, -0.5f, -1f]), new([1.5f, 0f, 2f, -1f, -1.5f])], include: ChromaQueryInclude.Metadatas | ChromaQueryInclude.Distances);
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
await collectionClient.Add(new ChromaRecords(["a"]) { Embeddings = [new([1f, 0.5f, 0f])], Uris = ["s3://bucket/a.png"] });

var entries = await collectionClient.Get(["a"], include: ChromaGetInclude.Uris);
Console.WriteLine(entries[0].Uri);
```

`ChromaRecords` holds the ids, embeddings, metadatas, documents and URIs of the records for `Add`, `Update` and `Upsert`.

## Tenants and databases

```csharp
await client.CreateTenant("my_tenant");
await client.CreateDatabase("my_database", tenant: "my_tenant");

var options = new ChromaConfigurationOptions(uri: "http://localhost:8000", defaultTenant: "my_tenant", defaultDatabase: "my_database");
```

The collections created with these options belong to that tenant and database.

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

## Migrating from ChromaDB.Client 1.x

- Use the `/api/v2/` URI, or just the address of the server. Chroma 1.x answers the v1 routes with `410 Gone`.
- Namespaces do not change: the code keeps `using ChromaDB.Client;`.
- Replace the `ChromaDB.Client` package reference with `ChromaDotNet.Client`.
- `ChromaCollectionQueryEntry.Distance` is a `float?`: it is `null` when the query does not include `ChromaQueryInclude.Distances`.
- Every async method takes an optional `CancellationToken` as its last parameter. Code compiled against 1.x has to be rebuilt, and a method group like `client.Heartbeat` passed as a `Func<Task>` becomes `() => client.Heartbeat()`.

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
