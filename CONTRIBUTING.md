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
- `CHROMA_TEST_DISTRIBUTED=true` says that the server is distributed Chroma, the one Chroma Cloud runs on, installed elsewhere: the tests use `WithDistributedChroma()` and expect what Chroma Cloud does, without its quotas.
- `CHROMA_TEST_OTHER_TENANTS=true` says that the server lets the tests create and look up other tenants and databases, like a server started for the tests.

Each fixture deletes the collections and the databases its requests created, and nothing else. The tests that reset the server, create or look up other tenants and databases, or query an id that does not exist are skipped; with `CHROMA_TEST_OTHER_TENANTS=true` the ones of other tenants and databases run.

The property tests, in the classes whose names end in `PropertyTests`, check random cases with [FsCheck](https://github.com/fscheck/FsCheck) and need no Chroma. Each run takes new cases. A failure prints the case and its seed, like `(5123468595321266860,8907826574119387873)`: `FSCHECK_REPLAY` with that seed runs the same cases again. The CI sets one, so that its runs check the same cases.

## Releases

The CI of a version tag, `vX.Y.Z`, publishes the packages to NuGet. The tests of a release run before its tag, on packages built from the commit that becomes the tag, as the CI builds them. Built that way, they have the same files as the published packages:

1. On Linux, with the .NET SDK the CI uses: the newest SDK of the runner image, as the repository has no `global.json`. The log of the CI run on `main` shows it. On Windows the files of the package get other line endings.
2. From a git checkout of the commit in `/home/runner/work/ChromaDB.Client/ChromaDB.Client`, with `origin` at `https://github.com/ChromaDotNet/ChromaDB.Client`. Each DLL holds the path of its PDB, and the package and the version of the DLLs hold the commit.
3. With `PackageIdPrefix=ChromaDotNet` and `GITHUB_REPOSITORY=ChromaDotNet/ChromaDB.Client`, and the two commands of the CI at the root:

```
dotnet build -c Release --version-suffix ''
dotnet pack -c Release --no-build --version-suffix ''
```

These packages have the version of the release. They stay in a folder of their own and are never pushed. The tests restore them with a NuGet cache of their own, set with `NUGET_PACKAGES`, never the global one.

After the release, the files of each package on nuget.org are compared with those of the tested package. They are the same, apart from `.signature.p7s`, the signature nuget.org adds; the times of the files in the archive differ. When a file differs, the tests of the release run again on the packages from nuget.org, with that cache emptied first.
