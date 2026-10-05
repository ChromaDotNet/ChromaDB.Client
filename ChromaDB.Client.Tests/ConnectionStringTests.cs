using NUnit.Framework;

namespace ChromaDB.Client.Tests;

// ChromaConfigurationOptions.FromConnectionString, for the connection strings of the settings of an application.
[TestFixture]
public class ConnectionStringTests
{
	[Test]
	public void ChromaCloud()
	{
		var options = ChromaConfigurationOptions.FromConnectionString("Endpoint=https://api.trychroma.com;Token=ck-123;Tenant=t1;Database=d1");
		Assert.That((options.Uri, options.ChromaToken, options.ChromaTokenTransportHeader, options.Tenant, options.Database),
			Is.EqualTo((new Uri("https://api.trychroma.com"), "ck-123", ChromaTokenTransportHeader.XChromaToken, "t1", "d1")));
	}

	// Just a URI, as a connection string of a container; or the endpoint alone.
	[TestCase("http://localhost:8000")]
	[TestCase("Endpoint=http://localhost:8000")]
	[TestCase("endpoint=http://localhost:8000;token=;TENANT=")]
	public void EndpointAlone(string connectionString)
	{
		var options = ChromaConfigurationOptions.FromConnectionString(connectionString);
		Assert.That((options.Uri, options.ChromaToken, options.Tenant, options.Database), Is.EqualTo((new Uri("http://localhost:8000"), (string?)null, (string?)null, (string?)null)));
	}

	// The other options keep their defaults.
	[Test]
	public void DefaultsOfTheOtherOptions()
	{
		var options = ChromaConfigurationOptions.FromConnectionString("Endpoint=http://localhost:8000");
		var defaults = new ChromaConfigurationOptions();
		Assert.That((options.MetadataValues, options.BatchSplitting, options.ApiVersion), Is.EqualTo((defaults.MetadataValues, defaults.BatchSplitting, defaults.ApiVersion)));
	}

	[Test]
	public void ValueInQuotes()
	{
		Assert.That(ChromaConfigurationOptions.FromConnectionString("Endpoint=http://localhost:8000;Token=\"a;b\"").ChromaToken, Is.EqualTo("a;b"));
	}

	[TestCase("Token=ck-123")]
	[TestCase("Endpoint=;Token=ck-123")]
	[TestCase("Endpoint=localhost")]
	[TestCase("Endpoint=http://localhost:8000;ApiKey=ck-123")]
	public void Rejected(string connectionString)
	{
		Assert.That(() => ChromaConfigurationOptions.FromConnectionString(connectionString), Throws.ArgumentException);
	}
}
