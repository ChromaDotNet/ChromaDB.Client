using ChromaDB.Client.Common;

namespace ChromaDB.Client;

/// <summary>
/// The options of a client: the server, the default tenant and database, the credentials, the API version, how metadata values
/// are read and how records are split in batches. Each <c>With</c> method returns a copy with one setting changed.
/// </summary>
public class ChromaConfigurationOptions
{
	/// <summary>
	/// The URI of the server. Just the address, like <c>http://localhost:8000</c>, gets the path of the API version, like
	/// <c>/api/v2/</c>; a URI with a path is used as it is, with or without the trailing slash.
	/// </summary>
	public Uri Uri { get; init; }
	/// <summary>
	/// The tenant of the requests that are not given one; when null or empty, <c>default_tenant</c>.
	/// </summary>
	public string? Tenant { get; init; }
	/// <summary>
	/// The database of the requests that are not given one; when null or empty, <c>default_database</c>.
	/// </summary>
	public string? Database { get; init; }
	/// <summary>
	/// The token sent with each request, in the header that <c>ChromaTokenTransportHeader</c> names; none when null or empty.
	/// </summary>
	public string? ChromaToken { get; init; }
	/// <summary>
	/// The header that carries the token: <c>X-Chroma-Token</c> by default, or <c>Authorization: Bearer</c>.
	/// </summary>
	public ChromaTokenTransportHeader ChromaTokenTransportHeader { get; init; }
	/// <summary>
	/// The user name for basic authentication, sent in the <c>Authorization</c> header of each request; none when null.
	/// It cannot be used together with a token in the <c>Authorization</c> header.
	/// </summary>
	public string? BasicAuthUsername { get; init; }
	/// <summary>
	/// The password for basic authentication, sent with <c>BasicAuthUsername</c>.
	/// </summary>
	public string? BasicAuthPassword { get; init; }
	/// <summary>
	/// The version of the Chroma API the client uses: v2 by default, v1 for Chroma 0.5.15 and earlier.
	/// </summary>
	public ChromaApiVersion ApiVersion { get; init; }
	/// <summary>
	/// How the client reads the values of the metadata: <c>Exact</c> by default, or <c>Inferred</c>.
	/// </summary>
	public ChromaMetadataValues MetadataValues { get; init; } = ChromaMetadataValues.Exact;
	/// <summary>
	/// Whether <c>Add</c>, <c>Update</c>, <c>Upsert</c> and <c>Delete</c> send their records in batches of the
	/// <c>max_batch_size</c> of the server, and <c>Get</c> reads in pages of it; on by default.
	/// </summary>
	public bool BatchSplitting { get; init; } = true;
	private readonly int? _maxBatchSize;

	/// <summary>
	/// The largest batch, in records, when the records are sent in batches: the smaller of this and the <c>max_batch_size</c>
	/// of the server, or this alone where the server declares none. At least one record, also when set directly.
	/// </summary>
	public int? MaxBatchSize
	{
		get => _maxBatchSize;
		init => _maxBatchSize = value is null or > 0 ? value : throw new ArgumentOutOfRangeException(nameof(MaxBatchSize), value, "The batches need at least one record.");
	}

	/// <summary>
	/// Options for the server at the given URI, with the default tenant, the default database and the token, when given.
	/// </summary>
	public ChromaConfigurationOptions(Uri uri, string? defaultTenant = null, string? defaultDatabase = null, string? chromaToken = null)
	{
		Uri = uri;
		Tenant = defaultTenant;
		Database = defaultDatabase;
		ChromaToken = chromaToken;
	}

	/// <summary>
	/// Options for the server at the given URI, as a string, with the default tenant, the default database and the token, when given.
	/// </summary>
	public ChromaConfigurationOptions(string uri, string? defaultTenant = null, string? defaultDatabase = null, string? chromaToken = null)
		: this(new Uri(uri), defaultTenant, defaultDatabase, chromaToken)
	{ }

	/// <summary>
	/// Options for the server at <c>http://localhost:8000</c>.
	/// </summary>
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
		MaxBatchSize = options.MaxBatchSize;
	}

	/// <summary>
	/// A copy of these options with the given URI.
	/// </summary>
	public ChromaConfigurationOptions WithUri(Uri uri)
		=> new(this) { Uri = uri };

	/// <summary>
	/// A copy of these options with the given URI, as a string.
	/// </summary>
	public ChromaConfigurationOptions WithUri(string uri)
		=> new(this) { Uri = new Uri(uri) };

	/// <summary>
	/// A copy of these options with the given default tenant.
	/// </summary>
	public ChromaConfigurationOptions WithTenant(string tenant)
		=> new(this) { Tenant = tenant };

	/// <summary>
	/// A copy of these options with the given default database.
	/// </summary>
	public ChromaConfigurationOptions WithDatabase(string database)
		=> new(this) { Database = database };

	/// <summary>
	/// A copy of these options with the given token, in the header these options already name: <c>X-Chroma-Token</c> by default.
	/// </summary>
	public ChromaConfigurationOptions WithChromaToken(string chromaToken)
		=> new(this) { ChromaToken = chromaToken };

	/// <summary>
	/// A copy of these options with the given token, in the given header.
	/// </summary>
	public ChromaConfigurationOptions WithChromaToken(string chromaToken, ChromaTokenTransportHeader transportHeader)
		=> new(this) { ChromaToken = chromaToken, ChromaTokenTransportHeader = transportHeader };

	/// <summary>
	/// A copy of these options with the user name and the password for basic authentication, for the Chroma 0.x servers with
	/// basic authentication.
	/// </summary>
	public ChromaConfigurationOptions WithBasicAuth(string username, string password)
		=> new(this) { BasicAuthUsername = username, BasicAuthPassword = password };

	/// <summary>
	/// A copy of these options with the given version of the Chroma API.
	/// </summary>
	public ChromaConfigurationOptions WithApiVersion(ChromaApiVersion apiVersion)
		=> new(this) { ApiVersion = apiVersion };

	/// <summary>
	/// A copy of these options that reads the values of the metadata the given way.
	/// </summary>
	public ChromaConfigurationOptions WithMetadataValues(ChromaMetadataValues metadataValues)
		=> new(this) { MetadataValues = metadataValues };

	/// <summary>
	/// <c>Add</c>, <c>Update</c>, <c>Upsert</c> and <c>Delete</c> send their records in batches of the <c>max_batch_size</c>
	/// of the server, one request after the other, as by default. If a batch fails, the earlier ones stay written. With
	/// <c>false</c> the records go in one request.
	/// </summary>
	public ChromaConfigurationOptions WithBatchSplitting(bool batchSplitting = true)
		=> new(this) { BatchSplitting = batchSplitting };

	/// <summary>
	/// Batches of at most <c>maxBatchSize</c> records, or of the <c>max_batch_size</c> of the server if smaller. For a server whose
	/// limit differs from the one it declares, like Chroma Cloud, which declares 1000 but takes 300 records per write by default.
	/// </summary>
	public ChromaConfigurationOptions WithBatchSplitting(int maxBatchSize)
		=> maxBatchSize > 0
			? new(this) { BatchSplitting = true, MaxBatchSize = maxBatchSize }
			: throw new ArgumentOutOfRangeException(nameof(maxBatchSize), maxBatchSize, "The batches need at least one record.");
}
