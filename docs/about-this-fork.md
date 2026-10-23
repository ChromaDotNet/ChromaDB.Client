# About this fork

The original project was created by [ssone95](https://github.com/ssone95) and largely written by [cincuranet](https://github.com/cincuranet). Its code last changed in February 2025, and it still targets the Chroma v1 API, which Chroma 1.x no longer serves. Its [ChromaDB.Client](https://www.nuget.org/packages/ChromaDB.Client) package is deprecated on NuGet in favor of ChromaDotNet.Client, and since pull request [85](https://github.com/ssone95/ChromaDB.Client/pull/85) its README says that the development continues here.

This fork:

- integrates the pending upstream pull requests [80](https://github.com/ssone95/ChromaDB.Client/pull/80), by [inlineHamed](https://github.com/inlineHamed), and [82](https://github.com/ssone95/ChromaDB.Client/pull/82), by [richlander](https://github.com/richlander), which move the client to the v2 API;
- tests the client against several Chroma versions, with its .NET 8 and .NET Standard 2.0 builds;
- works through the issues reported upstream, one pull request each.

The original commits of the upstream pull requests, which were squash-merged, are kept in the [`upstream-history`](https://github.com/ChromaDotNet/ChromaDB.Client/tree/upstream-history) branch.

The repository started as a GitHub fork of [ssone95/ChromaDB.Client](https://github.com/ssone95/ChromaDB.Client) and left its fork network in October 2026. The history and the credits above are unchanged.
