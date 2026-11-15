# Backup and restore

`ChromaDotNet.Client.Backup` saves collections of a database into a zip file through the API of Chroma, and writes them back into the same server or another one. `ChromaDotNet.Client.Cli` is the same as a command, `chromadotnet`, with `backup`, `restore` and `verify`.

It is tested with Chroma 1.5.9, VecturaDB and Chroma Cloud, which runs on distributed Chroma, also from each one into the others. Distributed Chroma installed by yourself: not tested. With another server the command warns and goes on.

## A server that takes writes during the backup

A backup reads the collections while the server runs. If the server takes writes meanwhile, the file can hold records of different moments: a record added during the backup can be there or not, and one changed can be there as it was before or after. On Chroma 1.5.9 the reads themselves can then mix records of different moments.

A stopped server, or one that takes no writes, gives a consistent copy. The backup counts each collection at the start and at the end: when the counts differ, a get gives an id twice, or the file holds another number of records than the count, the result says `ChangedDuringBackup`, the manifest has `"changed_during_backup": true`, and `chromadotnet backup` warns and exits with 2.

## The command

```
dotnet tool install -g ChromaDotNet.Client.Cli

chromadotnet backup --url http://localhost:8000 --all --out backup.zip
chromadotnet verify --url http://localhost:8000 --in backup.zip
chromadotnet restore --url http://localhost:8001 --in backup.zip
```

- `--collections <name>`, once for each collection, in place of `--all`; in `restore` and `verify`, some of the collections of the file.
- `--token`, or the environment variable `CHROMA_API_KEY`, so that the key stays out of the history of the shell. `--tenant` and `--database`, or `CHROMA_TENANT` and `CHROMA_DATABASE`; on Chroma Cloud without a tenant, the tenant and the database of the API key.
- `--distributed` for distributed Chroma that is not Chroma Cloud.
- `--batch`, the records of one request, 100 by default and at most 300, the most that Chroma Cloud answers; `--concurrent`, the gets of a backup at the same time, 5 by default.

### Without .NET

