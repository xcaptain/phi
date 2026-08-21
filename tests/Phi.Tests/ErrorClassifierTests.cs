using Phi.Status;

namespace Phi.Tests;

public class ErrorClassifierTests
{
    [Test]
    public async Task LooksTransient_NetworkTimeout_IsTransient()
    {
        await Assert.That(ErrorClassifier.LooksTransient("Connection timed out after 30s")).IsTrue();
    }

    [Test]
    public async Task LooksTransient_RateLimit_IsTransient()
    {
        await Assert.That(ErrorClassifier.LooksTransient("HTTP 429 rate limit exceeded")).IsTrue();
    }

    [Test]
    public async Task LooksTransient_ServerError_IsTransient()
    {
        await Assert.That(ErrorClassifier.LooksTransient("Upstream 502 Bad Gateway")).IsTrue();
        await Assert.That(ErrorClassifier.LooksTransient("503 Service Unavailable")).IsTrue();
        await Assert.That(ErrorClassifier.LooksTransient("504 Gateway Timeout")).IsTrue();
    }

    [Test]
    public async Task LooksTransient_ConnectionReset_IsTransient()
    {
        await Assert.That(ErrorClassifier.LooksTransient("Connection reset by peer")).IsTrue();
    }

    [Test]
    public async Task LooksTransient_Overloaded_IsTransient()
    {
        await Assert.That(ErrorClassifier.LooksTransient("Model is overloaded, try again later")).IsTrue();
    }

    [Test]
    public async Task LooksTransient_Throttled_IsTransient()
    {
        await Assert.That(ErrorClassifier.LooksTransient("Request throttled")).IsTrue();
    }

    [Test]
    public async Task LooksTransient_AuthFailure_IsPersistent()
    {
        await Assert.That(ErrorClassifier.LooksTransient("401 Unauthorized: invalid API key")).IsFalse();
        await Assert.That(ErrorClassifier.LooksTransient("Authentication failed")).IsFalse();
    }

    [Test]
    public async Task LooksTransient_ModelNotFound_IsPersistent()
    {
        await Assert.That(ErrorClassifier.LooksTransient("Model 'phi-99' not found")).IsFalse();
    }

    [Test]
    public async Task LooksTransient_BadConfig_IsPersistent()
    {
        await Assert.That(ErrorClassifier.LooksTransient("Missing required config: ANTHROPIC_API_KEY")).IsFalse();
    }

    [Test]
    public async Task LooksTransient_EmptyString_IsNotTransient()
    {
        await Assert.That(ErrorClassifier.LooksTransient("")).IsFalse();
    }

    [Test]
    public async Task LooksTransient_NullString_IsNotTransient()
    {
        await Assert.That(ErrorClassifier.LooksTransient(null!)).IsFalse();
    }

    [Test]
    public async Task LooksTransient_MatchIsCaseInsensitive()
    {
        await Assert.That(ErrorClassifier.LooksTransient("TIMEOUT occurred")).IsTrue();
        await Assert.That(ErrorClassifier.LooksTransient("Rate Limit")).IsTrue();
    }
}
