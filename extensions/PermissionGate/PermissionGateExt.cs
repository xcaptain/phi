using System.Text.RegularExpressions;
using Phi.Extensions.Events;

namespace Phi.Extensions.PermissionGate;

/// <summary>
/// Sprint 2 reference extension: blocks dangerous bash commands before
/// they run via the <c>tool_call</c> hook. Demonstrates the interception
/// pipeline end-to-end:
/// <list type="bullet">
/// <item>Register <c>On("tool_call", handler)</c></item>
/// <item>Handler inspects the <see cref="ToolCallHookEvent.Arguments"/> for
/// <c>command</c>, sets <see cref="ToolCallHookEvent.Result"/> to
/// <c>Block = true</c> for guarded patterns</item>
/// <item>Blocked call returns a <c>ToolResult.IsError = true</c> with the
/// reason, so the model knows why and can avoid retrying the same
/// command</item>
/// </list>
/// </summary>
[PhiExtension(
    Name = "permission-gate",
    Version = "1.0.0",
    Description = "Block dangerous bash commands before they run.",
    Capabilities = ExtensionCapability.None)]
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
        api.On("tool_call", (ev, _) =>
        {
            if (ev is not ToolCallHookEvent tce || tce.ToolName != "bash")
                return;

            var cmd = tce.Arguments["command"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(cmd)) return;

            foreach (var pattern in Dangerous)
            {
                if (!pattern.IsMatch(cmd)) continue;
                tce.Result = new ToolCallHookResult
                {
                    Block = true,
                    Reason =
                        $"command matches guarded pattern `{pattern}`; " +
                        "ask the user to run it manually if they really meant it",
                };
                return;
            }
        });
    }
}