Each [release](https://github.com/ChromaDotNet/ChromaDB.Client/releases) has the command as a file that runs without .NET, built with NativeAOT, and their SHA-256 in `SHA256SUMS`:

| File | System |
|---|---|
| `chromadotnet-linux-x64`, `chromadotnet-linux-arm64` | Linux with glibc 2.34 or later: Ubuntu 22.04, Debian 12, RHEL 9 and later. Not Ubuntu 20.04, Debian 11 or RHEL 8. |
| `chromadotnet-linux-musl-x64` | Alpine and other Linux with musl, x64 |
| `chromadotnet-osx-arm64`, `chromadotnet-osx-x64` | macOS on Apple silicon and on Intel |
| `chromadotnet-win-x64.exe` | Windows x64 |

```
curl -fsSLO https://github.com/ChromaDotNet/ChromaDB.Client/releases/latest/download/chromadotnet-linux-x64
curl -fsSLO https://github.com/ChromaDotNet/ChromaDB.Client/releases/latest/download/SHA256SUMS
sha256sum --check --ignore-missing SHA256SUMS
chmod +x chromadotnet-linux-x64
./chromadotnet-linux-x64 backup --url http://localhost:8000 --all --out backup.zip
```

Where the files are tested: the CI of every pull request runs the ones for Linux (x64, arm64 and musl) against Chroma 1.5.9, with a backup, a restore and a verification, and the others only with `--version`. The one for Windows x64 was also run by hand against Chroma 1.5.9 and VecturaDB 1.0.0-preview.9 on 15 November 2026. The ones for macOS were run once, on 15 November 2026, on the macOS runners of GitHub, Apple silicon and Intel, against Chroma 1.5.9 and VecturaDB 1.0.0-preview.9, both ways: not in the CI of every pull request.

Each file also has its Sigstore signature, `<file>.sigstore.json`. On macOS a file downloaded with a browser needs `xattr -d com.apple.quarantine chromadotnet-osx-arm64` before it runs.

### Docker

From 2.22.0, each release also has the image `ghcr.io/chromadotnet/chromadotnet`, tagged with its version: the file for musl x64 on Alpine, for linux/amd64. The backup goes in the folder mounted on `/data`, written with the owner of that folder by `--user`:

```
docker run --rm --network host --user "$(id -u):$(id -g)" -v "$PWD:/data" ghcr.io/chromadotnet/chromadotnet:2.22.0 backup --url http://localhost:8000 --all --out backup.zip
```

The CI of every pull request builds the image and runs it against Chroma 1.5.9, with a backup, a restore and a verification. The image is signed by its digest with Sigstore and has its build provenance on GitHub.

Chroma Cloud:

```
export CHROMA_API_KEY=...
chromadotnet backup --url https://api.trychroma.com --database my-database --all --out cloud.zip
```

Exit codes: 0 when done; 1 for an error, or a collection that a restore left as it was; 2 for a backup of a server that took writes, or a verification that found a difference.

## The library

```csharp
using ChromaDB.Client;
using ChromaDB.Client.Backup;

var client = new ChromaClient(new ChromaConfigurationOptions("http://localhost:8000"));
var backup = await ChromaBackup.BackupAsync(client, "backup.zip", new() { Collections = ["documents"] });
if (backup.ChangedDuringBackup)
{
	Console.WriteLine("The server took writes during the backup.");
}

var other = new ChromaClient(new ChromaConfigurationOptions("http://localhost:8001"));
await ChromaBackup.RestoreAsync(other, "backup.zip");
var verify = await ChromaBackup.VerifyAsync(other, "backup.zip");
Console.WriteLine(verify.Same);
```

The tenant and the database come from the options of the client. An `IProgress<ChromaBackupProgress>` gets the records done and the requests that wait.

## What the file holds

A zip file:

- `manifest.json`: the format and its version, the server, the times, the tenant and the database, and for each collection its counts and the SHA-256 of its records. A file whose records do not match it throws `ChromaBackupFormatException`.
- `collections/0001/collection.json`: the name, the metadata, `configuration_json` and `schema` as the server sent them, and the dimension.
- `collections/0001/records.jsonl`: a record a line, with `id`, `metadata`, `embedding`, `text_chunk` (the document) and `uri`. The first four are the names of the lines of [Chroma Data Pipes](https://github.com/amikos-tech/chromadb-data-pipes), so its `cdp import` can read the records of a collection without lists or sparse vectors in its metadata.

The embeddings are numbers that read back as the same floats. The metadata keeps the JSON of Chroma: an integer stays `1`, a float `1.0`, a list a list, a sparse vector `{"#type":"sparse_vector",...}`.

The records come with gets of `limit` and `offset`, in pages, written in the file in their order: the whole collection is never in memory.

## What a backup does not save

The API does not give these, so the file does not have them:

- the id and the CRN of a collection: a restore creates a collection with the same name;
- the version, the position of the log and the dates of a collection;
- the forks, which come back as collections of their own;
- the attached functions;
- the index itself, which the server builds again: the results of an approximate query can be close to the ones before, not the same;
- the settings of tenants and databases, the users and the keys.

What the server changes on its own stays changed. In a collection with the space `cosine`, Chroma can change the last bit of an embedding (KD-8 in [COMPATIBILITY.md](COMPATIBILITY.md)), and it gives back `-0.0` in metadata as `0.0` (KD-11). When Chroma 1.x sends an embedding as `null`, for a float it cannot write, the file has no embedding for that record: a restore cannot write it, and the backup lists its id.

## Restore into a collection that exists

A restore compares every collection of the file that exists on the server before it writes anything, as the `copy` command of the CLI of VecturaDB does. A collection that is the same stays as it is. One that is not:

- with `ChromaRestoreExisting.Fail`, the default of the library, the restore writes nothing and throws;
- `Resume` (`--resume`) adds the records the collection does not have, and leaves the rest of it as it is;
- `Force` (`--force`) makes the collection the same as the file: with another schema or dimension it is deleted and created again; otherwise the records that only the server has are deleted, the different ones deleted and added again, and the missing ones added;
- `Skip` leaves the collection as it is, and the others are restored.

`chromadotnet restore` asks for each of them, and with `--resume` or `--force` asks again unless `--yes`. Without a terminal and without `--yes` it leaves the collection as it is and exits with 1.

`Force` deletes and adds a different record instead of an upsert: on Chroma 1.0.21 to 1.5.9 an upsert of a record that exists, with its embedding, can lose a record from the index (KD-49).

A new collection gets the schema of the file as the server sent it. Chroma 1.5.9, a single server, refuses a schema with sparse vector indexes: the error names their keys, and no collection stays.
