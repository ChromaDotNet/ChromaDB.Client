# Contributing

Issues and pull requests are welcome.

## Build and test

```
dotnet build
dotnet test
```

`dotnet test` starts a Chroma container for each test fixture with Testcontainers, so Docker must be running. The variables that choose the Chroma version, the v1 API, a tenant and a database, or a server already running like Chroma Cloud are described in [Tests](README.md#tests). `-p:ChromaClientTargetFramework=netstandard2.0` runs the tests against the .NET Standard 2.0 builds of the packages.

## Pull requests

- A fix comes with a test that fails without it.
- A behavior that differs between Chroma versions is tested on the versions where it changes, and the README names those versions.
- The public API is listed in `PublicAPI.Shipped.txt`, the API marked as shipped, and `PublicAPI.Unshipped.txt`, what was added or removed since; the version bump after a release moves it to `PublicAPI.Shipped.txt`. A new public member goes in `PublicAPI.Unshipped.txt`, otherwise the build warns with RS0016; the code fix of the analyzer adds it.
- Public types and members have XML documentation, which goes in the package.
- Versions follow semantic versioning: removing or changing public API needs a major version.

The CI runs the tests against several Chroma versions, and publishes the app in `Samples/ChromaDB.Client.TrimmingTest` with trimming and with NativeAOT. Only the version tags are published to NuGet; every change merged into `main` builds the packages, which its CI run keeps as artifacts.
