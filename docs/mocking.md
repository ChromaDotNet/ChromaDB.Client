# Mocks in tests

`ChromaClient` and `ChromaCollectionClient` can be mocked, as in the Azure SDKs. Their members are virtual, and a protected constructor creates a client without a server, for a subclass or a mocking library. Only the members it overrides work.

```csharp
sealed class FakeClient : ChromaClient
{
	public override Task<ChromaCollection> GetCollectionAsync(string name, string? tenant = null, string? database = null, CancellationToken cancellationToken = default)
		=> Task.FromResult(new ChromaCollection(name));
}
```
