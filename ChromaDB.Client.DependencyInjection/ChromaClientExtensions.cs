using Microsoft.Extensions.DependencyInjection;

namespace ChromaDB.Client.DependencyInjection;

public static class ChromaClientExtensions
{
	public static void AddChromaClient(this IServiceCollection services, Func<ChromaConfigurationOptions?, ChromaConfigurationOptions>? configurationOptions = null)
	{
		configurationOptions ??= DefaultConfigurationOptions;

		ChromaConfigurationOptions options = new();
		options = configurationOptions(options);

		services.AddSingleton(options);
		services.AddHttpClient(nameof(ChromaClient));
		services.AddSingleton(serviceProvider => new ChromaClient(options, CreateHttpClient(serviceProvider, nameof(ChromaClient))));
	}

	// A client and its options under a key, for an application that talks to more than one server, tenant or database.
	public static void AddKeyedChromaClient(this IServiceCollection services, object? serviceKey, Func<ChromaConfigurationOptions?, ChromaConfigurationOptions>? configurationOptions = null)
	{
		configurationOptions ??= DefaultConfigurationOptions;

		ChromaConfigurationOptions options = new();
		options = configurationOptions(options);

		var httpClientName = $"{nameof(ChromaClient)}:{serviceKey}";
		services.AddKeyedSingleton(serviceKey, options);
		services.AddHttpClient(httpClientName);
		services.AddKeyedSingleton(serviceKey, (serviceProvider, _) => new ChromaClient(options, CreateHttpClient(serviceProvider, httpClientName)));
	}

	// The client is a singleton and keeps its HttpClient, which sends each request with the current handler of the factory:
	// the factory renews it after its handler lifetime, two minutes by default, so a change of the server in the DNS is seen
	// on every target, also .NET Framework.
	private static HttpClient CreateHttpClient(IServiceProvider serviceProvider, string name)
		=> new(new CurrentHandler(serviceProvider.GetRequiredService<IHttpMessageHandlerFactory>(), name));

	private sealed class CurrentHandler(IHttpMessageHandlerFactory factory, string name) : HttpMessageHandler
	{
		// The factory owns the handler, so the invoker does not dispose it.
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			=> new HttpMessageInvoker(factory.CreateHandler(name), disposeHandler: false).SendAsync(request, cancellationToken);
	}

	private static ChromaConfigurationOptions DefaultConfigurationOptions(ChromaConfigurationOptions? options = null)
		=> options ?? new ChromaConfigurationOptions();
}