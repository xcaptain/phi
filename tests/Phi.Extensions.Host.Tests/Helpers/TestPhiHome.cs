namespace Phi.Extensions.Host.Tests.Helpers;

/// <summary>
/// Per-fixture sandbox for <see cref="SessionPaths.PhiHome"/> so unit
/// tests in <c>Phi.Extensions.Host.Tests</c> don't pollute the user's
/// real <c>~/.phi</c> with <c>phi-stubs-*</c> / <c>phi-gate-*</c> etc.
/// when they call <c>Session.LoadAsync</c> against a temp cwd.
/// <para>
/// Usage in a fixture:
/// <code>
/// public class MyTests : IDisposable
/// {
///     private readonly TestPhiHome.Scope _phiHome = new();
///     public void Dispose() => _phiHome.Dispose();
/// }
/// </code>
/// The scope sets <see cref="SessionPaths.PhiHome"/> to a fresh
/// <c>Path.GetTempPath()/phi-home-{guid}</c> on construction and
/// restores the previous value (typically <c>~/.phi</c>) on disposal.
/// </para>
/// </summary>
internal static class TestPhiHome
{
    internal sealed class Scope : IDisposable
    {
        private readonly string _previous;
        public string PhiHome { get; }

        public Scope()
        {
            _previous = SessionPaths.PhiHome;
            PhiHome = Path.Combine(Path.GetTempPath(), "phi-home-" + Guid.NewGuid().ToString("N"));
            SessionPaths.PhiHome = PhiHome;
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            SessionPaths.PhiHome = _previous;
            if (Directory.Exists(PhiHome)) Directory.Delete(PhiHome, recursive: true);
        }
    }
}
