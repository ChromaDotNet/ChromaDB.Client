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
		: this(collection, options, new ChromaHttpClient(httpClient, options))
	{ }

	// Shares the ChromaHttpClient of a ChromaClient, with what it keeps, like the version of the server.
	internal ChromaCollectionClient(ChromaCollection collection, ChromaConfigurationOptions options, ChromaHttpClient httpClient)
	{
		_collection = collection;
		_httpClient = httpClient;
		// An empty tenant or database means the default, as in ChromaClient.
		_tenant = collection.Tenant is not null and not [] ? collection.Tenant
			: options.Tenant is not null and not [] ? options.Tenant
			: ClientConstants.DefaultTenantName;
		_database = collection.Database is not null and not [] ? collection.Database
			: options.Database is not null and not [] ? options.Database
			: ClientConstants.DefaultDatabaseName;
	}

	// Without getting the collection first: the requests on a collection need only its id, and the tenant and database of the options.
	public ChromaCollectionClient(Guid collectionId, string collectionName, ChromaConfigurationOptions options, HttpClient httpClient)
		: this(new ChromaCollection(collectionName) { Id = collectionId }, options, httpClient)
	{ }

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

	public Task<List<List<ChromaCollectionQueryEntry>>> Query(List<ReadOnlyMemory<float>> queryEmbeddings, int nResults = 10, ChromaWhereOperator? where = null, ChromaWhereDocumentOperator? whereDocument = null, ChromaQueryInclude? include = null, CancellationToken cancellationToken = default)
		=> Query(new ChromaQuery(queryEmbeddings) { NResults = nResults, Where = where, WhereDocument = whereDocument, Include = include }, cancellationToken);

	public async Task<List<List<ChromaCollectionQueryEntry>>> Query(ChromaQuery query, CancellationToken cancellationToken = default)
	{
		var requestParams = new RequestQueryParams()
			.Insert("{tenant}", _tenant)
			.Insert("{database}", _database)
			.Insert("{collection_id}", _collection.Id);
		var request = new CollectionQueryRequest()
		{
			QueryEmbeddings = query.QueryEmbeddings,
			NResults = query.NResults,
			Where = query.Where?.ToWhere(),
			WhereDocument = query.WhereDocument?.ToWhereDocument(),
			Include = (query.Include ?? ChromaQueryInclude.Metadatas | ChromaQueryInclude.Documents | ChromaQueryInclude.Distances).ToInclude(),
			Ids = query.Ids,
		};
		var response = await _httpClient.Post<CollectionQueryRequest, CollectionEntriesQueryResponse>(_httpClient.Routes.Collection + "/query", request, requestParams, cancellationToken);
		var result = response.Map() ?? [];
		// Chroma 0.x ignores the ids and searches all the records: a result outside the ids shows it. When all the results
		// are among the ids, they are also the nearest among them, so the answer is right on those servers too.
		if (query.Ids is not null)
		{
			var ids = new HashSet<string>(query.Ids);
			if (result.Any(entries => entries.Any(entry => !ids.Contains(entry.Id))))
			{
				throw new ChromaException("The server searched outside the ids of the query: it does not support them. Chroma 1.0.0 and later do.");
			}
		}
		return result;
	}

	public Task Add(List<string> ids, List<ReadOnlyMemory<float>>? embeddings = null, List<Dictionary<string, object>>? metadatas = null, List<string>? documents = null, CancellationToken cancellationToken = default)
		=> Add(new ChromaRecords(ids) { Embeddings = embeddings, Metadatas = metadatas, Documents = documents }, cancellationToken);

	public async Task Add(ChromaRecords records, CancellationToken cancellationToken = default)
	{
		await CheckListsInMetadata(records, cancellationToken);
		var requestParams = new RequestQueryParams()
			.Insert("{tenant}", _tenant)
			.Insert("{database}", _database)
			.Insert("{collection_id}", _collection.Id);
		foreach (var batch in await Batches(records, cancellationToken))
		{
			var request = new CollectionAddRequest()
			{
				Ids = batch.Ids,
				Embeddings = batch.Embeddings,
				Metadatas = batch.Metadatas,
				Documents = batch.Documents,
				Uris = batch.Uris,
			};
			await _httpClient.Post(_httpClient.Routes.Collection + "/add", request, requestParams, cancellationToken);
		}
	}

	public Task Update(List<string> ids, List<ReadOnlyMemory<float>>? embeddings = null, List<Dictionary<string, object>>? metadatas = null, List<string>? documents = null, CancellationToken cancellationToken = default)
		=> Update(new ChromaRecords(ids) { Embeddings = embeddings, Metadatas = metadatas, Documents = documents }, cancellationToken);

	public async Task Update(ChromaRecords records, CancellationToken cancellationToken = default)
	{
		await CheckListsInMetadata(records, cancellationToken);
		var requestParams = new RequestQueryParams()
			.Insert("{tenant}", _tenant)
			.Insert("{database}", _database)
			.Insert("{collection_id}", _collection.Id);
		foreach (var batch in await Batches(records, cancellationToken))
		{
			var request = new CollectionUpdateRequest()
			{
				Ids = batch.Ids,
				Embeddings = batch.Embeddings,
				Metadatas = batch.Metadatas,
				Documents = batch.Documents,
				Uris = batch.Uris,
			};
			await _httpClient.Post(_httpClient.Routes.Collection + "/update", request, requestParams, cancellationToken);
		}
	}

	public Task Upsert(List<string> ids, List<ReadOnlyMemory<float>>? embeddings = null, List<Dictionary<string, object>>? metadatas = null, List<string>? documents = null, CancellationToken cancellationToken = default)
		=> Upsert(new ChromaRecords(ids) { Embeddings = embeddings, Metadatas = metadatas, Documents = documents }, cancellationToken);

	public async Task Upsert(ChromaRecords records, CancellationToken cancellationToken = default)
	{
		await CheckListsInMetadata(records, cancellationToken);
		var requestParams = new RequestQueryParams()
			.Insert("{tenant}", _tenant)
			.Insert("{database}", _database)
			.Insert("{collection_id}", _collection.Id);
		foreach (var batch in await Batches(records, cancellationToken))
		{
			var request = new CollectionUpsertRequest()
			{
				Ids = batch.Ids,
				Embeddings = batch.Embeddings,
				Metadatas = batch.Metadatas,
				Documents = batch.Documents,
				Uris = batch.Uris,
			};
			await _httpClient.Post(_httpClient.Routes.Collection + "/upsert", request, requestParams, cancellationToken);
		}
	}

	// With ChromaConfigurationOptions.WithBatchSplitting, records beyond the max_batch_size of the server go in more requests.
	// Up to Chroma 1.0.13 a request beyond it fails; later versions accept it, but still declare the limit.
	private async Task<List<ChromaRecords>> Batches(ChromaRecords records, CancellationToken cancellationToken)
	{
		if (!_httpClient.BatchSplitting || await _httpClient.GetMaxBatchSize(cancellationToken) is not { } size || records.Ids.Count <= size)
		{
			return [records];
		}
		return Enumerable.Range(0, (records.Ids.Count + size - 1) / size)
			.Select(i => new ChromaRecords(records.Ids.Skip(i * size).Take(size).ToList())
			{
				Embeddings = records.Embeddings?.Skip(i * size).Take(size).ToList(),
				Metadatas = records.Metadatas?.Skip(i * size).Take(size).ToList(),
				Documents = records.Documents?.Skip(i * size).Take(size).ToList(),
				Uris = records.Uris?.Skip(i * size).Take(size).ToList(),
			})
			.ToList();
	}

	// The 0.x servers accept lists in metadata but drop them without an error; Chroma 1.0 to 1.4 reject them, 1.5.0 stores them.
	private async Task CheckListsInMetadata(ChromaRecords records, CancellationToken cancellationToken)
	{
		if (records.Metadatas?.Any(metadata => metadata?.Values.Any(IsList) == true) == true
			&& await _httpClient.IsChroma0(cancellationToken))
		{
			throw new ChromaException("Chroma 0.x drops the lists in metadata without an error: they need Chroma 1.5.0 or later.");
		}
	}

	// A JsonElement array is what the client returns for a list read with ChromaMetadataValues.Inferred.
	private static bool IsList(object? value)
		=> value is System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.Array }
			or System.Collections.IEnumerable and not string and not System.Collections.IDictionary;

	public async Task Delete(List<string> ids, ChromaWhereOperator? where = null, ChromaWhereDocumentOperator? whereDocument = null, CancellationToken cancellationToken = default)
	{
		var requestParams = new RequestQueryParams()
			.Insert("{tenant}", _tenant)
			.Insert("{database}", _database)
			.Insert("{collection_id}", _collection.Id);
		foreach (var batch in await Batches(new ChromaRecords(ids), cancellationToken))
		{
			var request = new CollectionDeleteRequest()
			{
				Ids = batch.Ids,
				Where = where?.ToWhere(),
				WhereDocument = whereDocument?.ToWhereDocument(),
			};
			await _httpClient.Post(_httpClient.Routes.Collection + "/delete", request, requestParams, cancellationToken);
		}
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
