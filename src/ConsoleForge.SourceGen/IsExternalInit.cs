// Polyfill: 'record' init-only setters require this type, absent in netstandard2.0
#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    /// <summary>Polyfill for init-only setter support on netstandard2.0.</summary>
    internal static class IsExternalInit { }
}
#endif
