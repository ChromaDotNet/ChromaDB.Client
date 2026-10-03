using System.Net.Http.Headers;
using System.Text;

namespace ChromaDB.Client.Common;

// Sends requests with the URI and token of one client, without changing the HttpClient it was given:
// the same HttpClient can be shared by clients for different servers or tokens.
internal sealed class ChromaHttpClient
{
	private readonly HttpClient _httpClient;
	private readonly Uri _baseUri;
	private readonly string? _chromaToken;
	private readonly AuthenticationHeaderValue? _authorization;

	public ChromaHttpClient(HttpClient httpClient, ChromaConfigurationOptions options)
	{
		_httpClient = httpClient;
		_baseUri = CreateBaseUri(options.Uri);
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
