using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace ChromaDB.Client.DependencyInjection;

public static class ChromaClientExtensions
{
	public static void AddChromaClient(this IServiceCollection services, Func<ChromaConfigurationOptions?, ChromaConfigurationOptions>? configurationOptions = null)
		=> AddChromaClient(services, configurationOptions, _ => { });

	// configureHttpClient configures the HttpClient of the client, like a resilience handler, a proxy or a timeout.
	public static void AddChromaClient(this IServiceCollection services, Func<ChromaConfigurationOptions?, ChromaConfigurationOptions>? configurationOptions, Action<IHttpClientBuilder> configureHttpClient)
	{
		configurationOptions ??= DefaultConfigurationOptions;

		ChromaConfigurationOptions options = new();
		options = configurationOptions(options);

		services.AddSingleton(options);
		configureHttpClient(services.AddHttpClient(nameof(ChromaClient)));
		services.AddSingleton(serviceProvider => new ChromaClient(options, CreateHttpClient(serviceProvider, nameof(ChromaClient))));
	}

	// A client and its options under a key, for an application that talks to more than one server, tenant or database.
	public static void AddKeyedChromaClient(this IServiceCollection services, object? serviceKey, Func<ChromaConfigurationOptions?, ChromaConfigurationOptions>? configurationOptions = null)
		=> AddKeyedChromaClient(services, serviceKey, configurationOptions, _ => { });

	public static void AddKeyedChromaClient(this IServiceCollection services, object? serviceKey, Func<ChromaConfigurationOptions?, ChromaConfigurationOptions>? configurationOptions, Action<IHttpClientBuilder> configureHttpClient)
	{
		configurationOptions ??= DefaultConfigurationOptions;

		ChromaConfigurationOptions options = new();
		options = configurationOptions(options);

		var httpClientName = $"{nameof(ChromaClient)}:{serviceKey}";
		services.AddKeyedSingleton(serviceKey, options);
		configureHttpClient(services.AddHttpClient(httpClientName));
		services.AddKeyedSingleton(serviceKey, (serviceProvider, _) => new ChromaClient(options, CreateHttpClient(serviceProvider, httpClientName)));
	}

	// The client is a singleton and keeps its HttpClient, which sends each request with the current handler of the factory:
	// the factory renews it after its handler lifetime, two minutes by default, so a change of the server in the DNS is seen
	// on every target, also .NET Framework. The settings of the HttpClient itself, like ConfigureHttpClient, are applied here.
	private static HttpClient CreateHttpClient(IServiceProvider serviceProvider, string name)
	{
		var httpClient = new HttpClient(new CurrentHandler(serviceProvider.GetRequiredService<IHttpMessageHandlerFactory>(), name));
		foreach (var action in serviceProvider.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>().Get(name).HttpClientActions)
		{
			action(httpClient);
		}
		return httpClient;
	}

	private sealed class CurrentHandler(IHttpMessageHandlerFactory factory, string name) : HttpMessageHandler
	{
		// The factory owns the handler, so the invoker does not dispose it.
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			=> new HttpMessageInvoker(factory.CreateHandler(name), disposeHandler: false).SendAsync(request, cancellationToken);
	}

	private static ChromaConfigurationOptions DefaultConfigurationOptions(ChromaConfigurationOptions? options = null)
		=> options ?? new ChromaConfigurationOptions();
}
