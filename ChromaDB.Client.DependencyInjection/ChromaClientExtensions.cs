using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace ChromaDB.Client.DependencyInjection;

/// <summary>
/// The registration of the <c>ChromaClient</c> for <c>Microsoft.Extensions.DependencyInjection</c>. The client is a singleton
/// and sends each request with the current handler of <c>IHttpClientFactory</c>, which the factory renews after its handler
/// lifetime, two minutes by default.
/// </summary>
public static class ChromaClientExtensions
{
	/// <summary>
	/// Registers a <c>ChromaClient</c> and its options as singletons. <c>configurationOptions</c> takes the default options,
	/// for <c>http://localhost:8000</c>, and returns the options of the client. Returns the services, for more registrations.
	/// </summary>
	/// <param name="services">The services.</param>
	/// <param name="configurationOptions">Takes the default options, for <c>http://localhost:8000</c>, and returns the options of the client; null for the default ones.</param>
	/// <returns>The services, for more registrations.</returns>
	public static IServiceCollection AddChromaClient(this IServiceCollection services, Func<ChromaConfigurationOptions?, ChromaConfigurationOptions>? configurationOptions = null)
		=> AddChromaClient(services, configurationOptions, _ => { });

	/// <summary>
	/// Registers a <c>ChromaClient</c> and its options as singletons. <c>configureHttpClient</c> configures the <c>HttpClient</c>
	/// of the client, like a resilience handler, a proxy or a timeout. Returns the services, for more registrations.
	/// </summary>
	/// <param name="services">The services.</param>
	/// <param name="configurationOptions">Takes the default options, for <c>http://localhost:8000</c>, and returns the options of the client; null for the default ones.</param>
	/// <param name="configureHttpClient">Configures the <c>HttpClient</c> of the client, like a resilience handler, a proxy or a timeout.</param>
	/// <returns>The services, for more registrations.</returns>
	public static IServiceCollection AddChromaClient(this IServiceCollection services, Func<ChromaConfigurationOptions?, ChromaConfigurationOptions>? configurationOptions, Action<IHttpClientBuilder> configureHttpClient)
	{
		configurationOptions ??= DefaultConfigurationOptions;

		ChromaConfigurationOptions options = new();
		options = configurationOptions(options);

		services.AddSingleton(options);
		configureHttpClient(services.AddHttpClient(nameof(ChromaClient)));
		services.AddSingleton(serviceProvider => serviceProvider.CreateChromaClient(options, nameof(ChromaClient)));
		return services;
	}

	/// <summary>
	/// A client and its options under a key, for an application that talks to more than one server, tenant or database.
	/// Both are registered as singletons. Returns the services, for more registrations.
	/// </summary>
	/// <param name="services">The services.</param>
	/// <param name="serviceKey">The key of the client and of its options.</param>
	/// <param name="configurationOptions">Takes the default options, for <c>http://localhost:8000</c>, and returns the options of the client; null for the default ones.</param>
	/// <returns>The services, for more registrations.</returns>
	public static IServiceCollection AddKeyedChromaClient(this IServiceCollection services, object? serviceKey, Func<ChromaConfigurationOptions?, ChromaConfigurationOptions>? configurationOptions = null)
		=> AddKeyedChromaClient(services, serviceKey, configurationOptions, _ => { });

	/// <summary>
	/// Registers a <c>ChromaClient</c> and its options as singletons under <c>serviceKey</c>. <c>configureHttpClient</c>
	/// configures the <c>HttpClient</c> of the client, like a resilience handler, a proxy or a timeout. Returns the services, for
	/// more registrations.
	/// </summary>
	/// <param name="services">The services.</param>
	/// <param name="serviceKey">The key of the client and of its options.</param>
	/// <param name="configurationOptions">Takes the default options, for <c>http://localhost:8000</c>, and returns the options of the client; null for the default ones.</param>
	/// <param name="configureHttpClient">Configures the <c>HttpClient</c> of the client, like a resilience handler, a proxy or a timeout.</param>
	/// <returns>The services, for more registrations.</returns>
	public static IServiceCollection AddKeyedChromaClient(this IServiceCollection services, object? serviceKey, Func<ChromaConfigurationOptions?, ChromaConfigurationOptions>? configurationOptions, Action<IHttpClientBuilder> configureHttpClient)
	{
		// A null key would register the client without a key, next to the one of AddChromaClient.
		if (serviceKey is null)
		{
			throw new ArgumentNullException(nameof(serviceKey));
		}
		configurationOptions ??= DefaultConfigurationOptions;

		ChromaConfigurationOptions options = new();
		options = configurationOptions(options);

		// The type with the text, so that keys like 1 and "1" get an HttpClient each.
		var httpClientName = $"{nameof(ChromaClient)}:{serviceKey.GetType().FullName}:{serviceKey}";
		services.AddKeyedSingleton(serviceKey, options);
		configureHttpClient(services.AddHttpClient(httpClientName));
		services.AddKeyedSingleton(serviceKey, (serviceProvider, _) => serviceProvider.CreateChromaClient(options, httpClientName));
		return services;
	}

	/// <summary>
	/// A <c>ChromaClient</c> to keep, like a singleton of an application or of an integration, that sends each request with the
	/// current handler of <c>IHttpClientFactory</c> for <c>httpClientName</c>: the factory renews it after its handler lifetime, two
	/// minutes by default, so a change of the address of the server in the DNS is seen. The settings of that <c>HttpClient</c>, like
	/// its timeout, are applied too. <c>AddChromaClient</c> and <c>AddKeyedChromaClient</c> create their clients with it.
	/// </summary>
	/// <param name="services">The services.</param>
	/// <param name="options">The options of the client.</param>
	/// <param name="httpClientName">The name of the <c>HttpClient</c> of <c>IHttpClientFactory</c> that sends the requests.</param>
	/// <returns>The client.</returns>
	public static ChromaClient CreateChromaClient(this IServiceProvider services, ChromaConfigurationOptions options, string httpClientName)
		=> new(options, CreateHttpClient(services, httpClientName));

	// The client keeps its HttpClient, which sends each request with the current handler of the factory, on every target, also
	// .NET Framework. The settings of the HttpClient itself, like ConfigureHttpClient, are applied here.
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
