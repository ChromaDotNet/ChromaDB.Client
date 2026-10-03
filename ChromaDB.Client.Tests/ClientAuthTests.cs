using ChromaDB.Client.Tests.TestContainer;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

public class ClientAuthTests
{
	[TestFixture]
	public class NoAuth : ChromaTestsBase
	{
		[Test]
		public async Task Success()
		{
			var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
			await Assert.ThatAsync(() => client.CreateCollection($"collection{Random.Shared.Next()}"), Throws.Nothing);
		}
	}

	[TestFixture]
	public class ChromaTokenAuth : ChromaTestsBase
	{
		protected override string? SkipReason => BuiltInAuthenticationTested ? null : "Chroma 1.0 removed the built-in authentication, and the servers before 0.5.1 configure it with other settings.";

		[Test]
		public async Task Success()
		{
			var client = new ChromaClient(BaseConfigurationOptions.WithChromaToken("random-ToKen"), HttpClient);
			await Assert.ThatAsync(() => client.CreateCollection($"collection{Random.Shared.Next()}"), Throws.Nothing);
		}

		[Test]
		public async Task NoToken()
		{
			var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
			await Assert.ThatAsync(() => client.CreateCollection($"collection{Random.Shared.Next()}"), Throws.InstanceOf<ChromaException>().With.Message.Contains("Forbidden"));
		}

		[Test]
		public async Task WrongToken()
		{
			var client = new ChromaClient(BaseConfigurationOptions.WithChromaToken("wrong"), HttpClient);
			await Assert.ThatAsync(() => client.CreateCollection($"collection{Random.Shared.Next()}"), Throws.InstanceOf<ChromaException>().With.Message.Contains("Forbidden"));
		}

		[Test]
		public async Task WrongTokenCasing()
		{
			var client = new ChromaClient(BaseConfigurationOptions.WithChromaToken("random-token"), HttpClient);
			await Assert.ThatAsync(() => client.CreateCollection($"collection{Random.Shared.Next()}"), Throws.InstanceOf<ChromaException>().With.Message.Contains("Forbidden"));
		}

		protected override ChromaConfigurationOptions WithServerCredentials(ChromaConfigurationOptions options)
			=> options.WithChromaToken("random-ToKen");

		protected override ChromaDBBuilder ConfigureContainer(ChromaDBBuilder builder)
			=> builder
				.WithEnvironment("CHROMA_SERVER_AUTHN_CREDENTIALS", "random-ToKen")
				.WithEnvironment("CHROMA_SERVER_AUTHN_PROVIDER", "chromadb.auth.token_authn.TokenAuthenticationServerProvider")
				.WithEnvironment("CHROMA_AUTH_TOKEN_TRANSPORT_HEADER", "X-Chroma-Token");
	}

	// Without CHROMA_AUTH_TOKEN_TRANSPORT_HEADER the server reads the token from Authorization: Bearer.
	[TestFixture]
	public class ChromaTokenInAuthorizationHeader : ChromaTestsBase
	{
		protected override string? SkipReason => BuiltInAuthenticationTested ? null : "Chroma 1.0 removed the built-in authentication, and the servers before 0.5.1 configure it with other settings.";

		[Test]
		public async Task Success()
		{
			var client = new ChromaClient(BaseConfigurationOptions.WithChromaToken("random-ToKen", ChromaTokenTransportHeader.Authorization), HttpClient);
			await Assert.ThatAsync(() => client.CreateCollection($"collection{Random.Shared.Next()}"), Throws.Nothing);
		}

		[Test]
		public async Task NoToken()
		{
			var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
			await Assert.ThatAsync(() => client.CreateCollection($"collection{Random.Shared.Next()}"), Throws.InstanceOf<ChromaException>().With.Message.Contains("Forbidden"));
		}

		[Test]
		public async Task TokenInXChromaTokenHeader()
		{
			var client = new ChromaClient(BaseConfigurationOptions.WithChromaToken("random-ToKen"), HttpClient);
			await Assert.ThatAsync(() => client.CreateCollection($"collection{Random.Shared.Next()}"), Throws.InstanceOf<ChromaException>().With.Message.Contains("Forbidden"));
		}

		protected override ChromaConfigurationOptions WithServerCredentials(ChromaConfigurationOptions options)
			=> options.WithChromaToken("random-ToKen", ChromaTokenTransportHeader.Authorization);

		protected override ChromaDBBuilder ConfigureContainer(ChromaDBBuilder builder)
			=> builder
				.WithEnvironment("CHROMA_SERVER_AUTHN_CREDENTIALS", "random-ToKen")
				.WithEnvironment("CHROMA_SERVER_AUTHN_PROVIDER", "chromadb.auth.token_authn.TokenAuthenticationServerProvider");
	}

	[TestFixture]
	public class BasicAuth : ChromaTestsBase
	{
		// The htpasswd line of the user "admin" with the password "secret", hashed with bcrypt.
		const string Credentials = "admin:$2b$12$XkWBowOjcQqb09GdfnT0CurP1VJVZwbmcfkIeXQtkzZqmVQKrOug2";

		protected override string? SkipReason => BuiltInAuthenticationTested ? null : "Chroma 1.0 removed the built-in authentication, and the servers before 0.5.1 configure it with other settings.";

		[Test]
		public async Task Success()
		{
			var client = new ChromaClient(BaseConfigurationOptions.WithBasicAuth("admin", "secret"), HttpClient);
			await Assert.ThatAsync(() => client.CreateCollection($"collection{Random.Shared.Next()}"), Throws.Nothing);
		}

		[Test]
		public async Task NoCredentials()
		{
			var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
			await Assert.ThatAsync(() => client.CreateCollection($"collection{Random.Shared.Next()}"), Throws.InstanceOf<ChromaException>().With.Message.Contains("Forbidden"));
		}

		[Test]
		public async Task WrongPassword()
		{
			var client = new ChromaClient(BaseConfigurationOptions.WithBasicAuth("admin", "wrong"), HttpClient);
			await Assert.ThatAsync(() => client.CreateCollection($"collection{Random.Shared.Next()}"), Throws.InstanceOf<ChromaException>().With.Message.Contains("Forbidden"));
		}

		protected override ChromaConfigurationOptions WithServerCredentials(ChromaConfigurationOptions options)
			=> options.WithBasicAuth("admin", "secret");

		protected override ChromaDBBuilder ConfigureContainer(ChromaDBBuilder builder)
			=> builder
				.WithEnvironment("CHROMA_SERVER_AUTHN_CREDENTIALS", Credentials)
				.WithEnvironment("CHROMA_SERVER_AUTHN_PROVIDER", "chromadb.auth.basic_authn.BasicAuthenticationServerProvider");
	}
}
