using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using ChromaDB.Client.Models.Requests;
using ChromaDB.Client.Models.Responses;

namespace ChromaDB.Client.Common;

internal static partial class HttpClientHelpers
{
	internal static readonly JsonSerializerOptions PostJsonSerializerOptions = new()
	{
		AllowTrailingCommas = false,
		ReferenceHandler = ReferenceHandler.IgnoreCycles,
		ReadCommentHandling = JsonCommentHandling.Skip,
		TypeInfoResolver = ChromaJsonResolver.Instance,
	};

	private static readonly JsonSerializerOptions DeserializerJsonSerializerOptions = new()
	{
		Converters =
		{
			new ObjectToInferredTypesJsonConverter(),
		},
		TypeInfoResolver = ChromaJsonResolver.Instance,
	};

	private static readonly JsonSerializerOptions ExactDeserializerJsonSerializerOptions = new()
	{
		Converters =
		{
			new ObjectToExactTypesJsonConverter(),
		},
		TypeInfoResolver = ChromaJsonResolver.Instance,
	};

	// The generated metadata of the type: serialization without reflection, which trimming and NativeAOT keep.
	internal static JsonTypeInfo<T> TypeInfo<T>(this JsonSerializerOptions options)
		=> (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));

	public static JsonSerializerOptions DeserializerOptions(ChromaMetadataValues metadataValues)
		=> metadataValues == ChromaMetadataValues.Exact ? ExactDeserializerJsonSerializerOptions : DeserializerJsonSerializerOptions;

	public static async Task<TResponse> Get<TResponse>(this ChromaHttpClient httpClient, string endpoint, RequestQueryParams queryParams, CancellationToken cancellationToken)
	{
		using var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, requestUri: httpClient.CreateUri(ValidateAndPrepareEndpoint(endpoint, queryParams)));
		return await Send<TResponse>(httpClient, httpRequestMessage, cancellationToken);
	}
	public static async Task Get(this ChromaHttpClient httpClient, string endpoint, RequestQueryParams queryParams, CancellationToken cancellationToken)
	{
		using var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, requestUri: httpClient.CreateUri(ValidateAndPrepareEndpoint(endpoint, queryParams)));
		await Send(httpClient, httpRequestMessage, cancellationToken);
	}

	public static async Task<TResponse> Post<TInput, TResponse>(this ChromaHttpClient httpClient, string endpoint, TInput? input, RequestQueryParams queryParams, CancellationToken cancellationToken)
	{
		using var content = new StringContent(JsonSerializer.Serialize(input, PostJsonSerializerOptions.TypeInfo<TInput?>()), Encoding.UTF8, "application/json");
		using var httpRequestMessage = new HttpRequestMessage(HttpMethod.Post, requestUri: httpClient.CreateUri(ValidateAndPrepareEndpoint(endpoint, queryParams)))
		{
			Content = content,
			Headers = { Accept = { new MediaTypeWithQualityHeaderValue("application/json") } }
		};
		return await Send<TResponse>(httpClient, httpRequestMessage, cancellationToken);
	}
	public static async Task Post<TInput>(this ChromaHttpClient httpClient, string endpoint, TInput? input, RequestQueryParams queryParams, CancellationToken cancellationToken)
	{
		using var content = new StringContent(JsonSerializer.Serialize(input, PostJsonSerializerOptions.TypeInfo<TInput?>()), Encoding.UTF8, "application/json");
		using var httpRequestMessage = new HttpRequestMessage(HttpMethod.Post, requestUri: httpClient.CreateUri(ValidateAndPrepareEndpoint(endpoint, queryParams)))
		{
			Content = content,
			Headers = { Accept = { new MediaTypeWithQualityHeaderValue("application/json") } }
		};
		await Send(httpClient, httpRequestMessage, cancellationToken);
	}

	public static async Task<TResponse> Put<TInput, TResponse>(this ChromaHttpClient httpClient, string endpoint, TInput? input, RequestQueryParams queryParams, CancellationToken cancellationToken)
	{
		using var content = new StringContent(JsonSerializer.Serialize(input, PostJsonSerializerOptions.TypeInfo<TInput?>()), Encoding.UTF8, "application/json");
		using var httpRequestMessage = new HttpRequestMessage(HttpMethod.Put, requestUri: httpClient.CreateUri(ValidateAndPrepareEndpoint(endpoint, queryParams)))
		{
			Content = content,
			Headers = { Accept = { new MediaTypeWithQualityHeaderValue("application/json") } }
		};
		return await Send<TResponse>(httpClient, httpRequestMessage, cancellationToken);
	}
	public static async Task Put<TInput>(this ChromaHttpClient httpClient, string endpoint, TInput? input, RequestQueryParams queryParams, CancellationToken cancellationToken)
	{
		using var content = new StringContent(JsonSerializer.Serialize(input, PostJsonSerializerOptions.TypeInfo<TInput?>()), Encoding.UTF8, "application/json");
		using var httpRequestMessage = new HttpRequestMessage(HttpMethod.Put, requestUri: httpClient.CreateUri(ValidateAndPrepareEndpoint(endpoint, queryParams)))
		{
			Content = content,
			Headers = { Accept = { new MediaTypeWithQualityHeaderValue("application/json") } }
		};
		await Send(httpClient, httpRequestMessage, cancellationToken);
	}

	public static async Task<TResponse> Delete<TResponse>(this ChromaHttpClient httpClient, string endpoint, RequestQueryParams queryParams, CancellationToken cancellationToken)
	{
		using var httpRequestMessage = new HttpRequestMessage(HttpMethod.Delete, requestUri: httpClient.CreateUri(ValidateAndPrepareEndpoint(endpoint, queryParams)));
		return await Send<TResponse>(httpClient, httpRequestMessage, cancellationToken);
	}
	public static async Task Delete(this ChromaHttpClient httpClient, string endpoint, RequestQueryParams queryParams, CancellationToken cancellationToken)
	{
		using var httpRequestMessage = new HttpRequestMessage(HttpMethod.Delete, requestUri: httpClient.CreateUri(ValidateAndPrepareEndpoint(endpoint, queryParams)));
		await Send(httpClient, httpRequestMessage, cancellationToken);
	}

	private static async Task<TResponse> Send<TResponse>(ChromaHttpClient httpClient, HttpRequestMessage httpRequestMessage, CancellationToken cancellationToken)
	{
		try
		{
			using var httpResponseMessage = await httpClient.SendAsync(httpRequestMessage, cancellationToken);
			return (int)httpResponseMessage.StatusCode switch
			{
				>= 200 and <= 299 => JsonSerializer.Deserialize(await httpResponseMessage.Content.ReadAsStringAsync(), httpClient.DeserializerOptions.TypeInfo<TResponse>())!,
				_ => throw await HandleErrorStatusCode(httpRequestMessage, httpResponseMessage),
			};
		}
		catch (Exception ex) when (ex is not ChromaException && !(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
		{
			throw new ChromaException(ex.Message, ex);
		}
	}
	private static async Task Send(ChromaHttpClient httpClient, HttpRequestMessage httpRequestMessage, CancellationToken cancellationToken)
	{
		try
		{
			using var httpResponseMessage = await httpClient.SendAsync(httpRequestMessage, cancellationToken);
			switch ((int)httpResponseMessage.StatusCode)
			{
				case >= 200 and <= 299:
					return;
				default:
					throw await HandleErrorStatusCode(httpRequestMessage, httpResponseMessage);
			};
		}
		catch (Exception ex) when (ex is not ChromaException && !(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
		{
			throw new ChromaException(ex.Message, ex);
		}
	}

	private static async Task<ChromaException> HandleErrorStatusCode(HttpRequestMessage httpRequestMessage, HttpResponseMessage httpResponseMessage)
	{
		var errorMessageBody = await httpResponseMessage.Content.ReadAsStringAsync();
		var (message, errorType) = ParseErrorMessageBody(errorMessageBody);
		// A bare 404 or 405 usually means that this version of Chroma does not have the endpoint: name the request.
		if ((int)httpResponseMessage.StatusCode is 404 or 405 && message is null or "Not Found" or "Method Not Allowed")
		{
			return new ChromaException($"{message ?? httpResponseMessage.StatusCode.ToString()}: {httpRequestMessage.Method} {httpRequestMessage.RequestUri?.AbsolutePath}") { StatusCode = httpResponseMessage.StatusCode, ErrorType = errorType };
		}
		return new ChromaException(message ?? $"Unexpected status code: {httpResponseMessage.StatusCode}.") { StatusCode = httpResponseMessage.StatusCode, ErrorType = errorType };
	}

	private static (string? Message, string? ErrorType) ParseErrorMessageBody(string? errorMessageBody)
	{
		if (errorMessageBody is null or [])
		{
			return (null, null);
		}

		try
		{
			var deserialized = JsonSerializer.Deserialize(errorMessageBody, DeserializerJsonSerializerOptions.TypeInfo<GeneralError>())!;
			// v1 API: {"error": "ValueError('...')"}: the kind and the message in one string.
#if NETSTANDARD2_0
			var match = ParseErrorMessageBodyRegex.Match(deserialized?.Error ?? string.Empty);
#else
			var match = ParseErrorMessageBodyRegex().Match(deserialized?.Error ?? string.Empty);
#endif
			// v2 API: {"error": "NotFoundError", "message": "..."}; the kind also without a message.
			var errorType = match.Success
				? (match.Groups["errorType"].Value is { Length: > 0 } type ? type : null)
				: (deserialized?.Error is { Length: > 0 } error ? error : null);
			// Errors of the 0.x servers outside the API, like a 500: {"detail": "..."}.
			var message = deserialized?.Message is { Length: > 0 } text ? text
				: deserialized?.Detail is { Length: > 0 } detail ? detail
				: match.Success ? match.Groups["errorMessage"]?.Value
				: $"Couldn't identify the error message: {errorMessageBody}";
			return (message, errorType);
		}
		catch
		{
			return ($"Couldn't parse the incoming error message body: {errorMessageBody}", null);
		}
	}

	private static List<string> PrepareQueryParams(string input)
	{
#if NETSTANDARD2_0
		return PrepareQueryParamsRegex.Matches(input)
			.Cast<Match>()
			.Select(x => x.Value)
			.ToList();
#else
		return PrepareQueryParamsRegex().Matches(input)
			.Select(x => x.Value)
			.ToList();
#endif
	}

#if NETSTANDARD2_0
	private static readonly Regex ParseErrorMessageBodyRegex = new(@"^(?<errorType>\w*)\('(?<errorMessage>.*)'\)", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled);
#else
	[GeneratedRegex(@"^(?<errorType>\w*)\('(?<errorMessage>.*)'\)", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
	private static partial Regex ParseErrorMessageBodyRegex();
#endif

#if NETSTANDARD2_0
		private static readonly Regex PrepareQueryParamsRegex = new(@"{[a-zA-Z0-9\-_]+}", RegexOptions.CultureInvariant | RegexOptions.Compiled);
#else
	[GeneratedRegex(@"{[a-zA-Z0-9\-_]+}", RegexOptions.CultureInvariant)]
	private static partial Regex PrepareQueryParamsRegex();
#endif

	private static string ValidateAndPrepareEndpoint(string endpoint, RequestQueryParams queryParams)
	{
		var queryArgs = PrepareQueryParams(endpoint);
		return queryArgs is not []
		? FormatRequestUri(endpoint, queryParams)
		: endpoint;
	}

	private static string FormatRequestUri(string endpoint, RequestQueryParams queryParams)
	{
		var formattedEndpoint = endpoint;
		foreach (var (key, value) in queryParams)
		{
			var urlEncodedQueryParam = Uri.EscapeDataString(value);
			formattedEndpoint = formattedEndpoint.Replace(key, urlEncodedQueryParam);
		}
		return formattedEndpoint;
	}
}
