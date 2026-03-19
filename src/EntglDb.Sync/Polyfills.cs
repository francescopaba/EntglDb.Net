#if NETSTANDARD2_1

// Polyfill required to use record types and init-only setters (C# 9+) when targeting netstandard2.1.
// This type ships in-box starting with .NET 5; .NET Standard is not receiving new versions,
// so we define it here as internal so the compiler can find it at build time.

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}

#endif
