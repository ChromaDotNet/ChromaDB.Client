# Platforms

## Builds

The package has three builds: .NET 10, .NET Framework 4.6.2 and .NET Standard 2.0. Applications on .NET 10 and later take the .NET 10 build, and applications on .NET Core 3.1 to .NET 9 the .NET Standard 2.0 one. The three builds have the same public API, the embedding generators included, and all advise the buckets of the histogram.

The tests run the .NET 10 build on every tested Chroma version. On Chroma 1.5.9 they also run built for .NET 11, and against the .NET Standard 2.0 build. The .NET Framework 4.6.2 build has the code of the .NET Standard 2.0 one. It is built against the assemblies that .NET Framework applications ship, so it runs next to OpenTelemetry without binding redirects.

On .NET Core, the .NET Standard 2.0 build uses two types of the runtime that .NET Standard 2.0 does not have, found by their names:

- `SocketsHttpHandler`, for the `HttpClient` of a client created without one: its connections are renewed every two minutes, as with the .NET 10 build;
- `DateOnly`, which `ChromaMetadataConvert` writes as `yyyy-MM-dd` and reads back, also in arrays and lists.

The .NET Standard 2.0 build takes System.Diagnostics.DiagnosticSource 9, for the buckets of the histogram. On .NET Core 3.1 to 7 that package warns that it does not support them; `SuppressTfmSupportBuildWarnings` turns the warning off.

In our tests on 12 November 2026, the suite passed against the .NET Standard 2.0 build on the .NET 8.0.34 runtime, against Chroma 1.5.9.

## Trimming and NativeAOT

The client serializes with metadata generated at build time, so it works in applications published with trimming or NativeAOT, where serialization by reflection is off. The .NET 10 builds of the packages are marked `IsAotCompatible`. The .NET Standard 2.0 builds cannot be marked. In our tests on 12 November 2026, `Samples/ChromaDB.Client.TrimmingTest`, published for .NET 8 with trimming and with NativeAOT, every warning an error, ran against Chroma 1.5.9 on the .NET Standard 2.0 build.

The values of metadata and filters can be `string`, the numeric types, `bool`, `DateTime`, `DateTimeOffset`, `Guid`, `JsonElement`, arrays and lists of strings, numbers and booleans, `List<object>` and `object[]`. Other types work only where reflection is on, as without trimming.

The CI publishes `Samples/ChromaDB.Client.TrimmingTest` with trimming and with NativeAOT, on .NET 10 and on .NET 11, with every warning as an error, and runs it against Chroma 1.5.9. With `CHROMA_HOST`, `CHROMA_API_KEY`, `CHROMA_TENANT` and `CHROMA_DATABASE`, it runs against Chroma Cloud.
