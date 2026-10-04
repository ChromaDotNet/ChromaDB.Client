using System.Net;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

[TestFixture]
public class AuthenticationHeaderTests
{
	static readonly ChromaConfigurationOptions Options = new(uri: "http://localhost:8000/api/v2/");

	[Test]
	public async Task NoCredentials()
	{
		var request = (await Send(Options)).Single();
		Assert.That(request.ChromaToken, Is.Null);
		Assert.That(request.Authorization, Is.Null);
	}

	[Test]
	public async Task ChromaTokenInXChromaTokenHeader()
	{
		var request = (await Send(Options.WithChromaToken("token"))).Single();
		Assert.That(request.ChromaToken, Is.EqualTo("token"));
		Assert.That(request.Authorization, Is.Null);
	}

	[Test]
	public async Task ChromaTokenInAuthorizationHeader()
	{
		var request = (await Send(Options.WithChromaToken("token", ChromaTokenTransportHeader.Authorization))).Single();
		Assert.That(request.ChromaToken, Is.Null);
		Assert.That(request.Authorization, Is.EqualTo("Bearer token"));
	}

	[Test]
	public async Task BasicAuth()
	{
		var request = (await Send(Options.WithBasicAuth("admin", "secret"))).Single();
		Assert.That(request.ChromaToken, Is.Null);
		Assert.That(request.Authorization, Is.EqualTo("Basic YWRtaW46c2VjcmV0"));
	}

	[Test]
	public async Task CredentialsKeptByTheOtherOptions()
	{
		var basic = (await Send(Options.WithBasicAuth("admin", "secret").WithTenant("tenant").WithDatabase("database").WithUri("http://localhost:8001/api/v2/"))).Single();
		Assert.That(basic.Authorization, Is.EqualTo("Basic YWRtaW46c2VjcmV0"));
		var bearer = (await Send(Options.WithChromaToken("token", ChromaTokenTransportHeader.Authorization).WithTenant("tenant"))).Single();
		Assert.That(bearer.Authorization, Is.EqualTo("Bearer token"));
	}

	[Test]
	public async Task ClientsSharingHttpClientSendTheirOwnCredentials()
	{
		var handler = new RecordingHandler();
		using var httpClient = new HttpClient(handler);
		var first = new ChromaClient(Options.WithChromaToken("first"), httpClient);
		var second = new ChromaClient(Options.WithChromaToken("second", ChromaTokenTransportHeader.Authorization), httpClient);
		var third = new ChromaClient(Options, httpClient);
		await first.Heartbeat();
		await second.Heartbeat();
		await third.Heartbeat();
		await first.Heartbeat();
		Assert.That(handler.Requests.Select(x => x.ChromaToken), Is.EqualTo(new[] { "first", null, null, "first" }));
		Assert.That(handler.Requests.Select(x => x.Authorization), Is.EqualTo(new[] { null, "Bearer second", null, null }));
	}

	[Test]
	public void BasicAuthAndTokenInAuthorizationHeader()
	{
		var options = Options.WithBasicAuth("admin", "secret").WithChromaToken("token", ChromaTokenTransportHeader.Authorization);
		Assert.That(() => new ChromaClient(options, new HttpClient()), Throws.ArgumentException);
		Assert.That(() => new ChromaClient(options), Throws.ArgumentException);
	}

	static async Task<List<RecordedRequest>> Send(ChromaConfigurationOptions options)
	{
		var handler = new RecordingHandler();
		using var httpClient = new HttpClient(handler);
		await new ChromaClient(options, httpClient).Heartbeat();
		return handler.Requests;
	}

	record RecordedRequest(string? ChromaToken, string? Authorization);

	sealed class RecordingHandler : HttpMessageHandler
	{
		public List<RecordedRequest> Requests { get; } = [];

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Requests.Add(new(
				request.Headers.TryGetValues("X-Chroma-Token", out var values) ? values.Single() : null,
				request.Headers.Authorization?.ToString()));
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"nanosecond heartbeat":1}""") });
		}
	}
}
