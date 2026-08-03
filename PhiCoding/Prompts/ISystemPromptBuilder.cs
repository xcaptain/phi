namespace PhiCoding.Prompts;

/// <summary>
/// Pure-function builder that turns a <see cref="SystemPromptBuildContext"/>
/// into the final system-prompt string sent to the provider.
/// </summary>
public interface ISystemPromptBuilder
{
    string Build(SystemPromptBuildContext context);
}
