using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models;

/// <summary>
/// A function of Chroma Cloud attached to a collection, which writes its results to an output collection.
/// <c>AttachFunction</c> fills <c>Id</c>, <c>Name</c> and <c>FunctionName</c>; <c>GetAttachedFunction</c> fills the rest too.
/// </summary>
public class ChromaAttachedFunction
{
	/// <summary>
	/// The id of the attached function.
	/// </summary>
	[JsonPropertyName("id")]
	public Guid Id { get; init; }

	/// <summary>
	/// The name the function is attached under.
	/// </summary>
	[JsonPropertyName("name")]
	public string Name { get; init; } = null!;

	/// <summary>
	/// The name of the function, like <c>ChromaFunctions.Statistics</c>.
	/// </summary>
	[JsonPropertyName("function_name")]
	public string FunctionName { get; init; } = null!;

	/// <summary>
	/// The id of the collection the function is attached to.
	/// </summary>
	[JsonPropertyName("input_collection_id")]
	public Guid? InputCollectionId { get; init; }

	/// <summary>
	/// The name of the output collection.
	/// </summary>
	[JsonPropertyName("output_collection")]
	public string? OutputCollection { get; init; }

	/// <summary>
	/// The id of the output collection. Null until Chroma creates the output collection.
	/// </summary>
	[JsonPropertyName("output_collection_id")]
	public Guid? OutputCollectionId { get; init; }

	/// <summary>
	/// The tenant, from <c>tenant_id</c> in the answer of the server.
	/// </summary>
	[JsonPropertyName("tenant_id")]
	public string? Tenant { get; init; }

	/// <summary>
	/// The database, from <c>database_id</c> in the answer of the server.
	/// </summary>
	[JsonPropertyName("database_id")]
	public string? Database { get; init; }

	/// <summary>
	/// The parameters of the function as JSON.
	/// </summary>
	[JsonPropertyName("params")]
	public string? Params { get; init; }

	/// <summary>
	/// The position in the log of the collection up to which the function has run.
	/// </summary>
	[JsonPropertyName("completion_offset")]
	public long? CompletionOffset { get; init; }

	/// <summary>
	/// How many new records start the function again.
	/// </summary>
	[JsonPropertyName("min_records_for_invocation")]
	public long? MinRecordsForInvocation { get; init; }
}
