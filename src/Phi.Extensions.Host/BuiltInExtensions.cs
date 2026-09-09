using System.Reflection;
using Phi.Extensions.CodingPack;

namespace Phi.Extensions.Host;

/// <summary>
/// The single, version-controlled registry of extensions that the host
/// always loads — regardless of which composition root (Phi.Tui,
/// Phi.Avalonia.Desktop, a future headless harness, ...) builds the
/// runtime.
/// <para>
/// Both Phi.Tui and Phi.Avalonia.Desktop call
/// <see cref="RegisterAll"/> from their composition root so the default
/// coding capability is identical across frontends. A future built-in
/// (e.g. a metrics pack) appends one line here; the contract is that it
/// must be a compile-time extension (one whose assembly is referenced
/// by <c>Phi.Extensions.Host</c> via ProjectReference) and that its
/// <c>[PhiExtension]</c> name is stable.
/// </para>
/// <para>
/// The host package takes the ProjectReference on CodingPack (rather
/// than each frontend doing it) so AOT trimming decisions for
/// Avalonia/desktop publish have one decision-maker: anything in this
/// list must keep its tool / command / guideline registrations
/// AOT-visible, and that contract is enforced by
/// <c>BuiltInExtensionsTests</c>.
/// </para>
/// </summary>
internal static class BuiltInExtensions
{
    /// <summary>
    /// Registers every built-in extension on <paramref name="runtime"/>.
    /// Cheap and side-effect free until <see cref="ExtensionRuntime.Initialize"/>
    /// runs each extension's <c>Setup</c> — call this immediately before
    /// (or as the first step of) initializing the runtime.
    /// <para>
    /// Idempotent: a built-in that is already registered (matched by its
    /// <c>[PhiExtension(Name = ...)]</c>) is skipped on subsequent calls.
    /// This is the property the composition root relies on when it calls
    /// <c>RegisterAll</c> from both the sync and async
    /// <see cref="Phi.SessionEnvironment"/> factories — both can fire
    /// during a <c>/reload</c> cycle, and double-registering would leave
    /// a duplicate <see cref="ExtensionRuntime.Extensions"/> entry that
    /// makes tools / commands show up twice in the audit log.
    /// </para>
    /// </summary>
    public static void RegisterAll(ExtensionRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        RegisterIfMissing(runtime, new CodingPackExt());
    }

    private static void RegisterIfMissing(ExtensionRuntime runtime, IPhiExtension instance)
    {
        var name = instance.GetType().GetCustomAttributes<PhiExtensionAttribute>().FirstOrDefault()?.Name
            ?? throw new InvalidOperationException(
                $"compile-time extension '{instance.GetType().FullName}' has no [PhiExtension] attribute");

        if (runtime.Extensions.Any(e => e.Name == name))
            return;

        runtime.RegisterCompiledExtension(instance);
    }
}
