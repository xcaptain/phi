using Phi.Providers;

namespace Phi.Extensions.Host;

/// <summary>
/// Sprint 3b Project Trust foundation: gates project-level extension
/// assemblies (discovered under <c>{cwd}/.phi/extensions/</c>) behind a
/// user confirm dialog, and remembers the decision in
/// <see cref="ProjectTrustStore"/> so the user isn't re-prompted every
/// session.
/// <para>
/// Modes:
/// <list type="bullet">
/// <item><b>Headless</b> (<c>ui.HasUi == false</c>): auto-approves the
/// whole set without prompting. CI / unit tests never block on
/// dialogs; the trust store is still written so a later interactive
/// session picks up the same decision.</item>
/// <item><b>Interactive</b>: looks up the cwd key in the store. If
/// there's a remembered decision, applies it (skipping declined
/// extensions). Otherwise calls
/// <see cref="IPhiUiBridge.ShowConfirmAsync"/> with a "do you trust
/// these N extensions?" prompt; the user's answer (and the
/// "remember" toggle) writes back to the store.</item>
/// </list>
/// </para>
/// <para>
/// Both modes also write a <see cref="AuditEvent.ProjectTrust"/> entry
/// to <c>~/.phi/audit.log</c> so the host can review trust decisions
/// out-of-band.
/// </para>
/// </summary>
internal static class ProjectTrustGate
{
    /// <summary>
    /// Filter <paramref name="assemblyPaths"/> against the project's
    /// trust decision, asking the user via
    /// <paramref name="uiBridge"/> if no prior decision exists. Returns
    /// the subset the user is willing to load (empty when declined).
    /// </summary>
    public static async Task<IReadOnlyList<string>> GateAsync(
        string cwd,
        IReadOnlyList<string> assemblyPaths,
        IPhiUiBridge uiBridge,
        IProjectTrustStore? store = null)
    {
        ArgumentNullException.ThrowIfNull(assemblyPaths);
        ArgumentNullException.ThrowIfNull(uiBridge);

        store ??= new ProjectTrustStoreAdapter(ProjectTrustStore.Load());
        var cwdKey = ProjectExtensions.ProjectKey(cwd);

        if (assemblyPaths.Count == 0) return [];

        // Headless: auto-approve (no dialogs in CI).
        if (!uiBridge.HasUi)
        {
            var decision = new ProjectTrustDecision(
                ProjectTrustKind.Approve,
                DateTimeOffset.UtcNow,
                ExtractNames(assemblyPaths),
                Remember: true);
            store.Set(cwdKey, decision);
            store.Save();
            AuditLogger.Write(AuditEvent.ProjectTrust(cwd, "approve-headless", true,
                ExtractNames(assemblyPaths)));
            return assemblyPaths;
        }

        // Interactive: check prior decision.
        var existing = store.Lookup(cwdKey);
        if (existing is not null)
        {
            AuditLogger.Write(AuditEvent.ProjectTrust(cwd,
                existing.Kind == ProjectTrustKind.Approve ? "approve-remembered" : "decline-remembered",
                existing.Remember, existing.ExtensionNames));
            return existing.Kind == ProjectTrustKind.Approve ? assemblyPaths : [];
        }

        // No prior decision — ask. The IPhiUiBridge confirm dialog
        // returns true / false; we record the choice with Remember=true
        // so the next session for the same project doesn't re-prompt.
        var title = "Project extensions";
        var message = assemblyPaths.Count == 1
            ? $"This project wants to load 1 extension:\n  • {assemblyPaths[0]}\n\nTrust it for future sessions in this project?"
            : $"This project wants to load {assemblyPaths.Count} extensions:\n" +
              string.Concat(assemblyPaths.Select(p => $"\n  • {p}")) +
              "\n\nTrust them for future sessions in this project?";
        var timeout = TimeSpan.FromSeconds(30);
        bool approved = await uiBridge.ConfirmAsync(title, message, timeout);

        var newDecision = new ProjectTrustDecision(
            approved ? ProjectTrustKind.Approve : ProjectTrustKind.Decline,
            DateTimeOffset.UtcNow,
            ExtractNames(assemblyPaths),
            Remember: true);
        store.Set(cwdKey, newDecision);
        store.Save();
        AuditLogger.Write(AuditEvent.ProjectTrust(cwd,
            approved ? "approve-confirmed" : "decline-confirmed",
            true, ExtractNames(assemblyPaths)));

        return approved ? assemblyPaths : [];
    }

    private static List<string> ExtractNames(IReadOnlyList<string> assemblyPaths) =>
        assemblyPaths.Select(Path.GetFileName).Where(n => n is not null).Select(n => n!).ToList();

    /// <summary>
    /// Test seam: lets the production code stay non-static while unit
    /// tests inject an in-memory store. The default production adapter
    /// just delegates to <see cref="ProjectTrustStore"/>.
    /// </summary>
    internal interface IProjectTrustStore
    {
        ProjectTrustDecision? Lookup(string cwdKey);
        void Set(string cwdKey, ProjectTrustDecision decision);
        void Save();
    }

    private sealed class ProjectTrustStoreAdapter(ProjectTrustStore inner) : IProjectTrustStore
    {
        public ProjectTrustDecision? Lookup(string cwdKey) => inner.Lookup(cwdKey);
        public void Set(string cwdKey, ProjectTrustDecision decision) =>
            inner.Decisions[cwdKey] = decision;
        public void Save() => inner.Save();
    }
}
