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

public sealed partial class BashTool : TypedTool<BashArgs>
{
    public override string Name => "bash";
    public override string Description => "Run a shell command and return stdout/stderr/exit code.";

    public override async Task<ToolResult> ExecuteTypedAsync(BashArgs args, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo("/bin/bash", ["-c", args.Command])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

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