using System.Text.RegularExpressions;
using Phi.Extensions.Events;

namespace Phi.Extensions.PermissionGate;

/// <summary>
/// Sprint 2 + Sprint 3 reference extension: blocks (or asks permission
/// for) dangerous bash commands before they run via the <c>tool_call</c>
/// hook. Demonstrates the interception pipeline end-to-end:
/// <list type="bullet">
/// <item>Register <c>On("tool_call", handler)</c></item>
/// <item>Handler inspects the <see cref="ToolCallHookEvent.Arguments"/> for
/// <c>command</c>, decides block / allow via <see cref="ToolCallHookEvent.Result"/>.</item>
/// <item>When <c>IPhiContext.Ui.HasUi</c> is <c>true</c> (real TUI /
/// Avalonia host), the user gets a confirm dialog with the matched
/// pattern + the actual command; their answer decides the outcome.</item>
/// <item>When <c>HasUi</c> is <c>false</c> (CI, headless test), the
/// no-op bridge returns <c>false</c> and the gate falls back to
/// auto-blocking — preserving the previous Sprint 2 default.</item>
/// <item>Allowed / blocked outcomes are surfaced via
/// <c>IPhiUiBridge.Notify</c> so the user can see why.</item>
/// </list>
/// </summary>
[PhiExtension(
    Name = "permission-gate",
    Version = "1.0.0",
    Description = "Block dangerous bash commands before they run.",
    Capabilities = ExtensionCapability.UiInteract)]
public sealed partial class PermissionGateExt : IPhiExtension
{
    [GeneratedRegex(@"\brm\s+-(?=[a-zA-Z]*r)(?=[a-zA-Z]*f)[a-zA-Z]+")]
    private static partial Regex RmRecursiveForce();

    [GeneratedRegex(@"\bgit\s+push\s+--force")]
    private static partial Regex GitPushForce();

    [GeneratedRegex(@"\bgit\s+reset\s+--hard")]
    private static partial Regex GitResetHard();

    [GeneratedRegex(@"\bchmod\s+-R\s+777\b")]
    private static partial Regex ChmodRecursive777();

    [GeneratedRegex(@"\bmkfs\b")]
    private static partial Regex Mkfs();

    private static readonly Regex[] Dangerous =
    [
        RmRecursiveForce(),
        GitPushForce(),
        GitResetHard(),
        ChmodRecursive777(),
        Mkfs(),
    ];

    public void Setup(IPhiApi api)
    {
        api.On("tool_call", async (ev, ctx) =>
        {
            if (ev is not ToolCallHookEvent tce || tce.ToolName != "bash")
                return;

            var cmd = tce.Arguments["command"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(cmd)) return;

            foreach (var pattern in Dangerous)
            {
                if (!pattern.IsMatch(cmd)) continue;

                // Sprint 3: instead of silently blocking, ask the user via
                // the UI bridge. The user can approve (gate lets it
                // through), deny (gate blocks as before), or the dialog
                // returns the no-op default (HasUi==false → block) which
                // preserves the previous auto-block behaviour for
                // headless contexts.
                string preview = cmd.Length > 200 ? cmd[..200] + "…" : cmd;
                bool allowed = ctx.Ui.HasUi && await ctx.Ui.ConfirmAsync(
                    title: "Permission Gate",
                    message:
                        $"PermissionGate flagged this command:\n\n  {preview}\n\n" +
                        $"Matched pattern: `{pattern}`\n\nAllow it to run?",
                    timeout: TimeSpan.FromSeconds(30));

                if (allowed)
                {
                    ctx.Ui.Notify($"Permission granted for guarded command.", NotifyLevel.Warning);
                    return;
                }

                tce.Result = new ToolCallHookResult
                {
                    Block = true,
                    Reason =
                        $"command matches guarded pattern `{pattern}`; " +
                        "user denied or no UI available to ask",
                };
                ctx.Ui.Notify(
                    $"Blocked guarded command: {preview}",
                    NotifyLevel.Warning);
                return;
            }
        });
    }
}
