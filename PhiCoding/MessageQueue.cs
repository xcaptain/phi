using PhiAgent;

namespace PhiCoding;

/// <summary>
/// Thread-safe FIFO holding user-submitted messages that arrive while the
/// harness is busy. Mirrors tau's <c>steering_queue</c> + <c>follow_up_queue</c>.
/// <list type="bullet">
/// <item>Steering: injected at the next turn boundary — used for "wait,
///   redirect this" prompts that should influence the very next turn.</item>
/// <item>Follow-up: also injected at turn boundaries, but treated as
///   independent tasks to be appended after the current direction lands.</item>
/// </list>
/// Both queues are drained by <c>Loop.RunAgentAsync</c> via callbacks at
/// turn boundaries; the queue itself has no opinion on turn scheduling.
/// </summary>
public sealed class MessageQueue
{
    private readonly object _lock = new();
    private readonly Queue<UserMessage> _steering = new();
    private readonly Queue<UserMessage> _followUp = new();

    public int SteeringCount
    {
        get { lock (_lock) return _steering.Count; }
    }

    public int FollowUpCount
    {
        get { lock (_lock) return _followUp.Count; }
    }

    public void EnqueueSteering(UserMessage message)
    {
        lock (_lock) _steering.Enqueue(message);
    }

    public void EnqueueFollowUp(UserMessage message)
    {
        lock (_lock) _followUp.Enqueue(message);
    }

    /// <summary>Removes and returns all queued steering messages in FIFO order.</summary>
    public IReadOnlyList<UserMessage> DrainSteering()
    {
        lock (_lock)
        {
            if (_steering.Count == 0) return [];
            var copy = _steering.ToList();
            _steering.Clear();
            return copy;
        }
    }

    /// <summary>Removes and returns all queued follow-up messages in FIFO order.</summary>
    public IReadOnlyList<UserMessage> DrainFollowUp()
    {
        lock (_lock)
        {
            if (_followUp.Count == 0) return [];
            var copy = _followUp.ToList();
            _followUp.Clear();
            return copy;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _steering.Clear();
            _followUp.Clear();
        }
    }
}
