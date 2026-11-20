#if NET
using System.Runtime.CompilerServices;

// A library compiled for .NET Standard calls the init setters of this package with the IsExternalInit of the .NET Standard build,
// and at run time gets the .NET build, whose setters have the one of the runtime: this makes the two the same type. The forward
// adds no type to the API of the package, and the .NET Standard build has no forward: the public API files stay the same for both.
#pragma warning disable RS0016
[assembly: TypeForwardedTo(typeof(IsExternalInit))]
#pragma warning restore RS0016
#endif
