# ChromaDB.Client

_ChromaDB.Client_ is a .NET SDK that offers a seamless connection to the Chroma database. It allows creating and managing collections, performing CRUD operations, and executing nearest neighbor search and filtering.

> This is the community-maintained continuation of [ssone95/ChromaDB.Client](https://github.com/ssone95/ChromaDB.Client), kept up to date with current Chroma versions. It is an independent project and is not affiliated with or endorsed by Chroma. See [About this fork](#about-this-fork).

## Compatibility

| Chroma server | Server API | Status | Tested in CI |
|---|---|---|---|
| 1.x | v2 | supported | 1.5.9, latest |
| 0.5.16 to 0.6.x | v1 and v2 | supported, through v2 | 0.5.20, 0.6.3 |
| 0.5.15 and earlier | v1 only | not supported yet | - |

Since Chroma 1.0.16 the server requires embeddings in `Add` and `Upsert`: the client does not compute them, so pass them explicitly.

The package targets .NET 8 and .NET Standard 2.0; the tests run against both builds.

## Installation

```
dotnet add package ChromaDotNet.Client
```

`ChromaDotNet.Client.DependencyInjection` adds the registration for `Microsoft.Extensions.DependencyInjection`. Every change merged into `main` is also published as a preview version, installed with `--prerelease`.

## Example

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

## Migrating from ChromaDB.Client 1.x

- Use the `/api/v2/` URI. Chroma 1.x answers the v1 routes with `410 Gone`.
- Namespaces do not change: the code keeps `using ChromaDB.Client;`.
- Replace the `ChromaDB.Client` package reference with `ChromaDotNet.Client`.
- `ChromaCollectionQueryEntry.Distance` is a `float?`: it is `null` when the query does not include `ChromaQueryInclude.Distances`.
- Every async method takes an optional `CancellationToken` as its last parameter. Code compiled against 1.x has to be rebuilt, and a method group like `client.Heartbeat` passed as a `Func<Task>` becomes `() => client.Heartbeat()`.

The [migration guide](https://github.com/ChromaDotNet/ChromaDB.Client/blob/main/v2-migration/MIGRATION_GUIDE_V2.md) has the details.

## Status

[![NuGet](https://img.shields.io/nuget/v/ChromaDotNet.Client)](https://www.nuget.org/packages/ChromaDotNet.Client/)
[![CI](https://img.shields.io/github/actions/workflow/status/ChromaDotNet/ChromaDB.Client/ci.yml?branch=main)](https://github.com/ChromaDotNet/ChromaDB.Client/actions/workflows/ci.yml)
[![License](https://img.shields.io/github/license/ChromaDotNet/ChromaDB.Client)](LICENSE)

## About this fork

The original project was created by [ssone95](https://github.com/ssone95) and largely written by [cincuranet](https://github.com/cincuranet). Its last change dates from February 2025, and it still targets the Chroma v1 API, which Chroma 1.x no longer serves.

This fork:

- integrates the pending upstream pull requests [80](https://github.com/ssone95/ChromaDB.Client/pull/80), by [inlineHamed](https://github.com/inlineHamed), and [82](https://github.com/ssone95/ChromaDB.Client/pull/82), by [richlander](https://github.com/richlander), which migrate the client to the v2 API;
- tests the client against several Chroma versions and against both target frameworks;
- works through the issues reported upstream, one pull request each.

The original commits of the upstream pull requests, which were squash-merged, are kept in the [`upstream-history`](https://github.com/ChromaDotNet/ChromaDB.Client/tree/upstream-history) branch.

The project is released under the MIT License, keeping the original copyright notice.
