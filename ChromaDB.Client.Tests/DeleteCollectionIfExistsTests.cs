using System.Net;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

// A missing collection, in the answer of each kind of server, is not an error; any other error is.
[TestFixture]
public class DeleteCollectionIfExistsTests
{
	[TestCase(HttpStatusCode.OK, "{}", true)]
	[TestCase(HttpStatusCode.NotFound, """{"error":"NotFoundError","message":"Collection [c] does not exist"}""", false)]
	[TestCase(HttpStatusCode.BadRequest, """{"error":"InvalidCollection","message":"Collection c does not exist."}""", false)]
	[TestCase(HttpStatusCode.InternalServerError, """{"detail":"Collection c does not exist."}""", false)]
	public async Task TellsWhetherItDeleted(HttpStatusCode status, string body, bool deleted)
		=> Assert.That(await Client(status, body).DeleteCollectionIfExistsAsync("c"), Is.EqualTo(deleted));

	// A bare 404 comes also from a wrong address.
	[TestCase(HttpStatusCode.NotFound, "")]
	[TestCase(HttpStatusCode.InternalServerError, """{"error":"InternalError","message":"Error executing plan"}""")]
	public async Task OtherErrorsAreThrown(HttpStatusCode status, string body)
		=> await Assert.ThatAsync(() => Client(status, body).DeleteCollectionIfExistsAsync("c"), Throws.InstanceOf<ChromaException>().With.Property(nameof(ChromaException.StatusCode)).EqualTo(status));

	// A 0.x version, so that the delete is the only other request.
	static ChromaClient Client(HttpStatusCode status, string body)
		=> new(new ChromaConfigurationOptions("http://localhost:8000"), new HttpClient(new FakeServer(status, body)));

	sealed class FakeServer(HttpStatusCode status, string body) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			=> Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/version")
				? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("\"0.6.3\"") }
				: new HttpResponseMessage(status) { Content = new StringContent(body) });
	}
}
