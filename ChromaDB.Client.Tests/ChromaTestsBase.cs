using ChromaDB.Client.Tests.TestContainer;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

public abstract class ChromaTestsBase
{
	protected static readonly HttpClient HttpClient = new();

	private ChromaDBContainer _container;
	private ChromaConfigurationOptions? _baseConfigurationOptions;

	[OneTimeSetUp]
	public async Task OneTimeSetUp()
	{
		_container = ConfigureContainer(new ChromaDBBuilder()).Build();
		await _container.StartAsync();
		_baseConfigurationOptions = new ChromaConfigurationOptions(uri: $"http://{_container.IpAddress}:{_container.GetMappedPublicPort(ChromaDBBuilder.ChromaDBPort)}/api/v2/");
	}

	[OneTimeTearDown]
	public async Task OneTimeTearDown()
	{
		_baseConfigurationOptions = null;
		await _container.DisposeAsync();
	}

	protected ChromaConfigurationOptions BaseConfigurationOptions => _baseConfigurationOptions ?? throw new InvalidOperationException();

	// Chroma 1.0 removed the built-in authentication and reads its settings from a configuration file.
	protected static bool IsChroma1 => ChromaDBBuilder.ChromaDBVersion.Major >= 1;

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
}
