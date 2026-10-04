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

	sealed class Consumer(ChromaClient client)
	{
		public ChromaClient Client { get; } = client;
	}
}
