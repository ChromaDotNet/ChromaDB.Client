using NUnit.Framework;

namespace ChromaDB.Client.Tests;

// The HttpClient of a client created without one renews its connections every two minutes, as the handlers of IHttpClientFactory, so
// that the client follows a change of the address of the server: on .NET through SocketsHttpHandler, also with the netstandard2.0 build,
// which creates it through reflection. .NET Framework has no SocketsHttpHandler and keeps the HttpClientHandler.
[TestFixture]
public class DefaultHttpClientTests
{
	[Test]
	public void HandlerOfTheDefaultHttpClient()
	{
		using var handler = ChromaClient.NewHttpMessageHandler();
#if NETFRAMEWORK
		Assert.That(handler, Is.TypeOf<HttpClientHandler>());
#else
		Assert.That(handler, Is.TypeOf<SocketsHttpHandler>());
		Assert.That(((SocketsHttpHandler)handler).PooledConnectionLifetime, Is.EqualTo(TimeSpan.FromMinutes(2)));
#endif
	}
}
