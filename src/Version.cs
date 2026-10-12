// Version de Bip : seule source de verite, compilee dans les deux executables.
// La release GitHub verifie que le tag (vX.Y.Z) correspond a cette valeur.

using System.Reflection;

[assembly: AssemblyTitle("Bip")]
[assembly: AssemblyProduct("Bip — l'encoche Claude Code pour Windows")]
[assembly: AssemblyCopyright("© 2026 CR0ZER — licence MIT")]
[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]
[assembly: AssemblyInformationalVersion("0.1.0")]

namespace ClaudeNotch
{
    static class AppVersion
    {
        public const string Text = "0.1.0";
    }
}
