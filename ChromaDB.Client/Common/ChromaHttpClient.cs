using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
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

	private string? _serverVersion;
	private readonly SemaphoreSlim _serverVersionLock = new(1, 1);

	public ChromaRoutes Routes { get; }
	public JsonSerializerOptions DeserializerOptions { get; }

	public ChromaHttpClient(HttpClient httpClient, ChromaConfigurationOptions options)
	{
		_httpClient = httpClient;
		_baseUri = CreateBaseUri(options.Uri, options.ApiVersion);
		Routes = options.ApiVersion == ChromaApiVersion.V1 ? ChromaRoutes.V1 : ChromaRoutes.V2;
		DeserializerOptions = HttpClientHelpers.DeserializerOptions(options.MetadataValues);
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

	public Uri CreateUri(string endpoint) => new(_baseUri, endpoint);

	// Asked once, when a request needs it, also by concurrent calls; a failed or canceled request is not kept, so the next call asks again.
	// The 0.x servers send their own version; every Chroma 1.x answers "1.0.0", so the version tells only 0.x from 1.x apart.
	public async Task<bool> IsChroma0(CancellationToken cancellationToken)
	{
		if (_serverVersion is null)
		{
			await _serverVersionLock.WaitAsync(cancellationToken);
			try
			{
				_serverVersion ??= await this.Get<string>(Routes.Version, new RequestQueryParams(), cancellationToken);
			}
			finally
			{
				_serverVersionLock.Release();
			}
		}
		return _serverVersion.StartsWith("0.", StringComparison.Ordinal);
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
}
