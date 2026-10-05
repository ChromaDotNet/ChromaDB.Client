using ChromaDB.Client.Models;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

// Dispose closes the HttpClient the client created, and only that one. The requests have a cancelled token, so that none reaches the
// network: a disposed HttpClient throws ObjectDisposedException, which is not about Chroma and is not wrapped, before it looks at the token.
[TestFixture]
public class ClientDisposeTests
{
	[Test]
	public async Task OwnHttpClient()
	{
		using var cancelled = new CancellationTokenSource();
		cancelled.Cancel();
		var client = new ChromaClient("http://localhost:8000");
		Assert.That(client.Options.Uri, Is.EqualTo(new ChromaConfigurationOptions("http://localhost:8000").Uri));
		var collectionClient = client.GetCollectionClient(Guid.Parse("11111111-2222-3333-4444-555555555555"), "c");
		client.WithMetadataValues(ChromaMetadataValues.Exact).Dispose();
		await Assert.ThatAsync(() => client.HeartbeatAsync(cancelled.Token), Throws.InstanceOf<OperationCanceledException>());

		client.Dispose();
		await Assert.ThatAsync(() => client.HeartbeatAsync(cancelled.Token), Throws.InstanceOf<ObjectDisposedException>());
		await Assert.ThatAsync(() => collectionClient.CountAsync(cancelled.Token), Throws.InstanceOf<ObjectDisposedException>());
	}

	[Test]
	public async Task GivenHttpClient()
	{
		using var cancelled = new CancellationTokenSource();
		cancelled.Cancel();
		using var httpClient = new HttpClient();
		var options = new ChromaConfigurationOptions("http://localhost:8000");
		new ChromaClient(options, httpClient).Dispose();
		await Assert.ThatAsync(() => httpClient.GetAsync("http://localhost:8000/", cancelled.Token), Throws.InstanceOf<OperationCanceledException>());
		using var client = new ChromaClient(options);
		Assert.That(client.Options, Is.SameAs(options));
	}
}
