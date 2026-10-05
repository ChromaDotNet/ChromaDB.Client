namespace ChromaDB.Client;

/// <summary>
/// The names of the traces and of the metrics of the client, for OpenTelemetry: <c>AddSource(ChromaTelemetry.ActivitySourceName)</c>
/// and <c>AddMeter(ChromaTelemetry.MeterName)</c>. Each operation, like <c>query</c> or <c>add</c>, is a client span named after the
/// operation and the collection, like <c>query articles</c>, with the attributes of the OpenTelemetry semantic conventions for database
/// clients: <c>db.system.name</c> <c>chroma</c>, <c>db.operation.name</c>, <c>db.collection.name</c>, <c>db.namespace</c> (the tenant
/// and the database, as <c>tenant|database</c>), <c>server.address</c>, <c>server.port</c>, and on a failure <c>error.type</c> and
/// <c>db.response.status_code</c>, the HTTP status. The duration of each operation goes in the histogram
/// <c>db.client.operation.duration</c>, in seconds, with the same attributes, and with the bucket boundaries that the semantic
/// conventions advise, from 0.001 to 10 seconds, which OpenTelemetry 1.10 and later apply; the netstandard2.0 build cannot advise
/// them, so there a view sets them. Without a listener nothing is measured.
/// </summary>
public static class ChromaTelemetry
{
	/// <summary>
	/// The name of the <c>ActivitySource</c> of the spans of the operations.
	/// </summary>
	public const string ActivitySourceName = "ChromaDB.Client";

	/// <summary>
	/// The name of the <c>Meter</c> of <c>db.client.operation.duration</c>.
	/// </summary>
	public const string MeterName = "ChromaDB.Client";
}
