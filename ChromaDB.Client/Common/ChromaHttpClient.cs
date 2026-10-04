using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ChromaDB.Client.Models;
using ChromaDB.Client.Models.Requests;

namespace ChromaDB.Client.Common;

// Sends requests with the URI and token of one client, without changing the HttpClient it was given:
// the same HttpClient can be shared by clients for different servers or tokens.
internal sealed class ChromaHttpClient
{
	private readonly HttpClient _httpClient;
	private readonly Uri _baseUri;
	private readonly string? _chromaToken;
	private readonly AuthenticationHeaderValue? _authorization;

	// What the client learns about the server, shared by the ChromaHttpClient made from this one.
	private readonly ServerFacts _server;

	public ChromaRoutes Routes { get; }
	public JsonSerializerOptions DeserializerOptions { get; }
	public bool BatchSplitting { get; }

	public ChromaHttpClient(HttpClient httpClient, ChromaConfigurationOptions options)
	{
		_httpClient = httpClient;
		_server = new ServerFacts();
		_baseUri = CreateBaseUri(options.Uri, options.ApiVersion);
		Routes = options.ApiVersion == ChromaApiVersion.V1 ? ChromaRoutes.V1 : ChromaRoutes.V2;
		DeserializerOptions = HttpClientHelpers.DeserializerOptions(options.MetadataValues);
		BatchSplitting = options.BatchSplitting;
		if (options.ChromaToken is not null and not [])
		{
			if (options.ChromaTokenTransportHeader == ChromaTokenTransportHeader.Authorization)
			{
				_authorization = new AuthenticationHeaderValue("Bearer", options.ChromaToken);
			}
			else
			{
				_chromaToken = options.ChromaToken;
			}
		}
		if (options.BasicAuthUsername is not null)
		{
			if (_authorization is not null)
			{
				throw new ArgumentException("Basic authentication and a token in the Authorization header cannot be used together.", nameof(options));
			}
			_authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.BasicAuthUsername}:{options.BasicAuthPassword}")));
		}
	}

	// The same server, HttpClient and credentials, reading metadata values another way.
	private ChromaHttpClient(ChromaHttpClient other, ChromaMetadataValues metadataValues)
	{
		_httpClient = other._httpClient;
		_baseUri = other._baseUri;
		_chromaToken = other._chromaToken;
		_authorization = other._authorization;
		_server = other._server;
		Routes = other.Routes;
		BatchSplitting = other.BatchSplitting;
		DeserializerOptions = HttpClientHelpers.DeserializerOptions(metadataValues);
	}

	public ChromaHttpClient WithMetadataValues(ChromaMetadataValues metadataValues) => new(this, metadataValues);

	public Uri CreateUri(string endpoint) => new(_baseUri, endpoint);

	// Asked once, when a request needs it, also by concurrent calls; a failed or canceled request is not kept, so the next call asks again.
	// The 0.x servers send their own version; every Chroma 1.x answers "1.0.0", so the version tells only 0.x from 1.x apart.
	public async Task<bool> IsChroma0(CancellationToken cancellationToken)
	{
		if (_server.Version is null)
		{
			await _server.VersionLock.WaitAsync(cancellationToken);
			try
			{
				_server.Version ??= await this.Get<string>(Routes.Version, new RequestQueryParams(), cancellationToken);
			}
			finally
			{
				_server.VersionLock.Release();
			}
		}
		return _server.Version.StartsWith("0.", StringComparison.Ordinal);
	}

	// The endpoints are relative to the base URI, so it needs the trailing slash: without it, new Uri(base, endpoint)
	// replaces the last segment ("api/v2" becomes "api/"). A URI with just the server address gets the path of the API version.
	private static Uri CreateBaseUri(Uri uri, ChromaApiVersion apiVersion)
	{
		if (uri.AbsolutePath is "/")
		{
			return new Uri(uri, apiVersion == ChromaApiVersion.V1 ? "api/v1/" : "api/v2/");
		}
		var path = uri.GetLeftPart(UriPartial.Path);
		return path.EndsWith("/") ? uri : new Uri(path + "/");
	}

	// From pre-flight-checks, asked once like the version. Null for Chroma 0.4.10, which has no pre-flight-checks.
	public async Task<int?> GetMaxBatchSize(CancellationToken cancellationToken)
	{
		if (!_server.MaxBatchSizeKnown)
		{
			await _server.MaxBatchSizeLock.WaitAsync(cancellationToken);
			try
			{
				if (!_server.MaxBatchSizeKnown)
				{
					try
					{
						var maxBatchSize = (await this.Get<ChromaPreFlightChecks>(Routes.PreFlightChecks, new RequestQueryParams(), cancellationToken)).MaxBatchSize;
						_server.MaxBatchSize = maxBatchSize > 0 ? maxBatchSize : null;
					}
					catch (ChromaException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
					{
						_server.MaxBatchSize = null;
					}
					_server.MaxBatchSizeKnown = true;
				}
			}
			finally
			{
				_server.MaxBatchSizeLock.Release();
			}
		}
		return _server.MaxBatchSize;
	}

	public Task<HttpResponseMessage> SendAsync(HttpRequestMessage httpRequestMessage, CancellationToken cancellationToken)
	{
		if (_chromaToken is not null)
		{
			httpRequestMessage.Headers.Add(ClientConstants.ChromaTokenHeader, _chromaToken);
		}
		if (_authorization is not null)
		{
			httpRequestMessage.Headers.Authorization = _authorization;
		}
		return _httpClient.SendAsync(httpRequestMessage, cancellationToken);
	}

	private sealed class ServerFacts
	{
		public string? Version;
		public readonly SemaphoreSlim VersionLock = new(1, 1);
		public int? MaxBatchSize;
		// Volatile: read outside the lock, it is written after MaxBatchSize, so whoever sees it true sees the limit too.
		public volatile bool MaxBatchSizeKnown;
		public readonly SemaphoreSlim MaxBatchSizeLock = new(1, 1);
	}
}
