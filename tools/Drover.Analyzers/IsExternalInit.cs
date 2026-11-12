// netstandard2.0 has no IsExternalInit, which the compiler needs for records and init-only members.
namespace System.Runtime.CompilerServices;

internal static class IsExternalInit;
