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
		services.AddScoped<ChromaClient>();
		services.AddHttpClient<ChromaClient>(o =>
		{
			o.BaseAddress = options.Uri;
		});
	}

	// A client and its options under a key, for an application that talks to more than one server, tenant or database.
	public static void AddKeyedChromaClient(this IServiceCollection services, object? serviceKey, Func<ChromaConfigurationOptions?, ChromaConfigurationOptions>? configurationOptions = null)
	{
		configurationOptions ??= DefaultConfigurationOptions;

		ChromaConfigurationOptions options = new();
		options = configurationOptions(options);

		var httpClientName = $"{nameof(ChromaClient)}:{serviceKey}";
		services.AddKeyedSingleton(serviceKey, options);
		services.AddHttpClient(httpClientName, o =>
		{
			o.BaseAddress = options.Uri;
		});
		services.AddKeyedScoped(serviceKey, (serviceProvider, _) => new ChromaClient(options, serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(httpClientName)));
	}

	private static ChromaConfigurationOptions DefaultConfigurationOptions(ChromaConfigurationOptions? options = null)
		=> options ?? new ChromaConfigurationOptions();
}