using System.Runtime.CompilerServices;

// The tests reach internal parts: ChromaHttpClient.ServerFacts.Lifetime, ChromaNumbers. The framework tests run some of them on .NET Framework.
[assembly: InternalsVisibleTo("ChromaDB.Client.Tests")]
[assembly: InternalsVisibleTo("ChromaDB.Client.FrameworkTests")]
