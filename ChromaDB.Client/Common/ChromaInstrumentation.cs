using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;

namespace ChromaDB.Client.Common;

// The spans and the durations of the operations, as the OpenTelemetry semantic conventions for database clients describe them:
// a client span "{operation} {collection}" around all the requests of an operation, and its duration in db.client.operation.duration.
internal static class ChromaInstrumentation
{
	private static readonly string? Version = typeof(ChromaInstrumentation).Assembly.GetName().Version?.ToString();
	private static readonly ActivitySource Source = new(ChromaTelemetry.ActivitySourceName, Version);
	private static readonly Meter Meter = new(ChromaTelemetry.MeterName, Version);
	// In seconds, with the bucket boundaries that the semantic conventions advise for it: without them OpenTelemetry uses its default
	// ones, made for milliseconds, and every operation under 5 s falls in the same bucket. InstrumentAdvice is in
	// System.Diagnostics.DiagnosticSource 9 and later, which the netstandard2.0 build does not take.
#if NETSTANDARD2_0
	private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("db.client.operation.duration", "s", "Duration of database client operations.");
#else
	private static readonly Histogram<double> Duration = Meter.CreateHistogram("db.client.operation.duration", "s", "Duration of database client operations.", tags: null,
		advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.001, 0.005, 0.01, 0.05, 0.1, 0.5, 1, 5, 10] });
#endif

	public static async Task<T> Run<T>(string operation, string? collection, string? @namespace, Uri server, Func<Task<T>> body)
	{
		if (!Source.HasListeners() && !Duration.Enabled)
		{
			return await body();
		}
		var tags = new TagList
		{
			{ "db.system.name", "chroma" },
			{ "db.operation.name", operation },
			{ "server.address", server.Host },
			{ "server.port", server.Port },
		};
		if (collection is not null)
		{
			tags.Add("db.collection.name", collection);
		}
		if (@namespace is not null)
		{
			tags.Add("db.namespace", @namespace);
		}
		var start = Stopwatch.GetTimestamp();
		using var activity = Source.StartActivity(collection is null ? operation : $"{operation} {collection}", ActivityKind.Client, default(ActivityContext), tags);
		try
		{
			return await body();
		}
		catch (Exception ex)
		{
			// The HTTP status when the server answered, otherwise the exception: the one the client wraps, like HttpRequestException.
			var status = (ex as ChromaException)?.StatusCode is { } code ? ((int)code).ToString(CultureInfo.InvariantCulture) : null;
			var errorType = status ?? (ex is ChromaException { InnerException: { } inner } ? inner : ex).GetType().FullName!;
			tags.Add("error.type", errorType);
			activity?.SetTag("error.type", errorType);
			if (status is not null)
			{
				tags.Add("db.response.status_code", status);
				activity?.SetTag("db.response.status_code", status);
			}
			activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
			throw;
		}
		finally
		{
			Duration.Record((Stopwatch.GetTimestamp() - start) / (double)Stopwatch.Frequency, tags);
		}
	}

	public static Task Run(string operation, string? collection, string? @namespace, Uri server, Func<Task> body)
		=> Run<object?>(operation, collection, @namespace, server, async () =>
		{
			await body();
			return null;
		});
}
