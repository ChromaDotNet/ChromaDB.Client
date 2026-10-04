using ChromaDB.Client.Common;

namespace ChromaDB.Client;

public class ChromaConfigurationOptions
{
	public Uri Uri { get; init; }
	public string? Tenant { get; init; }
	public string? Database { get; init; }
	public string? ChromaToken { get; init; }
	public ChromaTokenTransportHeader ChromaTokenTransportHeader { get; init; }
	public string? BasicAuthUsername { get; init; }
	public string? BasicAuthPassword { get; init; }
	public ChromaApiVersion ApiVersion { get; init; }
	public ChromaMetadataValues MetadataValues { get; init; }
	public bool BatchSplitting { get; init; }

	public ChromaConfigurationOptions(Uri uri, string? defaultTenant = null, string? defaultDatabase = null, string? chromaToken = null)
	{
		Uri = uri;
		Tenant = defaultTenant;
		Database = defaultDatabase;
		ChromaToken = chromaToken;
	}

	public ChromaConfigurationOptions(string uri, string? defaultTenant = null, string? defaultDatabase = null, string? chromaToken = null)
		: this(new Uri(uri), defaultTenant, defaultDatabase, chromaToken)
	{ }

	public ChromaConfigurationOptions()
		: this(ClientConstants.DefaultUri)
	{ }

	private ChromaConfigurationOptions(ChromaConfigurationOptions options)
		: this(options.Uri, options.Tenant, options.Database, options.ChromaToken)
	{
		ChromaTokenTransportHeader = options.ChromaTokenTransportHeader;
		BasicAuthUsername = options.BasicAuthUsername;
		BasicAuthPassword = options.BasicAuthPassword;
		ApiVersion = options.ApiVersion;
		MetadataValues = options.MetadataValues;
		BatchSplitting = options.BatchSplitting;
	}

	public ChromaConfigurationOptions WithUri(Uri uri)
		=> new(this) { Uri = uri };

	public ChromaConfigurationOptions WithUri(string uri)
		=> new(this) { Uri = new Uri(uri) };

	public ChromaConfigurationOptions WithTenant(string tenant)
		=> new(this) { Tenant = tenant };

	public ChromaConfigurationOptions WithDatabase(string database)
		=> new(this) { Database = database };

	public ChromaConfigurationOptions WithChromaToken(string chromaToken)
		=> new(this) { ChromaToken = chromaToken };

	public ChromaConfigurationOptions WithChromaToken(string chromaToken, ChromaTokenTransportHeader transportHeader)
		=> new(this) { ChromaToken = chromaToken, ChromaTokenTransportHeader = transportHeader };

	public ChromaConfigurationOptions WithBasicAuth(string username, string password)
		=> new(this) { BasicAuthUsername = username, BasicAuthPassword = password };

	public ChromaConfigurationOptions WithApiVersion(ChromaApiVersion apiVersion)
		=> new(this) { ApiVersion = apiVersion };

	public ChromaConfigurationOptions WithMetadataValues(ChromaMetadataValues metadataValues)
		=> new(this) { MetadataValues = metadataValues };

	// Add, Update, Upsert and Delete send their records in batches of the max_batch_size of the server, one request after
	// the other. If a batch fails, the earlier ones stay written.
	public ChromaConfigurationOptions WithBatchSplitting(bool batchSplitting = true)
		=> new(this) { BatchSplitting = batchSplitting };
}
