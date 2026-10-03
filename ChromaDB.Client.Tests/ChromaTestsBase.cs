using ChromaDB.Client.Tests.TestContainer;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

public abstract class ChromaTestsBase
{
	protected static readonly HttpClient HttpClient = new();

	// CHROMA_TEST_TENANT and CHROMA_TEST_DATABASE run the tests in that tenant and database, created for each fixture, instead of the default ones.
	private static readonly string? TestTenant = Environment.GetEnvironmentVariable("CHROMA_TEST_TENANT") is { Length: > 0 } tenant ? tenant : null;
	private static readonly string? TestDatabase = Environment.GetEnvironmentVariable("CHROMA_TEST_DATABASE") is { Length: > 0 } database ? database : null;

	// CHROMA_TEST_API_VERSION=v1 runs the tests with the v1 API, the only one of Chroma 0.5.15 and earlier.
	protected static readonly ChromaApiVersion ApiVersion = Environment.GetEnvironmentVariable("CHROMA_TEST_API_VERSION") is "v1" ? ChromaApiVersion.V1 : ChromaApiVersion.V2;

	// Null when the fixture is skipped before its container starts.
	private ChromaDBContainer? _container;
	private ChromaConfigurationOptions? _baseConfigurationOptions;

	[OneTimeSetUp]
	public async Task OneTimeSetUp()
	{
		if (SkipReason is { } reason)
		{
			Assert.Ignore(reason);
		}
		if ((TestTenant is not null || TestDatabase is not null) && !TenantsSupported)
		{
			Assert.Ignore("Chroma 0.4.14 and earlier have no tenants and databases.");
		}
		if ((TestTenant is not null || TestDatabase is not null) && !RecordsInOtherTenantsSupported)
		{
			Assert.Ignore("Chroma 0.4.15 does not add records to the collections of other tenants and databases.");
		}
		_container = ConfigureContainer(new ChromaDBBuilder()).Build();
		await _container.StartAsync();
		_baseConfigurationOptions = new ChromaConfigurationOptions(uri: $"http://{_container.IpAddress}:{_container.GetMappedPublicPort(ChromaDBBuilder.ChromaDBPort)}/api/{(ApiVersion == ChromaApiVersion.V1 ? "v1" : "v2")}/")
			.WithApiVersion(ApiVersion);
		if (TestTenant is not null || TestDatabase is not null)
		{
			var client = new ChromaClient(WithServerCredentials(_baseConfigurationOptions), HttpClient);
			if (TestTenant is not null)
			{
				await client.CreateTenant(TestTenant);
				_baseConfigurationOptions = _baseConfigurationOptions.WithTenant(TestTenant);
			}
			// A new tenant has no databases: without CHROMA_TEST_DATABASE, the tests use a default_database created in it.
			var database = TestDatabase ?? (TestTenant is not null ? "default_database" : null);
			if (database is not null)
			{
				await client.CreateDatabase(database, tenant: TestTenant);
				_baseConfigurationOptions = _baseConfigurationOptions.WithDatabase(database);
			}
		}
	}

	[OneTimeTearDown]
	public async Task OneTimeTearDown()
	{
		_baseConfigurationOptions = null;
		if (_container is not null)
		{
			await _container.DisposeAsync();
		}
	}

	protected ChromaConfigurationOptions BaseConfigurationOptions => _baseConfigurationOptions ?? throw new InvalidOperationException();

	// Chroma 1.0 removed the built-in authentication and reads its settings from a configuration file.
	protected static bool IsChroma1 => ChromaDBBuilder.ChromaDBVersion.Major >= 1;

	// Chroma 0.4.14 has no tenants and databases, 0.4.15 has them.
	protected static bool TenantsSupported => ChromaDBBuilder.ChromaDBVersion >= new Version(0, 4, 15);

	// Missing in Chroma 0.4.15 and there in 0.4.23, the next version whose image starts: count_collections, the $not_contains
	// document filter, and the tenant and database in the collections the server returns.
	protected static bool CountCollectionsAndNotContainsSupported => ChromaDBBuilder.ChromaDBVersion >= new Version(0, 4, 23);

	// Chroma 0.4.15 creates collections in other tenants and databases, but answers that they do not exist when records
	// are added to them; 0.4.23 adds them.
	protected static bool RecordsInOtherTenantsSupported => ChromaDBBuilder.ChromaDBVersion >= new Version(0, 4, 23);

	// The servers before Chroma 0.5.1 configure their built-in authentication with other settings, and Chroma 1.0 removed it.
	protected static bool BuiltInAuthenticationTested => ChromaDBBuilder.ChromaDBVersion >= new Version(0, 5, 1) && !IsChroma1;

	// Since Chroma 0.5.20, the server rejects embeddings of different dimensions in the same request.
	protected static bool EmbeddingDimensionsChecked => ChromaDBBuilder.ChromaDBVersion >= new Version(0, 5, 20);

	// Since Chroma 1.0.16, add and upsert require embeddings.
	protected static bool EmbeddingsRequired => ChromaDBBuilder.ChromaDBVersion >= new Version(1, 0, 16);

	protected static List<ReadOnlyMemory<float>> Embeddings(int count)
		=> Enumerable.Repeat(new ReadOnlyMemory<float>([1f, 0.5f, 0f, -0.5f, -1f]), count).ToList();

	// Before Chroma 1.0.16 these calls succeed; since then the server rejects them.
	protected static async Task WithoutEmbeddings(Func<Task> action)
	{
		if (EmbeddingsRequired)
			await Assert.ThatAsync(() => action(), Throws.InstanceOf<ChromaException>());
		else
			await action();
	}

	protected virtual ChromaDBBuilder ConfigureContainer(ChromaDBBuilder builder) => builder;

	// Skips the whole fixture before its container starts, when the server of this version cannot run it.
	protected virtual string? SkipReason => null;

	// The credentials the server of the fixture requires, if any: the setup needs them to create the test tenant and database.
	protected virtual ChromaConfigurationOptions WithServerCredentials(ChromaConfigurationOptions options) => options;
}
