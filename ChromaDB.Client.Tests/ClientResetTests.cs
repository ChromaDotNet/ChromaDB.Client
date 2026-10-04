using NUnit.Framework;
using Testcontainers.Chroma;

namespace ChromaDB.Client.Tests;

public class ClientResetTests
{
	[TestFixture]
	public class DefaultSettings : ChromaTestsBase
	{
		protected override string? SkipReason => RunningServer ? "The reset of a server already running is not tested: Chroma Cloud answers 403." : null;

		[Test]
		public async Task ResetSimple()
		{
			var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
			await Assert.ThatAsync(() => client.Reset(), Throws.InstanceOf<ChromaException>().With.Message.StartsWith(IsChroma1 ? "Reset is disabled by config" : "Resetting is not allowed by this configuration"));
		}
	}

	[TestFixture]
	public class AllowReset : ChromaTestsBase
	{
		protected override string? SkipReason => RunningServer ? "Allowing the reset needs a container started with that setting." : null;

		[Test]
		public async Task ResetSimple()
		{
			var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
			var result = await client.Reset();
			Assert.That(result, Is.True);
		}

		protected override ChromaBuilder ConfigureContainer(ChromaBuilder builder)
			=> IsChroma1
				? builder.WithEnvironment("CHROMA_ALLOW_RESET", "true")
				: builder.WithEnvironment("ALLOW_RESET", "TRUE");
	}
}