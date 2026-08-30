using System.Runtime.CompilerServices;
using System.Text;

namespace MobiusEditor
{
    internal static class CoreInit
    {
        /// <summary>Classic game files use code page 437, which .NET only provides through the code-pages package.</summary>
        [ModuleInitializer]
        internal static void RegisterEncodings() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }
}
