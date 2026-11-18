using System.Net;
using System.Net.Sockets;
using System.Text;
using ChromaDB.Client.Common;
using ChromaDB.Client.DependencyInjection;
using ChromaDB.Client.Models.Requests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

// What the client does with a server that answers in a way it should not: a redirect to another server, an error with a huge body,
// an answer larger than the client takes. Servers of HttpListener on free ports of 127.0.0.1 and 127.0.0.2.
[TestFixture]
public class HostileServerTests
{
	public enum ClientKind
	{
		Own,
		DependencyInjection,
	}

	[Test]
	public async Task TokenNotSentToTheHostOfARedirect([Values] ChromaTokenTransportHeader header, [Values] ClientKind kind)
	{
		using var other = Listen("127.0.0.2", out var otherPort);
		var seen = new List<string>();
		_ = Serve(other, async c =>
		{
			lock (seen) seen.Add(c.Request.Headers["X-Chroma-Token"] ?? c.Request.Headers["Authorization"] ?? "");
			await Answer(c, 200, "\"1.0.0\"");
		});
		using var chroma = Listen("127.0.0.1", out var port);
		_ = Serve(chroma, c => Redirect(c, 307, $"http://127.0.0.2:{otherPort}{c.Request.Url!.AbsolutePath}"));
		var options = new ChromaConfigurationOptions($"http://127.0.0.1:{port}/").WithChromaToken("secret-token", header);
		using var provider = kind == ClientKind.DependencyInjection ? new ServiceCollection().AddChromaClient(_ => options).BuildServiceProvider() : null;
		using var client = provider?.GetRequiredService<ChromaClient>() ?? new ChromaClient(options);
		var ex = Assert.ThrowsAsync<ChromaException>(() => client.GetVersionAsync());
		Assert.That(ex!.Message, Does.Contain("redirected the request to http://127.0.0.2:"));
		Assert.That(ex.Message, Does.Not.Contain("secret-token"));
		await Task.Delay(100);
		lock (seen) Assert.That(seen, Is.Empty);
	}

	// An HttpClient of the application that follows redirects: the token has gone, but the client does not take the answer.
	[Test]
	public void AnswerFromAnotherServerWithAnHttpClientThatFollowsRedirects()
	{
		using var other = Listen("127.0.0.2", out var otherPort);
		_ = Serve(other, c => Answer(c, 200, "\"1.0.0\""));
		using var chroma = Listen("127.0.0.1", out var port);
		_ = Serve(chroma, c => Redirect(c, 302, $"http://127.0.0.2:{otherPort}{c.Request.Url!.AbsolutePath}"));
		using var httpClient = new HttpClient();
		var client = new ChromaClient(new ChromaConfigurationOptions($"http://127.0.0.1:{port}/").WithChromaToken("secret-token"), httpClient);
		var ex = Assert.ThrowsAsync<ChromaException>(() => client.GetVersionAsync());
		Assert.That(ex!.Message, Does.Contain("The answer came from http://127.0.0.2:"));
	}

