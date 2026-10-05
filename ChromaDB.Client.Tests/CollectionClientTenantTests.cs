using System.Net;
using ChromaDB.Client.Models;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

[TestFixture]
public class CollectionClientTenantTests
{
	static readonly Guid Id = Guid.Parse("11111111-2222-3333-4444-555555555555");

	[TestCase(null, null, null, null, "default_tenant", "default_database")]
	[TestCase(null, null, "", "", "default_tenant", "default_database")]
	[TestCase(null, null, "options_tenant", "options_database", "options_tenant", "options_database")]
	[TestCase("", "", "options_tenant", "options_database", "options_tenant", "options_database")]
	[TestCase("collection_tenant", "collection_database", "options_tenant", "options_database", "collection_tenant", "collection_database")]
	public async Task TenantAndDatabaseOfTheRequests(string? collectionTenant, string? collectionDatabase, string? optionsTenant, string? optionsDatabase, string expectedTenant, string expectedDatabase)
	{
		var handler = new RecordingHandler();
		using var httpClient = new HttpClient(handler);
		var collection = new ChromaCollection("collection") { Id = Id, Tenant = collectionTenant, Database = collectionDatabase };
		var options = new ChromaConfigurationOptions("http://localhost:8000/api/v2/", tenant: optionsTenant, database: optionsDatabase);
		await new ChromaCollectionClient(collection, options, httpClient).CountAsync();
		Assert.That(handler.RequestUri?.AbsolutePath, Is.EqualTo($"/api/v2/tenants/{expectedTenant}/databases/{expectedDatabase}/collections/{Id}/count"));
	}

	sealed class RecordingHandler : HttpMessageHandler
	{
		public Uri? RequestUri { get; private set; }

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			RequestUri = request.RequestUri;
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("0") });
		}
	}
}
