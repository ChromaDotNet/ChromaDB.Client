using NUnit.Framework;

namespace ChromaDB.Client.Tests;

// Names that the path of a URL drops: the request would go to the resource above, GetCollectionAsync("..") to the
// database. The client stops before sending it.
[TestFixture]
public class PathNamesTests
{
	[TestCase("..")]
	[TestCase(".")]
	[TestCase("")]
	public void NamesThePathDrops(string name)
	{
		using var client = new ChromaClient(new ChromaConfigurationOptions("http://localhost:8000"), new HttpClient(new NoRequests()));
		Assert.That(() => client.GetCollectionAsync(name), Throws.ArgumentException);
		Assert.That(() => client.CollectionExistsAsync(name), Throws.ArgumentException);
		Assert.That(() => client.DeleteCollectionAsync(name), Throws.ArgumentException);
		Assert.That(() => client.GetDatabaseAsync(name), Throws.ArgumentException);
		if (name != "")
		{
			// An empty database or tenant of a collection is the one of the options.
			Assert.That(() => client.GetCollectionAsync("c", database: name), Throws.ArgumentException);
			Assert.That(() => client.GetCollectionAsync("c", tenant: name), Throws.ArgumentException);
		}
		Assert.That(() => client.GetTenantAsync(name), Throws.ArgumentException);
	}

	// Dots inside a name stay: "a.b" and "..x" are names like the others.
	[TestCase("a.b")]
	[TestCase("..x")]
	public void DotsInAName(string name)
	{
		var handler = new NoRequests();
		using var client = new ChromaClient(new ChromaConfigurationOptions("http://localhost:8000"), new HttpClient(handler));
		Assert.That(() => client.GetCollectionAsync(name), Throws.Exception.With.Message.Contains("/collections/" + name));
	}

	sealed class NoRequests : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			=> throw new InvalidOperationException("Request to " + request.RequestUri!.AbsolutePath);
	}
}
