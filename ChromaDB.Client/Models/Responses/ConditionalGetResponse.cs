using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models.Responses;

// The answer to the get of a conditional transaction: the records, with the read token that the later reads and the commit of the
// transaction send. Null when the answer has none, which the transaction rejects.
internal class ConditionalGetResponse : CollectionEntriesGetResponse
{
	[JsonPropertyName("read_token")]
	public ulong? ReadToken { get; init; }
}
