using ChromaDB.Client.Backup;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

// A library compiled for .NET Standard calls the init setters with the IsExternalInit of the netstandard2.0 build, the one of the
// assembly of the package; on .NET it gets the net10.0 build, whose setters have the one of the runtime. The name must lead to the
// type of the setters in both builds: the net10.0 builds forward it to the runtime.
[TestFixture]
public class TypeForwardingTests
{
	[TestCase(typeof(ChromaConfigurationOptions), nameof(ChromaConfigurationOptions.MaxResponseSize))]
	[TestCase(typeof(ChromaBackupOptions), nameof(ChromaBackupOptions.BatchSize))]
	public void IsExternalInitOfTheNetStandardBuildIsTheOneOfTheSetters(Type type, string property)
	{
		var named = Type.GetType($"System.Runtime.CompilerServices.IsExternalInit, {type.Assembly.GetName().Name}");
		Assert.That(named, Is.Not.Null);
		Assert.That(type.GetProperty(property)!.SetMethod!.ReturnParameter.GetRequiredCustomModifiers(), Does.Contain(named));
	}
}
