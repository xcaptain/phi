namespace Phi.Extensions;

/// <summary>
/// Identifies a class as a Phi extension entry point. The class must
/// implement <see cref="IPhiExtension"/>; the attribute is what the loader
/// uses to discover the extension's name and version without instantiating
/// every type in the assembly (avoids running unrelated static
/// initializers, attribute reflection is faster than full instantiation).
/// <para>
/// One assembly may declare at most one <see cref="PhiExtensionAttribute"/>
/// in v1. Multiple-entrypoint assemblies land in v2 alongside
/// manifest-driven discovery.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class PhiExtensionAttribute : Attribute
{
    /// <summary>
    /// Stable identifier used in <c>~/.phi/config.json</c>'s disable list, in
    /// audit logs, and in slash command name resolution
    /// (<c>mcp__&lt;server-key&gt;__...</c> etc.). Must be unique across all
    /// installed extensions. Convention: <c>kebab-case</c>.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>SemVer string (e.g. <c>"1.2.0"</c>). Defaults to <c>"0.0.0"</c>.</summary>
    public string Version { get; init; } = "0.0.0";

    /// <summary>One-line description shown in <c>/extensions</c> listing.</summary>
    public string Description { get; init; } = "";

    /// <summary>
    /// Capabilities the extension declares it needs. <see cref="IPhiApi"/>
    /// methods that match these capabilities may be called by the extension;
    /// other methods can still be invoked but the audit log records the
    /// capability mismatch. v1.5+ enforces strict mode (excess calls throw
    /// <see cref="ExtensionError"/>).
    /// </summary>
    public ExtensionCapability Capabilities { get; init; } = ExtensionCapability.None;
}
