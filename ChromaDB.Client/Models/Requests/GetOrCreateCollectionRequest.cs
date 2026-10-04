namespace ChromaDB.Client.Models.Requests;

internal class GetOrCreateCollectionRequest : GetOrCreateCollectionRequestBase
{
	public override bool GetOrCreate { get; } = true;
}
