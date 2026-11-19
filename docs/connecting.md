# Connecting

## The URI and the HttpClient

The URI can be just the address of the server, like `http://localhost:8000`, and the client adds `/api/v2/`. A URI with a path, like `http://localhost:8000/api/v2`, is used as it is, with or without the trailing slash.

Without an `HttpClient`, `new ChromaClient("http://localhost:8000")` or `new ChromaClient(options)` creates its own, and `Dispose` closes it: `using var client = new ChromaClient("http://localhost:8000");`. The clients it returns, like the collection clients, share that `HttpClient`. An `HttpClient` you pass to the constructor stays open.

## Redirects

The client follows a redirect only to the same server: the same host and port, or from `http` to `https` on the same host. It sends the token and the basic authentication again only there. A redirect to another host or port, or from `https` to `http`, is a `ChromaException`, and the request does not go. After 5 redirects the request is a `ChromaException` too. A `307` and a `308` keep the method and the body; a `301` or a `302` of a `POST`, and a `303`, become a `GET` without a body.

The `HttpClient` the client creates, and the one of `AddChromaClient` and the other methods of dependency injection, do not follow redirects themselves. An `HttpClient` you pass to the constructor follows them as its handler says, and by default it follows them to any server, with the `X-Chroma-Token` header: the client then throws a `ChromaException` when the answer comes from another server, but the token has already gone there. Turn off the redirects of its handler, and the client follows them as above:

```csharp
using var httpClient = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false });
var client = new ChromaClient(options, httpClient);
```

With dependency injection, a primary handler that `configureHttpClient` sets, like `ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler())`, follows redirects unless its `AllowAutoRedirect` is false.

## The size of the answers

The client reads an answer of at most 512 MB: a larger one is a `ChromaException`, with any `HttpClient`. `WithMaxResponseSize` sets another limit, in bytes: `options.WithMaxResponseSize(1L << 30)`. The body of an error is read up to 64 KB, and the message of the exception keeps its first 4 KB, followed by ` (truncated)`.

## Sharing a client between threads

`ChromaClient` and the collection clients can be shared between threads, as singletons: a call keeps its state to itself, what the client learns about the server is read once under a lock, and a collection client made by name may read its collection more than once when calls start together. Two clients that write the same records at the same time get what Chroma does with them.

## API version

The client uses the v2 API. For the servers that have only the v1 API, like Chroma 0.5.15, choose it once in the options:

```csharp
var options = new ChromaConfigurationOptions(uri: "http://localhost:8000").WithApiVersion(ChromaApiVersion.V1);

// or, with dependency injection
services.AddChromaClient(options => options!.WithUri("http://localhost:8000").WithApiVersion(ChromaApiVersion.V1));
```

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

The client adds the credentials to each of its requests, without changing the `HttpClient` you pass.

## Connection strings

From a connection string, as the settings of an application keep it:

```csharp
var options = ChromaConfigurationOptions.FromConnectionString("Endpoint=https://api.trychroma.com;Token=ck-...;Tenant=...;Database=...");
```

`Endpoint` is the http or https URI of the server, and a connection string that is just a URI is the endpoint alone; another key, no endpoint, an endpoint without http or https, or a `Distributed` that is not `true` or `false` throws an `ArgumentException` that says which. `Token` goes in the `X-Chroma-Token` header. `Tenant` and `Database` are the ones of the requests. `Distributed=true` says that the server is distributed Chroma, as `WithDistributedChroma()` does.

## Distributed Chroma

Distributed Chroma is the one Chroma Cloud runs on. Installed elsewhere, as with the Helm chart of Chroma or an operator, it answers like a single Chroma server to the questions the client could ask it: the same version, `1.0.0`, and no list of what it serves in `pre-flight-checks`. The options say it:

```csharp
var options = new ChromaConfigurationOptions(uri: "http://chroma.internal:8000").WithDistributedChroma();
// or, in a connection string
var fromSettings = ChromaConfigurationOptions.FromConnectionString("Endpoint=http://chroma.internal:8000;Distributed=true");
```

Then the client sends the indexes added with `ifSupported` ([sparse-vectors-and-schema.md](sparse-vectors-and-schema.md)), throws an `ArgumentException` before a filter on the documents that distributed Chroma gets wrong, also in a delete (KD-56 and KD-57 in [COMPATIBILITY.md](COMPATIBILITY.md#known-defects-of-the-servers)), and deletes a collection or a database in one request, as KD-12 is a defect of a single server. Chroma Cloud, at `*.trychroma.com` or with `WithChromaCloud()`, is distributed Chroma whatever the options say of it, and only Chroma Cloud has its quotas ([chroma-cloud.md](chroma-cloud.md)). A server that says what it is in the server block of `pre-flight-checks`, like VecturaDB, is what it says, whatever the options say.

`IsDistributedChroma` of the options tells whether the client takes the server for distributed Chroma, Chroma Cloud included; `DistributedChroma` only whether the options say it, with `WithDistributedChroma()` or `Distributed=true`, for code that passes the setting on.
