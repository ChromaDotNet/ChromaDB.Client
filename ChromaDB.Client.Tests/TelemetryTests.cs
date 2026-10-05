using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using ChromaDB.Client.Models;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

// The spans and the durations of the operations, with the attributes of the OpenTelemetry semantic conventions for database clients,
// against a fake server. Each test uses a port of its own, so that it records only its own operations.
[TestFixture]
public class TelemetryTests
{
	[Test]
	public async Task SpanAndDurationOfAnOperation()
	{
		var port = Port();
		using var recorder = new Recorder(port);
		var server = new FakeServer(_ => (HttpStatusCode.OK, "3"));
		Assert.That(await CollectionClient(server, port, "articles").Count(), Is.EqualTo(3));

		var span = recorder.Spans.Single();
		Assert.That((span.DisplayName, span.Kind, span.Status), Is.EqualTo(("count articles", ActivityKind.Client, ActivityStatusCode.Unset)));
		var expected = new Dictionary<string, object?>
		{
			["db.system.name"] = "chroma",
			["db.operation.name"] = "count",
			["db.collection.name"] = "articles",
			["db.namespace"] = "default_tenant|default_database",
			["server.address"] = "localhost",
			["server.port"] = port,
		};
		Assert.That(span.TagObjects.ToDictionary(x => x.Key, x => x.Value), Is.EquivalentTo(expected));
		var duration = recorder.Durations.Single();
		Assert.That(duration.Tags, Is.EquivalentTo(expected));
		Assert.That(duration.Value, Is.GreaterThanOrEqualTo(0));
	}

	// An error of the server: the HTTP status is error.type and db.response.status_code.
	[Test]
	public async Task ErrorOfTheServer()
	{
		var port = Port();
		using var recorder = new Recorder(port);
		var server = new FakeServer(_ => (HttpStatusCode.NotFound, """{"error":"NotFoundError","message":"Collection [articles] does not exist"}"""));
		await Assert.ThatAsync(() => CollectionClient(server, port, "articles").Count(), Throws.InstanceOf<ChromaException>());

		var span = recorder.Spans.Single();
		Assert.That(span.Status, Is.EqualTo(ActivityStatusCode.Error));
		Assert.That((span.GetTagItem("error.type"), span.GetTagItem("db.response.status_code")), Is.EqualTo(("404", "404")));
		var tags = recorder.Durations.Single().Tags;
		Assert.That((tags["error.type"], tags["db.response.status_code"]), Is.EqualTo(("404", "404")));
	}

	// Without an answer, error.type is the exception the client wraps.
	[Test]
	public async Task ErrorWithoutAnAnswer()
	{
		var port = Port();
		using var recorder = new Recorder(port);
		var server = new FakeServer(_ => throw new HttpRequestException("Connection refused"));
		await Assert.ThatAsync(() => CollectionClient(server, port, "articles").Count(), Throws.InstanceOf<ChromaException>());

		var span = recorder.Spans.Single();
		Assert.That(span.Status, Is.EqualTo(ActivityStatusCode.Error));
		Assert.That((span.GetTagItem("error.type"), span.GetTagItem("db.response.status_code")), Is.EqualTo(("System.Net.Http.HttpRequestException", (object?)null)));
	}

	// A missing collection is the answer of CollectionExists, not an error, and the request has no span of its own.
	[Test]
	public async Task CollectionExistsWithoutError()
	{
		var port = Port();
		using var recorder = new Recorder(port);
		var server = new FakeServer(_ => (HttpStatusCode.NotFound, """{"error":"NotFoundError","message":"Collection [articles] does not exist"}"""));
		Assert.That(await Client(server, port).CollectionExists("articles"), Is.False);

		var span = recorder.Spans.Single();
		Assert.That((span.DisplayName, span.Status, span.GetTagItem("error.type")), Is.EqualTo(("collection_exists articles", ActivityStatusCode.Unset, (object?)null)));
	}

	// The operations on the server have no namespace, the ones on a tenant the tenant, the ones in a database tenant|database.
	[Test]
	public async Task NamespaceOfEachOperation()
	{
		var port = Port();
		using var recorder = new Recorder(port);
		var server = new FakeServer(r => r.EndsWith("/heartbeat") ? (HttpStatusCode.OK, """{"nanosecond heartbeat":1}""")
			: r.EndsWith("/databases") ? (HttpStatusCode.OK, "[]")
			: (HttpStatusCode.OK, "{}"));
		var client = Client(server, port);
		await client.Heartbeat();
		await client.ListDatabases("tenant1");
		await client.CreateDatabase("database1", "tenant1");
		await client.DeleteCollection("articles");

		Assert.That(recorder.Spans.Select(x => (x.DisplayName, x.GetTagItem("db.namespace"))), Is.EqualTo(new (string, object?)[]
		{
			("heartbeat", null),
			("list_databases", "tenant1"),
			("create_database", "tenant1|database1"),
			("delete_collection articles", "default_tenant|default_database"),
		}));
	}

	static int Port() => Random.Shared.Next(20000, 60000);

	static ChromaClient Client(HttpMessageHandler handler, int port)
		=> new(new ChromaConfigurationOptions($"http://localhost:{port}"), new HttpClient(handler));

	static ChromaCollectionClient CollectionClient(HttpMessageHandler handler, int port, string name)
		=> new(new ChromaCollection(name) { Id = Guid.Parse("11111111-2222-3333-4444-555555555555") }, new ChromaConfigurationOptions($"http://localhost:{port}"), new HttpClient(handler));

	sealed class Recorder : IDisposable
	{
		private readonly ActivityListener _activities;
		private readonly MeterListener _meters;

		public List<Activity> Spans { get; } = [];
		public List<(double Value, Dictionary<string, object?> Tags)> Durations { get; } = [];

		public Recorder(int port)
		{
			_activities = new ActivityListener
			{
				ShouldListenTo = source => source.Name == ChromaTelemetry.ActivitySourceName,
				Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
				ActivityStopped = activity =>
				{
					if (Equals(activity.GetTagItem("server.port"), port))
					{
						lock (Spans)
						{
							Spans.Add(activity);
						}
					}
				},
			};
			ActivitySource.AddActivityListener(_activities);
			_meters = new MeterListener
			{
				InstrumentPublished = (instrument, listener) =>
				{
					if (instrument.Meter.Name == ChromaTelemetry.MeterName)
					{
						listener.EnableMeasurementEvents(instrument);
					}
				},
			};
			_meters.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
			{
				var all = new Dictionary<string, object?>();
				foreach (var tag in tags)
				{
					all[tag.Key] = tag.Value;
				}
				if (instrument.Name == "db.client.operation.duration" && Equals(all["server.port"], port))
				{
					lock (Durations)
					{
						Durations.Add((value, all));
					}
				}
			});
			_meters.Start();
		}

		public void Dispose()
		{
			_activities.Dispose();
			_meters.Dispose();
		}
	}

	sealed class FakeServer(Func<string, (HttpStatusCode Status, string Body)> answer) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var (status, body) = answer(request.RequestUri!.AbsolutePath);
			return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
		}
	}
}
