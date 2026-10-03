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
		_baseUri = options.Uri;
		_chromaToken = options.ChromaToken;
	}

	public Uri CreateUri(string endpoint) => new(_baseUri, endpoint);

	public Task<HttpResponseMessage> SendAsync(HttpRequestMessage httpRequestMessage, CancellationToken cancellationToken)
	{
		if (_chromaToken is not null and not [])
		{
			httpRequestMessage.Headers.Add(ClientConstants.ChromaTokenHeader, _chromaToken);
		}
		return _httpClient.SendAsync(httpRequestMessage, cancellationToken);
	}
}
