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
		AddHttpClient(services, nameof(ChromaClient), options);
		services.AddSingleton(serviceProvider => new ChromaClient(options, serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(ChromaClient))));
	}

	// A client and its options under a key, for an application that talks to more than one server, tenant or database.
	public static void AddKeyedChromaClient(this IServiceCollection services, object? serviceKey, Func<ChromaConfigurationOptions?, ChromaConfigurationOptions>? configurationOptions = null)
	{
		configurationOptions ??= DefaultConfigurationOptions;

		ChromaConfigurationOptions options = new();
		options = configurationOptions(options);

		var httpClientName = $"{nameof(ChromaClient)}:{serviceKey}";
		services.AddKeyedSingleton(serviceKey, options);
		AddHttpClient(services, httpClientName, options);
		services.AddKeyedSingleton(serviceKey, (serviceProvider, _) => new ChromaClient(options, serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(httpClientName)));
	}

	// The client is a singleton, so it keeps its HttpClient: on .NET the connections are renewed every few minutes,
	// so that a change of the address of the server in the DNS is seen.
	private static void AddHttpClient(IServiceCollection services, string name, ChromaConfigurationOptions options)
	{
		var builder = services.AddHttpClient(name, o =>
		{
			o.BaseAddress = options.Uri;
		});
#if NET
		builder.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) });
#endif
	}

	private static ChromaConfigurationOptions DefaultConfigurationOptions(ChromaConfigurationOptions? options = null)
		=> options ?? new ChromaConfigurationOptions();
}