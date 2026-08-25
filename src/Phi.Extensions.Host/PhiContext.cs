using Phi.Agent;

namespace Phi.Extensions.Host;

/// <summary>
/// Host-side implementation of <see cref="IPhiContext"/>. Read-only projection
/// of the session + the UI bridge. Extensions only see this through
/// <see cref="IPhiApi.Context"/>; the underlying <c>Phi.Session</c> is
/// never exposed directly to extensions.
/// </summary>
internal sealed class PhiContext : IPhiContext
{
    private readonly ISession _session;
    private readonly IPhiUiBridge _uiBridge;

    public PhiContext(ISession session, IPhiUiBridge uiBridge)
    {
        _session = session;
        _uiBridge = uiBridge;
    }

    public string Cwd => _session.Cwd;
    public string Model => _session.State.Model;
    public string ProviderName => _session.State.ProviderName;
    public string SessionId => _session.Id;
    public string SystemPrompt => _session.SystemPrompt;
    public bool IsRunning => _session.State.IsRunning;
    public bool HasUi => _session.HasUi;

    public IReadOnlyList<IAgentMessage> Transcript => _session.State.Messages;

    public IPhiUiBridge Ui => _uiBridge;
}
