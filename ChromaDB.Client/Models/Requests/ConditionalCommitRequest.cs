using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models.Requests;

// The commit of a conditional transaction: the read token and the ids of its reads, and its writes in their order.
internal class ConditionalCommitRequest
{
	[JsonPropertyName("read_token")]
	public long? ReadToken { get; init; }

	[JsonPropertyName("read_ids")]
	public required IReadOnlyList<string> ReadIds { get; init; }

	[JsonPropertyName("operations")]
	public required IReadOnlyList<ConditionalOperation> Operations { get; init; }
}

// A write of a conditional transaction: "add", "update", "upsert" or "delete", with the request that the write sends outside a
// transaction, serialized when the transaction takes the write.
internal class ConditionalOperation
{
	[JsonPropertyName("operation")]
	public required string Operation { get; init; }

	[JsonPropertyName("payload")]
	public required JsonElement Payload { get; init; }
}
