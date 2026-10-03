using ChromaDB.Client.Common;
using ChromaDB.Client.Models;
using ChromaDB.Client.Models.Requests;
using ChromaDB.Client.Models.Responses;

namespace ChromaDB.Client;

public class ChromaCollectionClient
{
	private readonly ChromaCollection _collection;
	private readonly ChromaHttpClient _httpClient;
	private readonly string _tenant;
	private readonly string _database;

	public ChromaCollectionClient(ChromaCollection collection, ChromaConfigurationOptions options, HttpClient httpClient)
	{
		_collection = collection;
		_httpClient = new ChromaHttpClient(httpClient, options);
		// An empty tenant or database means the default, as in ChromaClient.
		_tenant = collection.Tenant is not null and not [] ? collection.Tenant
			: options.Tenant is not null and not [] ? options.Tenant
			: ClientConstants.DefaultTenantName;
		_database = collection.Database is not null and not [] ? collection.Database
			: options.Database is not null and not [] ? options.Database
			: ClientConstants.DefaultDatabaseName;
	}

	public ChromaCollection Collection => _collection;

	public async Task<ChromaCollectionEntry?> Get(string id, ChromaWhereOperator? where = null, ChromaWhereDocumentOperator? whereDocument = null, ChromaGetInclude? include = null, CancellationToken cancellationToken = default)
		=> (await Get([id], where: where, whereDocument: whereDocument, include: include, cancellationToken: cancellationToken)).FirstOrDefault();

	public async Task<List<ChromaCollectionEntry>> Get(List<string>? ids = null, ChromaWhereOperator? where = null, ChromaWhereDocumentOperator? whereDocument = null, int? limit = null, int? offset = null, ChromaGetInclude? include = null, CancellationToken cancellationToken = default)
	{
		var requestParams = new RequestQueryParams()
			.Insert("{tenant}", _tenant)
			.Insert("{database}", _database)
			.Insert("{collection_id}", _collection.Id);
		var request = new CollectionGetRequest()
		{
			Ids = ids,
			Where = where?.ToWhere(),
			WhereDocument = whereDocument?.ToWhereDocument(),
			Limit = limit,
			Offset = offset,
			Include = (include ?? ChromaGetInclude.Metadatas | ChromaGetInclude.Documents).ToInclude(),
		};
		var response = await _httpClient.Post<CollectionGetRequest, CollectionEntriesGetResponse>(_httpClient.Routes.Collection + "/get", request, requestParams, cancellationToken);
		return response.Map() ?? [];
	}

	public async Task<List<ChromaCollectionQueryEntry>> Query(ReadOnlyMemory<float> queryEmbeddings, int nResults = 10, ChromaWhereOperator? where = null, ChromaWhereDocumentOperator? whereDocument = null, ChromaQueryInclude? include = null, CancellationToken cancellationToken = default)
		=> (await Query([queryEmbeddings], nResults: nResults, where: where, whereDocument: whereDocument, include: include, cancellationToken: cancellationToken)).FirstOrDefault() ?? [];

	public async Task<List<List<ChromaCollectionQueryEntry>>> Query(List<ReadOnlyMemory<float>> queryEmbeddings, int nResults = 10, ChromaWhereOperator? where = null, ChromaWhereDocumentOperator? whereDocument = null, ChromaQueryInclude? include = null, CancellationToken cancellationToken = default)
	{
		var requestParams = new RequestQueryParams()
			.Insert("{tenant}", _tenant)
			.Insert("{database}", _database)
			.Insert("{collection_id}", _collection.Id);
		var request = new CollectionQueryRequest()
		{
			QueryEmbeddings = queryEmbeddings,
			NResults = nResults,
			Where = where?.ToWhere(),
			WhereDocument = whereDocument?.ToWhereDocument(),
			Include = (include ?? ChromaQueryInclude.Metadatas | ChromaQueryInclude.Documents | ChromaQueryInclude.Distances).ToInclude(),
		};
		var response = await _httpClient.Post<CollectionQueryRequest, CollectionEntriesQueryResponse>(_httpClient.Routes.Collection + "/query", request, requestParams, cancellationToken);
		return response.Map() ?? [];
	}

	public Task Add(List<string> ids, List<ReadOnlyMemory<float>>? embeddings = null, List<Dictionary<string, object>>? metadatas = null, List<string>? documents = null, CancellationToken cancellationToken = default)
		=> Add(new ChromaRecords(ids) { Embeddings = embeddings, Metadatas = metadatas, Documents = documents }, cancellationToken);

