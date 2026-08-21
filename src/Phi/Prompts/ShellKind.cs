namespace Phi.Prompts;

/// <summary>
/// The shell the <c>bash</c> tool actually executes, so the system prompt
/// can tell the model which syntax to emit. Desktop Windows uses PowerShell
/// (pwsh 7 preferred, Windows PowerShell 5.1 fallback); WSL, macOS and
/// Linux use bash — inside WSL the OS is Linux, so it is never classified
/// as PowerShell.
/// </summary>
public enum ShellKind
{
    Bash,
    PowerShell,
}
