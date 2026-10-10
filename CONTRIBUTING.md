# Contributing

Issues and pull requests are welcome.

## Build and test

```
dotnet build
dotnet test
```

`dotnet test` starts a Chroma container for each test fixture with Testcontainers, so Docker must be running. The variables that choose the Chroma version, the v1 API, a tenant and a database, or a server already running like Chroma Cloud are described in [Tests](#tests). `-p:ChromaClientTargetFramework=netstandard2.0` runs the tests against the .NET Standard 2.0 builds of the packages.

## Pull requests

- A fix comes with a test that fails without it.
- New functionality comes with tests in the automated test suite, which the CI runs on every pull request.
- A behavior that differs between Chroma versions is tested on the versions where it changes, and [docs/COMPATIBILITY.md](docs/COMPATIBILITY.md) names those versions.
- The public API is listed in `PublicAPI.Shipped.txt`, the API marked as shipped, and `PublicAPI.Unshipped.txt`, what was added or removed since; the version bump after a release moves it to `PublicAPI.Shipped.txt`. A new public member goes in `PublicAPI.Unshipped.txt`, otherwise the build warns with RS0016; the code fix of the analyzer adds it.
- Public types and members have XML documentation, which goes in the package.
- Versions follow semantic versioning: removing or changing public API needs a major version.

The CI runs the tests against several Chroma versions, and publishes the app in `Samples/ChromaDB.Client.TrimmingTest` with trimming and with NativeAOT. Only the version tags are published to NuGet; every change merged into `main` builds the packages, which its CI run keeps as artifacts.

## Tests

`dotnet test` starts a Chroma container for each test fixture: `chromadb/chroma:0.6.3`, unless `CHROMA_IMAGE` names another image. `CHROMA_TEST_API_VERSION=v1` runs the tests with the v1 API.

With `CHROMA_TEST_URI`, the tests run against a server that is already running, like Chroma Cloud, and take it for the latest Chroma unless `CHROMA_IMAGE` says otherwise:

- `CHROMA_TEST_TOKEN` goes in `X-Chroma-Token`;
- `CHROMA_TEST_TENANT` and `CHROMA_TEST_DATABASE` are used as they are, not created;
- `CHROMA_TEST_MAX_BATCH_SIZE` is a batch limit lower than the one the server declares, like 300 on Chroma Cloud.

Each fixture deletes the collections and the databases its requests created, and nothing else. The tests that reset the server, create or look up other tenants and databases, or query an id that does not exist are skipped.

The property tests, in the classes whose names end in `PropertyTests`, check random cases with [FsCheck](https://github.com/fscheck/FsCheck) and need no Chroma. Each run takes new cases. A failure prints the case and its seed, like `(5123468595321266860,8907826574119387873)`: `FSCHECK_REPLAY` with that seed runs the same cases again. The CI sets one, so that its runs check the same cases.
