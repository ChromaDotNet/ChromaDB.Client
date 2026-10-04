using ChromaDB.Client;
using ChromaDB.Client.DependencyInjection;
using ChromaDB.Client.Models;
using Microsoft.Extensions.DependencyInjection;

// Runs the main calls of the client against the server at args[0], like http://localhost:8000.
// Published with trimming, it fails if the client needs code that trimming removed.
// --lists: the server stores lists in metadata (Chroma 1.5.0 and later), so they must work.
var options = new ChromaConfigurationOptions(uri: args.Length > 0 && !args[0].StartsWith("--") ? args[0] : "http://localhost:8000");
var listsStored = args.Contains("--lists");
using var httpClient = new HttpClient();
var client = new ChromaClient(options, httpClient);
var failures = 0;

async Task Check(string step, Func<Task<string>> action)
{
	try
	{
		Console.WriteLine($"ok   {step}: {await action()}");
	}
	catch (Exception ex)
	{
		failures++;
		Console.WriteLine($"FAIL {step}: {ex.GetType().Name}: {ex.Message}");
	}
}

static string Expect(string value, bool condition, string expected)
	=> condition ? value : throw new Exception($"expected {expected}, got {value}");

var version = "";
var name = $"trimming{Guid.NewGuid():N}";
ChromaCollectionClient collectionClient = null!;
var embeddings = new List<ReadOnlyMemory<float>> { new([1f, 0f, 0f]), new([0f, 1f, 0f]) };

