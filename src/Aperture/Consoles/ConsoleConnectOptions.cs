namespace Aperture.Consoles;

/// <summary>Connection and discovery retry policy for independently owned monitor sessions.</summary>
public sealed record ConsoleConnectOptions
{
    /// <summary>When opened by a ConsoleManager, reuse its serialized monitor socket until the last console lease is disposed.</summary>
    public bool Shared { get; init; }
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan ConversationTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public int Attempts { get; init; } = 1;
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromMilliseconds(250);
    internal void Validate()
    {
        if (ConnectTimeout <= TimeSpan.Zero || ConnectTimeout.TotalMilliseconds > uint.MaxValue - 1) throw new ArgumentOutOfRangeException(nameof(ConnectTimeout));
        if (ConversationTimeout <= TimeSpan.Zero || ConversationTimeout.TotalMilliseconds > uint.MaxValue - 1) throw new ArgumentOutOfRangeException(nameof(ConversationTimeout));
        if (Attempts is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(Attempts));
        if (RetryDelay < TimeSpan.Zero || RetryDelay > TimeSpan.FromMinutes(1)) throw new ArgumentOutOfRangeException(nameof(RetryDelay));
    }
}
