# Health, traces and metrics

## Health of the server

```csharp
var health = await client.HealthcheckAsync();
Console.WriteLine(health.IsExecutorReady);
```

[docs/COMPATIBILITY.md](COMPATIBILITY.md) lists the servers that have `HealthcheckAsync`. A server that is not ready answers `503`, and the client throws a `ChromaException`.

## Traces and metrics

```csharp
builder.Services.AddOpenTelemetry()
	.WithTracing(tracing => tracing.AddSource(ChromaTelemetry.ActivitySourceName))
	.WithMetrics(metrics => metrics.AddMeter(ChromaTelemetry.MeterName));
```

- **Spans:** each operation, like `query` or `add`, is a client span named after the operation and the collection, like `query articles`. Its requests, the batches of a large write too, are inside it.
- **Attributes**, from the OpenTelemetry semantic conventions for database clients:
  - `db.system.name` `chroma`, `db.operation.name` and `db.collection.name`;
  - `db.namespace`, the tenant and the database as `tenant|database`;
  - `server.address` and `server.port`;
  - on a failure, `error.type` and `db.response.status_code`, the HTTP status.
- **Metrics:** the duration of each operation in the `db.client.operation.duration` histogram, in seconds, with the same attributes.
- **Buckets of the histogram:** the boundaries the semantic conventions advise, 0.001, 0.005, 0.01, 0.05, 0.1, 0.5, 1, 5 and 10 seconds. OpenTelemetry 1.10 and later apply them. Without them, OpenTelemetry would use its default boundaries, made for milliseconds, and every operation under 5 seconds would fall in the same bucket. The netstandard2.0 build, used by applications on .NET Core 3.1 to 7, cannot advise them, so there the application adds a view with them:

  ```csharp
  metrics.AddView("db.client.operation.duration", new ExplicitBucketHistogramConfiguration { Boundaries = [0.001, 0.005, 0.01, 0.05, 0.1, 0.5, 1, 5, 10] });
  ```

- **Cost:** without a listener, nothing is measured. A missing collection in `CollectionExistsAsync` is an answer, not an error.
