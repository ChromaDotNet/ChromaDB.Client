using ChromaDB.Client.Tests.TestContainer;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

public class ClientResetTests
{
	[TestFixture]
	public class DefaultSettings : ChromaTestsBase
	{
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
		[Test]
		public async Task ResetSimple()
		{
			var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
			var result = await client.Reset();
			Assert.That(result, Is.True);
		}

		protected override ChromaDBBuilder ConfigureContainer(ChromaDBBuilder builder)
			=> IsChroma1
				? builder.WithEnvironment("CHROMA_ALLOW_RESET", "true")
				: builder.WithEnvironment("ALLOW_RESET", "TRUE");
	}
}