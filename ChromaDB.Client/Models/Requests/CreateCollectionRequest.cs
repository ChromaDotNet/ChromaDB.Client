namespace ChromaDB.Client.Models.Requests;

internal class CreateCollectionRequest : GetOrCreateCollectionRequestBase
{
	public override bool GetOrCreate { get; } = false;
}
