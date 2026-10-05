using System.Net;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

// What a ChromaException wraps: the failures of the request, and nothing else.
[TestFixture]
public class ExceptionTests
{
	[Test]
	public void NetworkErrorIsAChromaException()
	{
		var client = Client(_ => throw new HttpRequestException("Connection refused"));
		Assert.That(() => client.HeartbeatAsync(), Throws.InstanceOf<ChromaException>().With.InnerException.InstanceOf<HttpRequestException>());
	}

	[Test]
	public void AnswerThatIsNotJsonIsAChromaException()
	{
		var client = Client(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>") });
		Assert.That(() => client.HeartbeatAsync(), Throws.InstanceOf<ChromaException>().With.InnerException.InstanceOf<System.Text.Json.JsonException>());
	}

	// A timeout is a cancellation the caller did not ask for.
	[Test]
	public void TimeoutIsAChromaException()
	{
		var client = Client(_ => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"));
		Assert.That(() => client.HeartbeatAsync(), Throws.InstanceOf<ChromaException>().With.InnerException.InstanceOf<TaskCanceledException>());
	}

	// Not about Chroma, like an assembly that does not load: it goes as it is.
	[Test]
	public void OtherExceptionsGoAsTheyAre()
	{
		var client = Client(_ => throw new FileLoadException("Could not load file or assembly 'System.Numerics.Vectors'"));
		Assert.That(() => client.HeartbeatAsync(), Throws.InstanceOf<FileLoadException>());
	}

	static ChromaClient Client(Func<HttpRequestMessage, HttpResponseMessage> answer)
		=> new(new ChromaConfigurationOptions("http://localhost:8000"), new HttpClient(new Handler(answer)));

	sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			=> Task.FromResult(answer(request));
	}
}
