using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models;

// A function of Chroma Cloud attached to a collection, which writes its results to an output collection.
// AttachFunction fills Id, Name and FunctionName; GetAttachedFunction fills the rest too.
public class ChromaAttachedFunction
{
	[JsonPropertyName("id")]
	public Guid Id { get; init; }

	[JsonPropertyName("name")]
	public string Name { get; init; } = null!;

	// Like ChromaFunctions.Statistics.
	[JsonPropertyName("function_name")]
	public string FunctionName { get; init; } = null!;

	[JsonPropertyName("input_collection_id")]
	public Guid? InputCollectionId { get; init; }

	[JsonPropertyName("output_collection")]
	public string? OutputCollection { get; init; }

	// Null until Chroma creates the output collection.
	[JsonPropertyName("output_collection_id")]
	public Guid? OutputCollectionId { get; init; }

	[JsonPropertyName("tenant_id")]
	public string? Tenant { get; init; }

	[JsonPropertyName("database_id")]
	public string? Database { get; init; }

	// The parameters of the function as JSON.
	[JsonPropertyName("params")]
	public string? Params { get; init; }

	// The position in the log of the collection up to which the function has run.
	[JsonPropertyName("completion_offset")]
	public long? CompletionOffset { get; init; }

	// How many new records start the function again.
	[JsonPropertyName("min_records_for_invocation")]
	public long? MinRecordsForInvocation { get; init; }
}