	public async Task Add(ChromaRecords records, CancellationToken cancellationToken = default)
	{
		var requestParams = new RequestQueryParams()
			.Insert("{tenant}", _tenant)
			.Insert("{database}", _database)
			.Insert("{collection_id}", _collection.Id);
		var request = new CollectionAddRequest()
		{
			Ids = records.Ids,
			Embeddings = records.Embeddings,
			Metadatas = records.Metadatas,
			Documents = records.Documents,
			Uris = records.Uris,
		};
		await _httpClient.Post(_httpClient.Routes.Collection + "/add", request, requestParams, cancellationToken);
	}

	public Task Update(List<string> ids, List<ReadOnlyMemory<float>>? embeddings = null, List<Dictionary<string, object>>? metadatas = null, List<string>? documents = null, CancellationToken cancellationToken = default)
		=> Update(new ChromaRecords(ids) { Embeddings = embeddings, Metadatas = metadatas, Documents = documents }, cancellationToken);

	public async Task Update(ChromaRecords records, CancellationToken cancellationToken = default)
	{
		var requestParams = new RequestQueryParams()
			.Insert("{tenant}", _tenant)
			.Insert("{database}", _database)
			.Insert("{collection_id}", _collection.Id);
		var request = new CollectionUpdateRequest()
		{
			Ids = records.Ids,
			Embeddings = records.Embeddings,
			Metadatas = records.Metadatas,
			Documents = records.Documents,
			Uris = records.Uris,
		};
		await _httpClient.Post(_httpClient.Routes.Collection + "/update", request, requestParams, cancellationToken);
	}

	public Task Upsert(List<string> ids, List<ReadOnlyMemory<float>>? embeddings = null, List<Dictionary<string, object>>? metadatas = null, List<string>? documents = null, CancellationToken cancellationToken = default)
		=> Upsert(new ChromaRecords(ids) { Embeddings = embeddings, Metadatas = metadatas, Documents = documents }, cancellationToken);

	public async Task Upsert(ChromaRecords records, CancellationToken cancellationToken = default)
	{
		var requestParams = new RequestQueryParams()
			.Insert("{tenant}", _tenant)
			.Insert("{database}", _database)
			.Insert("{collection_id}", _collection.Id);
		var request = new CollectionUpsertRequest()
		{
			Ids = records.Ids,
			Embeddings = records.Embeddings,
			Metadatas = records.Metadatas,
			Documents = records.Documents,
			Uris = records.Uris,
		};
		await _httpClient.Post(_httpClient.Routes.Collection + "/upsert", request, requestParams, cancellationToken);
	}

	public async Task Delete(List<string> ids, ChromaWhereOperator? where = null, ChromaWhereDocumentOperator? whereDocument = null, CancellationToken cancellationToken = default)
	{
		var requestParams = new RequestQueryParams()
			.Insert("{tenant}", _tenant)
			.Insert("{database}", _database)
			.Insert("{collection_id}", _collection.Id);
		var request = new CollectionDeleteRequest()
		{
			Ids = ids,
			Where = where?.ToWhere(),
			WhereDocument = whereDocument?.ToWhereDocument(),
		};
		await _httpClient.Post(_httpClient.Routes.Collection + "/delete", request, requestParams, cancellationToken);
	}

	public async Task<int> Count(CancellationToken cancellationToken = default)
	{
		var requestParams = new RequestQueryParams()
			.Insert("{tenant}", _tenant)
			.Insert("{database}", _database)
			.Insert("{collection_id}", _collection.Id);
		return await _httpClient.Get<int>(_httpClient.Routes.Collection + "/count", requestParams, cancellationToken);
	}

	public async Task<List<ChromaCollectionEntry>> Peek(int limit = 10, CancellationToken cancellationToken = default)
	{
		var requestParams = new RequestQueryParams()
			.Insert("{tenant}", _tenant)
			.Insert("{database}", _database)
			.Insert("{collection_id}", _collection.Id);
		var request = new CollectionPeekRequest()
		{
			Limit = limit,
		};
		var response = await _httpClient.Post<CollectionPeekRequest, CollectionEntriesGetResponse>(_httpClient.Routes.Collection + "/get", request, requestParams, cancellationToken);
		return response.Map() ?? [];
	}

	public async Task Modify(string? name = null, Dictionary<string, object>? metadata = null, CancellationToken cancellationToken = default)
	{
		var requestParams = new RequestQueryParams()
			.Insert("{tenant}", _tenant)
			.Insert("{database}", _database)
			.Insert("{collection_id}", _collection.Id);
		var request = new CollectionModifyRequest()
		{
			Name = name,
			Metadata = metadata,
		};
		await _httpClient.Put(_httpClient.Routes.Collection, request, requestParams, cancellationToken);
	}
}
