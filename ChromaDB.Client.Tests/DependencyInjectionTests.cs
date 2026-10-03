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
}
