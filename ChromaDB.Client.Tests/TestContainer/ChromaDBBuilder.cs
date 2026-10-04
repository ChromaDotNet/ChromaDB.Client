using System.Text.RegularExpressions;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;

namespace ChromaDB.Client.Tests.TestContainer;

public class ChromaDBBuilder : ContainerBuilder<ChromaDBBuilder, ChromaDBContainer, ChromaDBConfiguration>
{
	public static readonly string ChromaDBImage = Environment.GetEnvironmentVariable("CHROMA_IMAGE") is { Length: > 0 } image ? image : "chromadb/chroma:0.6.3";
	public static readonly System.Version ChromaDBVersion = ParseVersion(ChromaDBImage);
	public const int ChromaDBPort = 8000;

	// "latest" is newer than any release; other tags start with the version, like "1.5.9" or "1.5.10.dev312".
	static System.Version ParseVersion(string image)
	{
		var tag = image.Substring(image.LastIndexOf(':') + 1);
		if (tag == "latest")
			return new System.Version(int.MaxValue, 0);
		var match = Regex.Match(tag, @"^\d+\.\d+(\.\d+)?");
		return match.Success
			? System.Version.Parse(match.Value)
			: throw new InvalidOperationException($"Cannot read the Chroma version from '{image}'.");
	}

	protected override ChromaDBConfiguration DockerResourceConfiguration { get; }

	public ChromaDBBuilder()
		: this(new ChromaDBConfiguration())
	{
		DockerResourceConfiguration = Init().DockerResourceConfiguration;
	}

	private ChromaDBBuilder(ChromaDBConfiguration dockerResourceConfiguration)
		: base(dockerResourceConfiguration)
	{
		DockerResourceConfiguration = dockerResourceConfiguration;
	}

	public override ChromaDBContainer Build()
	{
		Validate();
		return new ChromaDBContainer(DockerResourceConfiguration);
	}

	protected override ChromaDBBuilder Init()
	{
		return base.Init()
			.WithImage(ChromaDBImage)
			// A random host port: the tests reach the container on its own address, so several runs can share a Docker host.
			.WithPortBinding(ChromaDBPort, assignRandomHostPort: true)
			.WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(ChromaDBPort));
	}

	protected override ChromaDBBuilder Clone(IResourceConfiguration<CreateContainerParameters> resourceConfiguration)
	{
		return Merge(DockerResourceConfiguration, new ChromaDBConfiguration(resourceConfiguration));
	}

	protected override ChromaDBBuilder Merge(ChromaDBConfiguration oldValue, ChromaDBConfiguration newValue)
	{
		return new ChromaDBBuilder(new ChromaDBConfiguration(oldValue, newValue));
	}

	protected override ChromaDBBuilder Clone(IContainerConfiguration resourceConfiguration)
	{
		return Merge(DockerResourceConfiguration, new ChromaDBConfiguration(resourceConfiguration));
	}
}