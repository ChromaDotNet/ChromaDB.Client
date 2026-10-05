using System.Net;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

// Older Chroma versions answer the endpoints they do not have with a bare 404 or 405: the message names the request.
[TestFixture]
public class MissingEndpointTests
{
	[TestCase(HttpStatusCode.NotFound, """{"detail":"Not Found"}""", "Not Found: POST /api/v2/tenants")]
	[TestCase(HttpStatusCode.MethodNotAllowed, """{"detail":"Method Not Allowed"}""", "Method Not Allowed: POST /api/v2/tenants")]
	[TestCase(HttpStatusCode.NotFound, "", "NotFound: POST /api/v2/tenants")]
	[TestCase(HttpStatusCode.NotFound, """{"error":"NotFoundError","message":"Tenant [t] not found"}""", "Tenant [t] not found")]
	[TestCase(HttpStatusCode.BadRequest, """{"detail":"Not Found"}""", "Not Found")]
	public async Task Message(HttpStatusCode statusCode, string body, string expected)
	{
		using var httpClient = new HttpClient(new FixedResponseHandler(statusCode, body));
		var client = new ChromaClient(new ChromaConfigurationOptions("http://localhost:8000"), httpClient);
		await Assert.ThatAsync(() => client.CreateTenantAsync("t"), Throws.InstanceOf<ChromaException>().With.Message.EqualTo(expected));
	}

	sealed class FixedResponseHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			=> Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(body) });
	}
}
