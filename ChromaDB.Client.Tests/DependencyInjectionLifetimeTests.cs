using ChromaDB.Client.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

// The registrations, without a server: one ChromaClient per container, also for the services that live as long as it.
[TestFixture]
public class DependencyInjectionLifetimeTests
{
	static readonly ChromaConfigurationOptions Options = new("http://localhost:8000");

	[Test]
	public void AddChromaClientRegistersOneSingleton()
	{
		var services = new ServiceCollection();
		services.AddChromaClient(_ => Options);
		Assert.That(services.Where(x => x.ServiceType == typeof(ChromaClient)).Select(x => x.Lifetime), Is.EqualTo(new[] { ServiceLifetime.Singleton }));
		using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
		var fromRoot = provider.GetRequiredService<ChromaClient>();
		using var scope = provider.CreateScope();
		Assert.That(scope.ServiceProvider.GetRequiredService<ChromaClient>(), Is.SameAs(fromRoot));
		Assert.That(scope.ServiceProvider.GetRequiredService<ChromaClient>(), Is.SameAs(fromRoot));
		Assert.That(fromRoot.Options, Is.SameAs(Options));
	}

	[Test]
	public void AddKeyedChromaClientRegistersOneSingleton()
	{
		var services = new ServiceCollection();
		services.AddKeyedChromaClient("first", _ => Options);
		services.AddKeyedChromaClient("second", _ => Options.WithTenant("t"));
		Assert.That(services.Where(x => x.ServiceType == typeof(ChromaClient)).Select(x => x.Lifetime), Is.All.EqualTo(ServiceLifetime.Singleton));
		using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
		var first = provider.GetRequiredKeyedService<ChromaClient>("first");
		using var scope = provider.CreateScope();
		Assert.That(scope.ServiceProvider.GetRequiredKeyedService<ChromaClient>("first"), Is.SameAs(first));
		Assert.That(provider.GetRequiredKeyedService<ChromaClient>("second"), Is.Not.SameAs(first));
		Assert.That(provider.GetRequiredKeyedService<ChromaClient>("second").Options.Tenant, Is.EqualTo("t"));
	}

	// A singleton service can take the ChromaClient, as a vector store does.
	[Test]
	public void SingletonsCanTakeTheClient()
	{
		var services = new ServiceCollection();
		services.AddChromaClient(_ => Options);
		services.AddKeyedChromaClient("k", _ => Options);
		services.AddSingleton<Consumer>();
		services.AddKeyedSingleton<Consumer>("k", (sp, key) => new Consumer(sp.GetRequiredKeyedService<ChromaClient>(key)));
		using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
		Assert.That(provider.GetRequiredService<Consumer>().Client, Is.SameAs(provider.GetRequiredService<ChromaClient>()));
		Assert.That(provider.GetRequiredKeyedService<Consumer>("k").Client, Is.SameAs(provider.GetRequiredKeyedService<ChromaClient>("k")));
	}

	// The singleton keeps its HttpClient, but each request takes the current handler of the factory, which renews it.
	[Test]
	public async Task RequestsUseTheRenewedHandler()
	{
		var handlers = new List<int>();
		var created = 0;
		var services = new ServiceCollection();
		services.AddChromaClient(_ => Options);
		services.AddHttpClient(nameof(ChromaClient))
			.ConfigurePrimaryHttpMessageHandler(() => new HeartbeatHandler(Interlocked.Increment(ref created), handlers))
			.SetHandlerLifetime(TimeSpan.FromSeconds(1));
		using var provider = services.BuildServiceProvider();
		var client = provider.GetRequiredService<ChromaClient>();
		await client.HeartbeatAsync();
		await client.HeartbeatAsync();
		await Task.Delay(TimeSpan.FromSeconds(2));
		await client.HeartbeatAsync();
		Assert.That(handlers, Is.EqualTo(new[] { 1, 1, 2 }));
	}

	// A CreateChromaClient for an HttpClient name of its own, as an integration keeps it: the handler of that name, renewed by the
	// factory, and the settings of that HttpClient.
	[Test]
	public async Task CreateChromaClientUsesTheHandlerOfTheName()
	{
		var handlers = new List<int>();
		var headers = new List<string?>();
		var created = 0;
		var services = new ServiceCollection();
		services.AddHttpClient("connection")
			.ConfigurePrimaryHttpMessageHandler(() => new HeartbeatHandler(Interlocked.Increment(ref created), handlers, headers))
			.ConfigureHttpClient(httpClient => httpClient.DefaultRequestHeaders.Add("X-Connection", "connection"))
			.SetHandlerLifetime(TimeSpan.FromSeconds(1));
		using var provider = services.BuildServiceProvider();
		var client = provider.CreateChromaClient(Options, "connection");
		await client.HeartbeatAsync();
		await Task.Delay(TimeSpan.FromSeconds(2));
		await client.HeartbeatAsync();
		Assert.That(handlers, Is.EqualTo(new[] { 1, 2 }));
		Assert.That(headers, Is.EqualTo(new[] { "connection", "connection" }));
		Assert.That(client.Options, Is.SameAs(Options));
	}

	sealed class HeartbeatHandler(int id, List<int> handlers, List<string?>? headers = null) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			lock (handlers) handlers.Add(id);
			headers?.Add(request.Headers.TryGetValues("X-Connection", out var values) ? values.Single() : null);
			return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"nanosecond heartbeat\":1}") });
		}
	}

	sealed class Consumer(ChromaClient client)
	{
		public ChromaClient Client { get; } = client;
	}
}
