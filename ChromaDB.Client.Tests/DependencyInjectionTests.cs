using ChromaDB.Client.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

[TestFixture]
public class DependencyInjectionTests : ChromaTestsBase
{
	[Test]
	public async Task AddChromaClientUsesTheOptions()
	{
		var services = new ServiceCollection();
		services.AddChromaClient(_ => BaseConfigurationOptions);
		using var provider = services.BuildServiceProvider();
		var client = provider.GetRequiredService<ChromaClient>();
		var result = await client.Heartbeat();
		Assert.That(result.NanosecondHeartbeat, Is.GreaterThan(0));
	}

	[Test]
	public async Task AddKeyedChromaClientUsesTheOptionsOfTheKey()
	{
		var services = new ServiceCollection();
		services.AddKeyedChromaClient("first", _ => BaseConfigurationOptions);
		services.AddKeyedChromaClient("second", _ => BaseConfigurationOptions.WithUri("http://localhost:1/"));
		using var provider = services.BuildServiceProvider();
		Assert.That(provider.GetRequiredKeyedService<ChromaConfigurationOptions>("first"), Is.SameAs(BaseConfigurationOptions));
		Assert.That(provider.GetRequiredKeyedService<ChromaConfigurationOptions>("second").Uri, Is.EqualTo(new Uri("http://localhost:1/")));
		var result = await provider.GetRequiredKeyedService<ChromaClient>("first").Heartbeat();
		Assert.That(result.NanosecondHeartbeat, Is.GreaterThan(0));
		await Assert.ThatAsync(() => provider.GetRequiredKeyedService<ChromaClient>("second").Heartbeat(), Throws.InstanceOf<ChromaException>());
	}

	[Test]
	public void KeyedAndDefaultClientsTogether()
	{
		var services = new ServiceCollection();
		services.AddChromaClient(_ => BaseConfigurationOptions);
		services.AddKeyedChromaClient("other", _ => BaseConfigurationOptions.WithTenant("other_tenant"));
		using var provider = services.BuildServiceProvider();
		Assert.That(provider.GetRequiredService<ChromaConfigurationOptions>().Tenant, Is.EqualTo(BaseConfigurationOptions.Tenant));
		Assert.That(provider.GetRequiredKeyedService<ChromaConfigurationOptions>("other").Tenant, Is.EqualTo("other_tenant"));
		Assert.That(provider.GetRequiredService<ChromaClient>(), Is.Not.Null);
		Assert.That(provider.GetRequiredKeyedService<ChromaClient>("other"), Is.Not.Null);
	}
}
