namespace ChromaDB.Client.Common;

// Sends requests with the URI and token of one client, without changing the HttpClient it was given:
// the same HttpClient can be shared by clients for different servers or tokens.
internal sealed class ChromaHttpClient
{
	private readonly HttpClient _httpClient;
	private readonly Uri _baseUri;
	private readonly string? _chromaToken;

	public ChromaHttpClient(HttpClient httpClient, ChromaConfigurationOptions options)
	{
		_httpClient = httpClient;
		_baseUri = CreateBaseUri(options.Uri);
		_chromaToken = options.ChromaToken;
	}

	public Uri CreateUri(string endpoint) => new(_baseUri, endpoint);

	// The endpoints are relative to the base URI, so it needs the trailing slash: without it, new Uri(base, endpoint)
	// replaces the last segment ("api/v2" becomes "api/"). A URI with just the server address gets the v2 API path.
	private static Uri CreateBaseUri(Uri uri)
	{
		if (uri.AbsolutePath is "/")
		{
			return new Uri(uri, "api/v2/");
		}
		var path = uri.GetLeftPart(UriPartial.Path);
		return path.EndsWith("/") ? uri : new Uri(path + "/");
	}

	public Task<HttpResponseMessage> SendAsync(HttpRequestMessage httpRequestMessage, CancellationToken cancellationToken)
	{
		if (_chromaToken is not null and not [])
		{
			httpRequestMessage.Headers.Add(ClientConstants.ChromaTokenHeader, _chromaToken);
		}
		return _httpClient.SendAsync(httpRequestMessage, cancellationToken);
	}
}
