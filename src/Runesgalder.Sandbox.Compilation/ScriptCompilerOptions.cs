namespace Runesgalder.Sandbox.Compilation;

/// <summary>Options for compiling generated analysis scripts.</summary>
public sealed record ScriptCompilerOptions
{
    /// <summary>The default options.</summary>
    public static ScriptCompilerOptions Default { get; } = new();

    /// <summary>The maximum accepted source size in bytes.</summary>
    public int MaxSourceBytes { get; init; } = 256 * 1024;

    /// <summary>
    /// Namespaces that generated code must not use. The check is a defence-in-depth signal on
    /// top of the reference whitelist.
    /// </summary>
    public IReadOnlyList<string> BannedNamespaces { get; init; } =
    [
        "System.IO",
        "System.Net",
        "System.Reflection",
        "System.Diagnostics",
        "System.Runtime.InteropServices",
        "System.Runtime.Loader",
        "System.Management",
        "Microsoft.Win32"
    ];

    /// <summary>The simple names of the framework assemblies available to generated code.</summary>
    public IReadOnlyList<string> ReferenceAssemblyNames { get; init; } =
    [
        "System.Private.CoreLib",
        "System.Runtime",
        "System.Collections",
        "System.Linq",
        "System.Memory",
        "System.Threading",
        "System.Threading.Tasks"
    ];
}
