using System.Net;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

[TestFixture]
public class ConfigurationUriTests
{
	[TestCase("http://localhost:8000/api/v2/", "http://localhost:8000/api/v2/heartbeat")]
	[TestCase("http://localhost:8000/api/v2", "http://localhost:8000/api/v2/heartbeat")]
	[TestCase("http://localhost:8000", "http://localhost:8000/api/v2/heartbeat")]
	[TestCase("http://localhost:8000/", "http://localhost:8000/api/v2/heartbeat")]
	[TestCase("http://localhost:8000/chroma/api/v2", "http://localhost:8000/chroma/api/v2/heartbeat")]
	[TestCase("http://localhost:8000/my%20chroma/api/v2", "http://localhost:8000/my%20chroma/api/v2/heartbeat")]
	[TestCase("https://chroma.example.com", "https://chroma.example.com/api/v2/heartbeat")]
	public async Task RequestUri(string uri, string expected)
	{
		var handler = new RecordingHandler();
		using var httpClient = new HttpClient(handler);
		var client = new ChromaClient(new ChromaConfigurationOptions(uri: uri), httpClient);
		await client.Heartbeat();
		Assert.That(handler.RequestUri?.AbsoluteUri, Is.EqualTo(expected));
	}

	sealed class RecordingHandler : HttpMessageHandler
	{
		public Uri? RequestUri { get; private set; }

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			RequestUri = request.RequestUri;
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"nanosecond heartbeat":1}""") });
		}
	}
}
