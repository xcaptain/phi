using System.ComponentModel;
using System.Diagnostics;
using PhiAgent;
using PhiCoding.Tools.Details;

namespace PhiCoding.Tools;

public sealed record BashArgs
{
    [Description("Shell command to execute")]
    public required string Command { get; init; }
}

/// <summary>
/// Creates a bash tool bound to <paramref name="cwd"/>. Commands execute
/// with that directory as their working directory so relative paths
/// resolve against the session root, not the process cwd.
/// </summary>
public sealed partial class BashTool(string cwd) : TypedTool<BashArgs>
{
    private readonly string? _cwd = cwd;

    public override string Name => "bash";
    public override string Description => "Run a shell command and return stdout/stderr/exit code.";

    /// <summary>
    /// Creates a bash tool that inherits the process working directory. Kept
    /// for backward compatibility with code paths that have not been
    /// migrated to a session-cwd model yet.
    /// </summary>
    public BashTool() : this(Environment.CurrentDirectory) { }

    public override async Task<ToolResult> ExecuteTypedAsync(BashArgs args, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo("/bin/bash", ["-c", args.Command])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (_cwd is not null)
            psi.WorkingDirectory = _cwd;

        using var process = Process.Start(psi)!;
        var stopwatch = Stopwatch.StartNew();
        await process.WaitForExitAsync(cancellationToken);
        stopwatch.Stop();

        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);

        var details = new BashDetails(
            Command: args.Command,
            ExitCode: process.ExitCode,
            DurationMs: stopwatch.ElapsedMilliseconds,
            Stdout: stdout,
            Stderr: stderr);

        var content = new List<ContentBlock>();
        if (!string.IsNullOrEmpty(stdout))
            content.Add(new TextBlock(stdout));
        if (!string.IsNullOrEmpty(stderr))
            content.Add(new TextBlock(stderr));
        if (content.Count == 0)
            content.Add(new TextBlock($"<no output, exit={process.ExitCode}>"));

        return new ToolResult(content, Details: ToolDetails.Node(details), IsError: process.ExitCode != 0);
    }
}