	// The DI client with a primary handler of the application that follows redirects: the same check after the answer.
	[Test]
	public void AnswerFromAnotherServerWithAPrimaryHandlerOfTheApplication()
	{
		using var other = Listen("127.0.0.2", out var otherPort);
		_ = Serve(other, c => Answer(c, 200, "\"1.0.0\""));
		using var chroma = Listen("127.0.0.1", out var port);
		_ = Serve(chroma, c => Redirect(c, 302, $"http://127.0.0.2:{otherPort}{c.Request.Url!.AbsolutePath}"));
		using var provider = new ServiceCollection()
			.AddChromaClient(_ => new ChromaConfigurationOptions($"http://127.0.0.1:{port}/"), b => b.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler()))
			.BuildServiceProvider();
		var ex = Assert.ThrowsAsync<ChromaException>(() => provider.GetRequiredService<ChromaClient>().GetVersionAsync());
		Assert.That(ex!.Message, Does.Contain("The answer came from http://127.0.0.2:"));
	}

	[Test]
	public void RedirectToAnotherPortIsRefused()
	{
		using var other = Listen("127.0.0.1", out var otherPort);
		var hits = 0;
		_ = Serve(other, c => { Interlocked.Increment(ref hits); return Answer(c, 200, "\"1.0.0\""); });
		using var chroma = Listen("127.0.0.1", out var port);
		_ = Serve(chroma, c => Redirect(c, 308, $"http://127.0.0.1:{otherPort}{c.Request.Url!.AbsolutePath}"));
		using var client = new ChromaClient(new ChromaConfigurationOptions($"http://127.0.0.1:{port}/"));
		Assert.ThrowsAsync<ChromaException>(() => client.GetVersionAsync());
		Assert.That(hits, Is.Zero);
	}

	// 307 and 308 keep the method and the body; 301 and 302 of a POST and 303 become a GET without a body, as with HttpClient.
	[TestCase(307, "POST", "\"payload\"")]
	[TestCase(308, "POST", "\"payload\"")]
	[TestCase(301, "GET", "")]
	[TestCase(302, "GET", "")]
	[TestCase(303, "GET", "")]
	public async Task RedirectOnTheSameServerKeepsWhatItMust(int status, string method, string body)
	{
		using var chroma = Listen("127.0.0.1", out var port);
		var seen = new List<(string Method, string Body, string? Token, string? ContentType)>();
		_ = Serve(chroma, async c =>
		{
			if (c.Request.Url!.AbsolutePath.EndsWith("/start"))
			{
				await Redirect(c, status, "/api/v2/end");
				return;
			}
			using var reader = new StreamReader(c.Request.InputStream);
			var text = await reader.ReadToEndAsync();
			lock (seen) seen.Add((c.Request.HttpMethod, text, c.Request.Headers["X-Chroma-Token"], c.Request.ContentType));
			await Answer(c, 200, "\"done\"");
		});
		using var httpClient = new HttpClient(ChromaClient.NewHttpMessageHandler());
		var client = new ChromaHttpClient(httpClient, new ChromaConfigurationOptions($"http://127.0.0.1:{port}/").WithChromaToken("secret-token"));
		var answer = await client.Post<string, string>("start", "payload", new RequestQueryParams(), default);
		Assert.That(answer, Is.EqualTo("done"));
		lock (seen)
		{
			Assert.That(seen, Has.Count.EqualTo(1));
			Assert.That(seen[0].Method, Is.EqualTo(method));
			Assert.That(seen[0].Body, Is.EqualTo(body));
			Assert.That(seen[0].Token, Is.EqualTo("secret-token"));
			if (body is not "")
			{
				Assert.That(seen[0].ContentType, Does.StartWith("application/json"));
			}
		}
	}

	[Test]
	public void RedirectsBeyondTheLimitAreAChromaException()
	{
		using var chroma = Listen("127.0.0.1", out var port);
		var hits = 0;
		_ = Serve(chroma, c => Redirect(c, 302, $"/api/v2/hop{Interlocked.Increment(ref hits)}"));
		using var client = new ChromaClient(new ChromaConfigurationOptions($"http://127.0.0.1:{port}/"));
		var ex = Assert.ThrowsAsync<ChromaException>(() => client.GetVersionAsync());
		Assert.That(ex!.Message, Does.Contain($"more than {ChromaHttpClient.MaxRedirects} times"));
		Assert.That(hits, Is.EqualTo(ChromaHttpClient.MaxRedirects + 1));
	}

	[TestCase("http://h:8000/", "http://h:8000/x", true)]
	[TestCase("http://h:8000/", "http://H:8000/x", true)]
	[TestCase("http://h/", "https://h/x", true)]
	[TestCase("http://h:8000/", "https://h:8000/x", true)]
	[TestCase("http://h:8000/", "https://h/x", false)]
	[TestCase("https://h/", "http://h/x", false)]
	[TestCase("https://h:8443/", "http://h:8443/x", false)]
	[TestCase("http://h:8000/", "http://h:8001/x", false)]
	[TestCase("http://h:8000/", "http://other:8000/x", false)]
	[TestCase("https://h/", "https://h.evil/x", false)]
	public void SameServer(string origin, string target, bool same)
		=> Assert.That(ChromaHttpClient.IsSameServer(new Uri(origin), new Uri(target)), Is.EqualTo(same));

	[Test]
	public void ErrorMessageOfAHugeBodyIsBounded()
	{
		using var chroma = Listen("127.0.0.1", out var port);
		_ = Serve(chroma, c => Answer(c, 500, "{\"error\":\"InternalError\",\"message\":\"" + new string('a', 10 << 20) + "\"}"));
		using var client = new ChromaClient(new ChromaConfigurationOptions($"http://127.0.0.1:{port}/"));
		var ex = Assert.ThrowsAsync<ChromaException>(() => client.GetVersionAsync());
		Assert.That(ex!.Message.Length, Is.LessThanOrEqualTo(HttpClientHelpers.MaxErrorMessageLength + " (truncated)".Length));
		Assert.That(ex.Message, Does.EndWith(" (truncated)"));
	}

	[Test]
	public void ErrorMessageThatIsNotJsonIsBounded()
	{
		using var chroma = Listen("127.0.0.1", out var port);
		_ = Serve(chroma, c => Answer(c, 502, new string('b', 1 << 20)));
		using var client = new ChromaClient(new ChromaConfigurationOptions($"http://127.0.0.1:{port}/"));
		var ex = Assert.ThrowsAsync<ChromaException>(() => client.GetVersionAsync());
		Assert.That(ex!.Message, Does.StartWith("Couldn't parse the incoming error message body: bbb"));
		Assert.That(ex.Message, Does.EndWith(" (truncated)"));
		Assert.That(ex.Message.Length, Is.LessThanOrEqualTo(HttpClientHelpers.MaxErrorMessageLength + " (truncated)".Length));
	}

	[Test]
	public void ShortErrorMessageIsKept()
	{
		using var chroma = Listen("127.0.0.1", out var port);
		_ = Serve(chroma, c => Answer(c, 400, "{\"error\":\"InvalidArgumentError\",\"message\":\"bad\"}"));
		using var client = new ChromaClient(new ChromaConfigurationOptions($"http://127.0.0.1:{port}/"));
		var ex = Assert.ThrowsAsync<ChromaException>(() => client.GetVersionAsync());
		Assert.That(ex!.Message, Is.EqualTo("bad"));
		Assert.That(ex.ErrorType, Is.EqualTo("InvalidArgumentError"));
	}

	// Larger than the limit the client takes, also with an HttpClient of the application.
	[Test]
	public void AnswerLargerThanTheLimitIsAChromaException()
	{
		using var chroma = Listen("127.0.0.1", out var port);
		_ = Serve(chroma, c => Stream(c, "\"", Encoding.ASCII.GetBytes(new string('a', 1 << 20)), 1024));
		using var httpClient = new HttpClient();
		var client = new ChromaClient(new ChromaConfigurationOptions($"http://127.0.0.1:{port}/").WithMaxResponseSize(16 << 20), httpClient);
		var ex = Assert.ThrowsAsync<ChromaException>(() => client.GetVersionAsync());
		Assert.That(ex!.Message, Does.Contain("larger than MaxResponseSize"));
	}

	// The default limit, 512 MB, against an answer of 1 GB in small values: a ChromaException, not an OutOfMemoryException.
	[Test]
	public void AnswerLargerThanTheDefaultLimitIsAChromaException()
	{
		using var chroma = Listen("127.0.0.1", out var port);
		_ = Serve(chroma, c => Stream(c, "[", Encoding.ASCII.GetBytes("[" + string.Join(",", Enumerable.Repeat("\"a\"", (1 << 18) - 1)) + "],"), 1024));
		using var httpClient = new HttpClient(ChromaClient.NewHttpMessageHandler());
		var client = new ChromaHttpClient(httpClient, new ChromaConfigurationOptions($"http://127.0.0.1:{port}/"));
		var ex = Assert.ThrowsAsync<ChromaException>(() => client.Get<string[][]>("anything", new RequestQueryParams(), default));
		Assert.That(ex!.Message, Is.EqualTo($"The answer of the server is larger than MaxResponseSize ({512L << 20} bytes)."));
	}

	// The limit itself is taken; one byte more is not, with or without a Content-Length.
	[TestCase(true)]
	[TestCase(false)]
	public async Task AnswerOfExactlyTheLimit(bool chunked)
	{
		const string body = "\"0123456789\"";
		using var chroma = Listen("127.0.0.1", out var port);
		_ = Serve(chroma, async c =>
		{
			var bytes = Encoding.ASCII.GetBytes(body);
			c.Response.StatusCode = 200;
			c.Response.SendChunked = chunked;
			if (!chunked)
			{
				c.Response.ContentLength64 = bytes.Length;
			}
			await c.Response.OutputStream.WriteAsync(bytes);
			c.Response.Close();
		});
		using var exact = new ChromaClient(new ChromaConfigurationOptions($"http://127.0.0.1:{port}/").WithMaxResponseSize(body.Length));
		Assert.That(await exact.GetVersionAsync(), Is.EqualTo("0123456789"));
		using var smaller = new ChromaClient(new ChromaConfigurationOptions($"http://127.0.0.1:{port}/").WithMaxResponseSize(body.Length - 1));
		var ex = Assert.ThrowsAsync<ChromaException>(() => smaller.GetVersionAsync());
		Assert.That(ex!.Message, Does.Contain("larger than MaxResponseSize"));
	}

	[Test]
	public void MaxResponseSizeOptions()
	{
		var options = new ChromaConfigurationOptions("http://localhost:8000/");
		Assert.That(options.MaxResponseSize, Is.EqualTo(512L * 1024 * 1024));
		Assert.That(options.WithMaxResponseSize(1000).MaxResponseSize, Is.EqualTo(1000));
		Assert.That(options.WithMaxResponseSize(1000).WithTenant("t").MaxResponseSize, Is.EqualTo(1000));
		Assert.Throws<ArgumentOutOfRangeException>(() => options.WithMaxResponseSize(0));
		Assert.Throws<ArgumentOutOfRangeException>(() => _ = new ChromaConfigurationOptions("http://localhost:8000/") { MaxResponseSize = -1 });
	}

	// The body of an answer comes within what is left of the Timeout of the HttpClient, as when HttpClient reads the whole answer.
	[Test]
	public void SlowBodyGoesInTheTimeoutOfTheHttpClient()
	{
		using var chroma = Listen("127.0.0.1", out var port);
		_ = Serve(chroma, async c =>
		{
			c.Response.StatusCode = 200;
			c.Response.SendChunked = true;
			try
			{
				await c.Response.OutputStream.WriteAsync(Encoding.ASCII.GetBytes("\"1."));
				await c.Response.OutputStream.FlushAsync();
				await Task.Delay(TimeSpan.FromSeconds(5));
				await c.Response.OutputStream.WriteAsync(Encoding.ASCII.GetBytes("0\""));
				c.Response.Close();
			}
			catch (Exception)
			{
			}
		});
		using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
		var client = new ChromaClient(new ChromaConfigurationOptions($"http://127.0.0.1:{port}/"), httpClient);
		var started = DateTime.UtcNow;
		Assert.ThrowsAsync<ChromaException>(() => client.GetVersionAsync());
		Assert.That(DateTime.UtcNow - started, Is.LessThan(TimeSpan.FromSeconds(4)));
	}

	// A cancellation of the caller while the body comes stops the read, as when HttpClient reads the whole answer.
	[Test]
	public void CancellationDuringTheBodyIsACancellation()
	{
		using var chroma = Listen("127.0.0.1", out var port);
		_ = Serve(chroma, async c =>
		{
			c.Response.StatusCode = 200;
			c.Response.SendChunked = true;
			try
			{
				await c.Response.OutputStream.WriteAsync(Encoding.ASCII.GetBytes("\"1."));
				await c.Response.OutputStream.FlushAsync();
				await Task.Delay(TimeSpan.FromSeconds(5));
				await c.Response.OutputStream.WriteAsync(Encoding.ASCII.GetBytes("0\""));
				c.Response.Close();
			}
			catch (Exception)
			{
			}
		});
		using var client = new ChromaClient(new ChromaConfigurationOptions($"http://127.0.0.1:{port}/"));
		using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));
		var started = DateTime.UtcNow;
		Assert.That(() => client.GetVersionAsync(cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
		Assert.That(DateTime.UtcNow - started, Is.LessThan(TimeSpan.FromSeconds(4)));
	}

	static HttpListener Listen(string address, out int port)
	{
		var socket = new TcpListener(IPAddress.Parse(address), 0);
		socket.Start();
		port = ((IPEndPoint)socket.LocalEndpoint).Port;
		socket.Stop();
		var listener = new HttpListener();
		listener.Prefixes.Add($"http://{address}:{port}/");
		listener.Start();
		return listener;
	}

	static async Task Serve(HttpListener listener, Func<HttpListenerContext, Task> handle)
	{
		while (listener.IsListening)
		{
			HttpListenerContext context;
			try
			{
				context = await listener.GetContextAsync();
			}
			catch (Exception)
			{
				return;
			}
			_ = Task.Run(() => handle(context));
		}
	}

	static async Task Stream(HttpListenerContext context, string start, byte[] chunk, int count)
	{
		context.Response.StatusCode = 200;
		context.Response.SendChunked = true;
		try
		{
			await context.Response.OutputStream.WriteAsync(Encoding.ASCII.GetBytes(start));
			for (var i = 0; i < count; i++)
			{
				await context.Response.OutputStream.WriteAsync(chunk);
			}
			context.Response.Close();
		}
		catch (Exception)
		{
			// The client stopped reading.
		}
	}

	static Task Redirect(HttpListenerContext context, int status, string location)
	{
		context.Response.StatusCode = status;
		context.Response.Headers["Location"] = location;
		context.Response.Close();
		return Task.CompletedTask;
	}

	static async Task Answer(HttpListenerContext context, int status, string body)
	{
		var bytes = Encoding.UTF8.GetBytes(body);
		context.Response.StatusCode = status;
		context.Response.ContentType = "application/json";
		await context.Response.OutputStream.WriteAsync(bytes);
		context.Response.Close();
	}
}