await Check("Heartbeat", async () => (await client.Heartbeat()).NanosecondHeartbeat.ToString());
await Check("GetVersion", async () => version = await client.GetVersion());
await Check("GetPreFlightChecks", async () => (await client.GetPreFlightChecks()).MaxBatchSize.ToString());
await Check("CreateCollection", async () =>
{
	var collection = await client.CreateCollection(new ChromaCollectionDefinition(name) { Metadata = new() { ["owner"] = "trimming", ["level"] = 1 }, Configuration = new() { Space = ChromaSpace.Cosine } });
	collectionClient = client.GetCollectionClient(collection);
	return Expect($"{collection.Name} {collection.Space}", collection.Space == ChromaSpace.Cosine, "Cosine");
});
await Check("GetOrCreateCollection", async () => (await client.GetOrCreateCollection(name)).Id.ToString());
await Check("CollectionExists", async () => Expect((await client.CollectionExists(name)).ToString(), await client.CollectionExists(name), "True"));
await Check("ListCollections", async () => (await client.ListCollections()).Count.ToString());
await Check("Add", async () =>
{
	await collectionClient.Add(new ChromaRecords(["a", "b"])
	{
		Embeddings = embeddings,
		Metadatas = [new() { ["text"] = "x", ["int"] = 1, ["long"] = 2L, ["double"] = 1.5, ["float"] = 2.5f, ["bool"] = true }, new() { ["text"] = "y", ["int"] = 2 }],
		Documents = ["first", "second"],
		Uris = ["file://a", null],
	});
	return "2 records";
});
await Check("Every type of single value", async () =>
{
	var guid = Guid.NewGuid();
	var values = new Dictionary<string, object>
	{
		["string"] = "s", ["int"] = 1, ["long"] = 2L, ["short"] = (short)3, ["byte"] = (byte)4, ["sbyte"] = (sbyte)5, ["ushort"] = (ushort)6,
		["uint"] = 7u, ["ulong"] = 8ul, ["double"] = 1.5, ["float"] = 2.5f, ["decimal"] = 3.5m, ["bool"] = true,
		["dateTime"] = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc), ["dateTimeOffset"] = new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.FromHours(2)),
		["guid"] = guid,
	};
	await collectionClient.Add(new ChromaRecords(["v"]) { Embeddings = [new([0f, 0f, 0.5f])], Metadatas = [values] });
	var where = ChromaWhereOperator.Equal("short", (short)3) & ChromaWhereOperator.Equal("ulong", 8ul) & ChromaWhereOperator.Equal("decimal", 3.5m)
		& ChromaWhereOperator.In("guid", guid.ToString()) & ChromaWhereOperator.Equal("sbyte", (sbyte)5);
	var exact = client.WithMetadataValues(ChromaMetadataValues.Exact).GetCollectionClient(collectionClient.Collection);
	var result = await exact.Get(where: where, include: ChromaGetInclude.Metadatas);
	var metadata = result.FirstOrDefault()?.Metadata;
	await collectionClient.Delete(["v"]);
	return Expect($"{result.Count} record, {metadata?.Count} values, guid {metadata?["guid"]}", result.Count == 1 && metadata?.Count == values.Count && Equals(metadata["guid"], guid.ToString()), $"1 record, {values.Count} values");
});
await Check("Count", async () => Expect((await collectionClient.Count()).ToString(), await collectionClient.Count() == 2, "2"));
await Check("Get with filters", async () =>
{
	var where = ChromaWhereOperator.Equal("text", "x") & (ChromaWhereOperator.In("int", 1, 3) | ChromaWhereOperator.GreaterThan("double", 1.0));
	var result = await collectionClient.Get(where: where, whereDocument: ChromaWhereDocumentOperator.Contains("fir"), include: ChromaGetInclude.Metadatas | ChromaGetInclude.Documents | ChromaGetInclude.Embeddings);
	return Expect($"{string.Join(",", result.Select(x => x.Id))} {result.FirstOrDefault()?.Metadata?["bool"]}", result.Count == 1 && result[0].Id == "a", "a");
});
await Check("Query", async () =>
{
	var result = await collectionClient.Query(new ChromaQuery([new([1f, 0.1f, 0f])]) { NResults = 2, Include = ChromaQueryInclude.Distances | ChromaQueryInclude.Documents });
	return Expect($"{result[0][0].Id} {result[0][0].Distance}", result[0][0].Id == "a", "a first");
});
await Check("Exact metadata", async () =>
{
	var exact = client.WithMetadataValues(ChromaMetadataValues.Exact).GetCollectionClient(collectionClient.Collection);
	await exact.Upsert(new ChromaRecords(["c"]) { Embeddings = [new([0f, 0f, 1f])], Metadatas = [new() { ["date"] = "2026-10-04" }] });
	var value = (await exact.Get("c", include: ChromaGetInclude.Metadatas))?.Metadata?["date"];
	return Expect($"{value?.GetType().Name} {value}", value is string, "a string");
});
await Check("Lists in metadata", async () =>
{
	// A list of each type the client knows without reflection.
	var lists = new Dictionary<string, object>
	{
		["tags"] = new List<string> { "red", "blue" }, ["strings"] = new[] { "x" },
		["ints"] = new[] { 1, 2 }, ["intList"] = new List<int> { 1 }, ["longs"] = new[] { 1L }, ["longList"] = new List<long> { 1L },
		["shorts"] = new short[] { 1 }, ["shortList"] = new List<short> { 1 }, ["sbytes"] = new sbyte[] { 1 }, ["sbyteList"] = new List<sbyte> { 1 },
		["ushorts"] = new ushort[] { 1 }, ["ushortList"] = new List<ushort> { 1 }, ["uints"] = new uint[] { 1 }, ["uintList"] = new List<uint> { 1 },
		["ulongs"] = new ulong[] { 1 }, ["ulongList"] = new List<ulong> { 1 }, ["byteList"] = new List<byte> { 1 },
		["doubles"] = new[] { 1.5 }, ["doubleList"] = new List<double> { 1.5 }, ["floats"] = new[] { 2.5f }, ["floatList"] = new List<float> { 2.5f },
		["decimals"] = new[] { 3.5m }, ["decimalList"] = new List<decimal> { 3.5m },
		["bools"] = new[] { true }, ["boolList"] = new List<bool> { false },
		["objects"] = new List<object> { "a", "b" }, ["objectArray"] = new object[] { 1, 2 },
	};
	var records = new ChromaRecords(["l"]) { Embeddings = [new([0.5f, 0.5f, 0f])], Metadatas = [lists] };
	if (!listsStored)
	{
		try
		{
			await collectionClient.Add(records);
			return "stored";
		}
		catch (ChromaException ex)
		{
			return $"rejected on Chroma {version}: {ex.Message}";
		}
	}
	await collectionClient.Add(records);
	var result = await client.WithMetadataValues(ChromaMetadataValues.Exact).GetCollectionClient(collectionClient.Collection).Get(where: ChromaWhereOperator.Contains("tags", "red"), include: ChromaGetInclude.Metadatas);
	var metadata = result.FirstOrDefault()?.Metadata;
	return Expect($"{result.Count} records, {metadata?.Count} lists", result.Count == 1 && metadata?.Count == lists.Count && metadata.Values.All(x => x is List<object>), $"1 record, {lists.Count} lists");
});
await Check("Update and Delete", async () =>
{
	await collectionClient.Update(["a"], documents: ["first updated"]);
	await collectionClient.Delete(["b"]);
	return (await collectionClient.Peek()).Count.ToString();
});
await Check("Modify", async () =>
{
	await collectionClient.Modify(metadata: new() { ["owner"] = "trimming2" });
	return (await client.GetCollection(name)).Metadata?["owner"]?.ToString() ?? "null";
});
await Check("Filters as JSON", async () =>
{
	await Task.CompletedTask;
	return Expect(ChromaWhereOperator.NotEqual("k", 1).ToString() + ChromaWhereDocumentOperator.NotContains("x").ToString(), true, "");
});
await Check("Missing collection", async () =>
{
	try
	{
		await client.GetCollection(name + "missing");
		throw new Exception("no exception");
	}
	catch (ChromaException ex)
	{
		return $"{ex.StatusCode}: {ex.Message}";
	}
});
await Check("Tenants and databases", async () =>
{
	// Chroma 0.4.14 and earlier have no tenants and databases.
	if (Version.TryParse(version, out var parsed) && parsed < new Version(0, 4, 15))
		return "skipped";
	var tenant = $"tenant{Guid.NewGuid():N}";
	await client.CreateTenant(tenant);
	await client.CreateDatabase("database", tenant: tenant);
	return $"{(await client.GetTenant(tenant)).Name}/{(await client.GetDatabase("database", tenant: tenant)).Name}";
});
await Check("DependencyInjection", async () =>
{
	var services = new ServiceCollection();
	services.AddChromaClient(_ => options);
	services.AddKeyedChromaClient("k", _ => options);
	using var provider = services.BuildServiceProvider();
	await provider.GetRequiredKeyedService<ChromaClient>("k").Heartbeat();
	return (await provider.GetRequiredService<ChromaClient>().Heartbeat()).NanosecondHeartbeat.ToString();
});
await Check("DeleteCollection", async () =>
{
	await client.DeleteCollection(name);
	return Expect((await client.CollectionExists(name)).ToString(), !await client.CollectionExists(name), "False");
});

Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED");
return failures;
