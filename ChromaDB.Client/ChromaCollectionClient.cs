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
		var base64 = records.Embeddings is not null && await _httpClient.SupportsBase64Embeddings(cancellationToken);
		var requestParams = new RequestQueryParams()
			.Insert("{tenant}", _tenant)
			.Insert("{database}", _database)
			.Insert("{collection_id}", _collection.Id);
		foreach (var batch in await Batches(records, cancellationToken))
		{
			var request = new CollectionAddRequest()
			{
				Ids = batch.Ids,
				Embeddings = batch.Embeddings is { } embeddings ? new ChromaEmbeddings(embeddings, base64) : null,
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
		var base64 = records.Embeddings is not null && await _httpClient.SupportsBase64Embeddings(cancellationToken);
		var requestParams = new RequestQueryParams()
			.Insert("{tenant}", _tenant)
			.Insert("{database}", _database)
			.Insert("{collection_id}", _collection.Id);
		foreach (var batch in await Batches(records, cancellationToken))
		{
			var request = new CollectionUpdateRequest()
			{
				Ids = batch.Ids,
				Embeddings = batch.Embeddings is { } embeddings ? new ChromaEmbeddings(embeddings, base64) : null,
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
		var base64 = records.Embeddings is not null && await _httpClient.SupportsBase64Embeddings(cancellationToken);
		var requestParams = new RequestQueryParams()
			.Insert("{tenant}", _tenant)
			.Insert("{database}", _database)
			.Insert("{collection_id}", _collection.Id);
		foreach (var batch in await Batches(records, cancellationToken))
		{
			var request = new CollectionUpsertRequest()
			{
				Ids = batch.Ids,
				Embeddings = batch.Embeddings is { } embeddings ? new ChromaEmbeddings(embeddings, base64) : null,
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
		if (!_httpClient.BatchSplitting || await BatchSize(cancellationToken) is not { } size || records.Ids.Count <= size)
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

	// The smaller of the limit of the caller and the max_batch_size of the server, either one when the other is missing.
	private async Task<int?> BatchSize(CancellationToken cancellationToken)
	{
		var server = await _httpClient.GetMaxBatchSize(cancellationToken);
		return _httpClient.MaxBatchSize is { } caller && server is { } declared ? Math.Min(caller, declared) : _httpClient.MaxBatchSize ?? server;
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

	// Deletes the records with the ids, the ones the filters match, or both, at most delete.Limit of them. Returns how many records
	// were deleted when the server says it, from Chroma 1.5.3; null otherwise.
	public async Task<int?> Delete(ChromaDelete delete, CancellationToken cancellationToken = default)
	{
		// The rules of Chroma and of its Python client, checked before any request.
		if (delete.Ids is null && delete.Where is null && delete.WhereDocument is null)
		{
			throw new ArgumentException("A delete needs ids, a where filter or a where document filter: without them it would select every record.", nameof(delete));
		}
		if (delete.Ids is [])
		{
			throw new ArgumentException("The ids of a delete cannot be empty: leave them null to delete by the filters only.", nameof(delete));
		}
		if (delete.Limit is < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(delete), "The limit of a delete cannot be negative.");
		}
		if (delete.Limit is not null && delete.Where is null && delete.WhereDocument is null)
		{
			throw new ArgumentException("The limit of a delete needs a where or where document filter: Chroma rejects it with the ids alone.", nameof(delete));
		}
		if (delete.Limit is not null && !await _httpClient.SupportsDeleteLimit(cancellationToken))
		{
			throw new ChromaException("The server ignores the limit of a delete and would delete every matching record: Chroma 1.5.3 and later apply it.");
		}
		var requestParams = new RequestQueryParams()
			.Insert("{tenant}", _tenant)
			.Insert("{database}", _database)
			.Insert("{collection_id}", _collection.Id);
		var batches = delete.Ids is null ? [null] : (await Batches(new ChromaRecords(delete.Ids), cancellationToken)).Select(batch => batch.Ids).ToList<List<string>?>();
		int? deleted = null;
		var remaining = delete.Limit;
		foreach (var ids in batches)
		{
			var request = new CollectionDeleteRequest()
			{
				Ids = ids,
				Where = delete.Where?.ToWhere(),
				WhereDocument = delete.WhereDocument?.ToWhereDocument(),
				Limit = remaining,
			};
			// {"deleted": n} from Chroma 1.5.3; {} or null before, and a list of ids from some 0.x servers.
			var response = await _httpClient.Post<CollectionDeleteRequest, System.Text.Json.JsonElement>(_httpClient.Routes.Collection + "/delete", request, requestParams, cancellationToken);
			if (response.ValueKind == System.Text.Json.JsonValueKind.Object
				&& response.TryGetProperty("deleted", out var count) && count.ValueKind == System.Text.Json.JsonValueKind.Number)
			{
				deleted = (deleted ?? 0) + count.GetInt32();
				remaining -= count.GetInt32();
			}
			// The limit is used up: the next batches would delete nothing.
			if (remaining is <= 0)
			{
				break;
			}
		}
		return deleted;
	}

	public async Task<int> Count(CancellationToken cancellationToken = default)
	{
		var requestParams = new RequestQueryParams()
			.Insert("{tenant}", _tenant)
			.Insert("{database}", _database)
			.Insert("{collection_id}", _collection.Id);
		return await _httpClient.Get<int>(_httpClient.Routes.Collection + "/count", requestParams, cancellationToken);
	}

	// The count at a read level: on Chroma Cloud, ChromaReadLevel.IndexOnly leaves out the records not indexed yet.
	public async Task<int> Count(ChromaReadLevel readLevel, CancellationToken cancellationToken = default)
	{
		var requestParams = new RequestQueryParams()
			.Insert("{tenant}", _tenant)
			.Insert("{database}", _database)
			.Insert("{collection_id}", _collection.Id)
			.Insert("{read_level}", ReadLevelName(readLevel));
		return await _httpClient.Get<int>(_httpClient.Routes.Collection + "/count?read_level={read_level}", requestParams, cancellationToken);
	}

	private static string ReadLevelName(ChromaReadLevel readLevel) => readLevel switch
	{
		ChromaReadLevel.IndexAndWal => "index_and_wal",
		ChromaReadLevel.IndexOnly => "index_only",
		ChromaReadLevel.IndexAndBoundedWal => "index_and_bounded_wal",
		_ => throw new ArgumentOutOfRangeException(nameof(readLevel)),
	};

	// A copy of the collection under a new name, with the same records: Chroma Cloud only, a single server answers 501.
	public async Task<ChromaCollection> Fork(string newName, CancellationToken cancellationToken = default)
	{
		var requestParams = new RequestQueryParams()
			.Insert("{tenant}", _tenant)
			.Insert("{database}", _database)
			.Insert("{collection_id}", _collection.Id);
		var request = new ForkCollectionRequest()
		{
			NewName = newName,
		};
		return await _httpClient.Post<ForkCollectionRequest, ChromaCollection>(_httpClient.Routes.Collection + "/fork", request, requestParams, cancellationToken);
	}

	// How many forks the collection has: Chroma Cloud only.
	public async Task<int> ForkCount(CancellationToken cancellationToken = default)
	{
		var requestParams = new RequestQueryParams()
			.Insert("{tenant}", _tenant)
			.Insert("{database}", _database)
			.Insert("{collection_id}", _collection.Id);
		return (await _httpClient.Get<ForkCountResponse>(_httpClient.Routes.Collection + "/fork_count", requestParams, cancellationToken)).Count;
	}

	// How far the writes to the collection are indexed: Chroma Cloud only.
	public async Task<ChromaIndexingStatus> GetIndexingStatus(CancellationToken cancellationToken = default)
	{
		var requestParams = new RequestQueryParams()
			.Insert("{tenant}", _tenant)
			.Insert("{database}", _database)
			.Insert("{collection_id}", _collection.Id);
		return await _httpClient.Get<ChromaIndexingStatus>(_httpClient.Routes.Collection + "/indexing_status", requestParams, cancellationToken);
	}

	// Attaches a function of Chroma Cloud, like ChromaFunctions.Statistics, under a name of its own; its results go to the output
	// collection. Created is false when a function with that name was already attached.
	public async Task<(ChromaAttachedFunction AttachedFunction, bool Created)> AttachFunction(string function, string name, string outputCollection, Dictionary<string, object>? parameters = null, CancellationToken cancellationToken = default)
	{
		var requestParams = new RequestQueryParams()
			.Insert("{tenant}", _tenant)
			.Insert("{database}", _database)
			.Insert("{collection_id}", _collection.Id);
		var request = new AttachFunctionRequest()
		{
			Name = name,
			FunctionId = function,
			OutputCollection = outputCollection,
			Params = parameters,
		};
		var response = await _httpClient.Post<AttachFunctionRequest, AttachFunctionResponse>(_httpClient.Routes.Collection + "/functions/attach", request, requestParams, cancellationToken);
		return (response.AttachedFunction, response.Created);
	}

	public async Task<ChromaAttachedFunction> GetAttachedFunction(string name, CancellationToken cancellationToken = default)
	{
		var requestParams = new RequestQueryParams()
			.Insert("{tenant}", _tenant)
			.Insert("{database}", _database)
			.Insert("{collection_id}", _collection.Id)
			.Insert("{function_name}", name);
		return (await _httpClient.Get<GetAttachedFunctionResponse>(_httpClient.Routes.Collection + "/functions/{function_name}", requestParams, cancellationToken)).AttachedFunction;
	}

	public async Task<bool> DetachFunction(string name, bool deleteOutputCollection = false, CancellationToken cancellationToken = default)
	{
		var requestParams = new RequestQueryParams()
			.Insert("{tenant}", _tenant)
			.Insert("{database}", _database)
			.Insert("{collection_id}", _collection.Id)
			.Insert("{function_name}", name);
		var request = new DetachFunctionRequest()
		{
			DeleteOutput = deleteOutputCollection,
		};
		return (await _httpClient.Post<DetachFunctionRequest, DetachFunctionResponse>(_httpClient.Routes.Collection + "/attached_functions/{function_name}/detach", request, requestParams, cancellationToken)).Success;
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

	// Changes the settings of the index, like ef_search. Chroma 1.0.6 and later apply them; the earlier versions answer
	// without applying them, so on them the client throws a ChromaException before sending the request.
	public async Task ModifyConfiguration(ChromaCollectionConfigurationUpdate configuration, CancellationToken cancellationToken = default)
	{
		if (!await AppliesNewConfiguration(cancellationToken))
		{
			throw new ChromaException("The server answers without applying a new configuration: Chroma 1.0.6 and later apply it.");
		}
		var requestParams = new RequestQueryParams()
			.Insert("{tenant}", _tenant)
			.Insert("{database}", _database)
			.Insert("{collection_id}", _collection.Id);
		var request = new CollectionModifyRequest()
		{
			Configuration = configuration,
		};
		await _httpClient.Put(_httpClient.Routes.Collection, request, requestParams, cancellationToken);
	}

	// Chroma 1.0.6 and later, Chroma Cloud too, send the configuration with "hnsw" and "spann"; 0.5.4 to 1.0.5 with
	// "hnsw_configuration", and 0.4.10 to 0.5.3 send none. Without the configuration at hand, the collection is read by its name.
	private async Task<bool> AppliesNewConfiguration(CancellationToken cancellationToken)
	{
		var configuration = _collection.ConfigurationJson;
		if (configuration is null)
		{
			var requestParams = new RequestQueryParams()
				.Insert("{collectionName}", _collection.Name)
				.Insert("{tenant}", _tenant)
				.Insert("{database}", _database);
			configuration = (await _httpClient.Get<ChromaCollection>(_httpClient.Routes.CollectionByName, requestParams, cancellationToken)).ConfigurationJson;
		}
		return configuration is { ValueKind: System.Text.Json.JsonValueKind.Object } value
			&& (value.TryGetProperty("hnsw", out _) || value.TryGetProperty("spann", out _));
	}
}
