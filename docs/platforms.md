# Platforms

## Builds

The package has three builds: .NET 8, .NET Framework 4.6.2 and .NET Standard 2.0. The tests run the .NET 8 build on every tested Chroma version, and the .NET Standard 2.0 build on Chroma 1.5.9. The .NET Framework 4.6.2 build has the code of the .NET Standard 2.0 one, and also advises the buckets of the histogram, as the .NET 8 build does. It is built against the assemblies that .NET Framework applications ship, so it runs next to OpenTelemetry without binding redirects.

## Trimming and NativeAOT

The client serializes with metadata generated at build time, so it works in applications published with trimming or NativeAOT, where serialization by reflection is off. The packages are marked `IsAotCompatible`.

The values of metadata and filters can be `string`, the numeric types, `bool`, `DateTime`, `DateTimeOffset`, `Guid`, `JsonElement`, arrays and lists of strings, numbers and booleans, `List<object>` and `object[]`. Other types work only where reflection is on, as without trimming.

The CI publishes `Samples/ChromaDB.Client.TrimmingTest` with trimming and with NativeAOT, with every warning as an error, and runs it against Chroma 1.5.9. With `CHROMA_HOST`, `CHROMA_API_KEY`, `CHROMA_TENANT` and `CHROMA_DATABASE`, it runs against Chroma Cloud.
