using System.Collections.Concurrent;
using System.Text.Json;

namespace ChromaDB.Client.Tests;

// What the requests of a fixture created on a server already running: the collections by their id, which stays the same
// when a test renames them, and the databases. The fixture deletes these at its end and nothing else: other clients may
// be creating their own collections in the same database.
sealed class CreatedOnTheServer
{
	public ConcurrentDictionary<Guid, (string? Tenant, string? Database)> Collections { get; } = new();
	public ConcurrentDictionary<(string? Tenant, string Name), bool> Databases { get; } = new();

	public HttpClient NewHttpClient() => new(new Recorder(this));

	sealed class Recorder(CreatedOnTheServer created) : DelegatingHandler(new HttpClientHandler())
	{
		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var uri = request.RequestUri!;
			var path = uri.AbsolutePath.TrimEnd('/');
			var post = request.Method == HttpMethod.Post;
			string? database = null;
			if (post && path.EndsWith("/databases", StringComparison.Ordinal) && request.Content is not null)
			{
				using var body = JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
				database = body.RootElement.GetProperty("name").GetString();
			}
			var response = await base.SendAsync(request, cancellationToken);
			if (!response.IsSuccessStatusCode)
			{
				return response;
			}
			if (database is not null)
			{
				created.Databases[(Part(uri, "tenants", "tenant"), database)] = true;
			}
			else if (post && path.EndsWith("/collections", StringComparison.Ordinal))
			{
				using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
				created.Collections[body.RootElement.GetProperty("id").GetGuid()] = (Part(uri, "tenants", "tenant"), Part(uri, "databases", "database"));
			}
			return response;
		}

		// The tenant or the database of a request: in the path of the v2 API, in the query of the v1 API, null for the default one.
		static string? Part(Uri uri, string segment, string parameter)
		{
			var segments = uri.AbsolutePath.Split('/');
			var index = Array.IndexOf(segments, segment);
			if (index >= 0 && index + 1 < segments.Length && segments[index + 1].Length > 0)
			{
				return Uri.UnescapeDataString(segments[index + 1]);
			}
			return uri.Query.TrimStart('?').Split('&')
				.Select(x => x.Split('=', 2))
				.Where(x => x.Length == 2 && x[0] == parameter)
				.Select(x => Uri.UnescapeDataString(x[1]))
				.FirstOrDefault();
		}
	}
}
