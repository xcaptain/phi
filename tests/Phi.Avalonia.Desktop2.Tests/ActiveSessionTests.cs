using Phi.Avalonia.Desktop2;

namespace Phi.Avalonia.Desktop2.Tests;

/// <summary>
/// <see cref="ActiveSession"/>: the ISession holder the shell binds
/// to. Pure holder — no session construction logic — but the swap and
/// clear flows are the contract every page subscribes to.
/// </summary>
public class ActiveSessionTests
{
    [Test]
    public async Task Current_IsNullOnConstructionWithoutInitial()
    {
        var holder = new ActiveSession();
        await Assert.That(holder.Current).IsNull();
    }

    [Test]
    public async Task Current_HoldsInitialWhenConstructedWithOne()
    {
        var session = new FakeSession { Id = "s-1" };
        var holder = new ActiveSession(session);

        await Assert.That(holder.Current).IsSameReferenceAs(session);
    }

    [Test]
    public async Task Replace_SwapsCurrentAndFiresChanged()
    {
        var first = new FakeSession { Id = "s-1" };
        var second = new FakeSession { Id = "s-2" };
        var holder = new ActiveSession(first);

        var fired = 0;
        holder.Changed += () => fired++;

        holder.Replace(second);

        await Assert.That(holder.Current).IsSameReferenceAs(second);
        await Assert.That(fired).IsEqualTo(1);
    }

    [Test]
    public async Task Replace_WithSameInstance_DoesNotFireChanged()
    {
        var session = new FakeSession { Id = "s-1" };
        var holder = new ActiveSession(session);

        var fired = 0;
        holder.Changed += () => fired++;

        holder.Replace(session);

        await Assert.That(fired).IsEqualTo(0);
    }

    [Test]
    public async Task Clear_NullsCurrentAndFiresChanged()
    {
        var session = new FakeSession { Id = "s-1" };
        var holder = new ActiveSession(session);

        var fired = 0;
        holder.Changed += () => fired++;

        holder.Clear();

        await Assert.That(holder.Current).IsNull();
        await Assert.That(fired).IsEqualTo(1);
    }

    [Test]
    public async Task Clear_DisposesOutgoingSession()
    {
        // Sidebar New Chat on an existing chat must release the
        // outgoing ISession's CTS, provider transport, and extension
        // runtime. Clear owns the lifecycle so the caller (which only
        // sees the holder, not the session) doesn't have to pair a
        // Dispose call with every navigation.
        var session = new FakeSession { Id = "s-1" };
        var holder = new ActiveSession(session);

        holder.Clear();

        await Assert.That(session.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task Clear_WhenAlreadyEmpty_DoesNotFire()
    {
        var holder = new ActiveSession();

        var fired = 0;
        holder.Changed += () => fired++;

        holder.Clear();

        await Assert.That(fired).IsEqualTo(0);
    }
}