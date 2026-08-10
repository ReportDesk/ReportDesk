using System.Collections.Concurrent;

namespace ReportSystem.Services;

public interface IMachinePhotoCaptureSessionService
{
    MachinePhotoCaptureSession Create();
    bool TryConsume(string token);
}

public sealed record MachinePhotoCaptureSession(string Token, DateTimeOffset ExpiresAt);

public sealed class MachinePhotoCaptureSessionService : IMachinePhotoCaptureSessionService
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _sessions = new();

    public MachinePhotoCaptureSession Create()
    {
        var token = Guid.NewGuid().ToString("N");
        var expiresAt = DateTimeOffset.UtcNow.Add(Lifetime);
        _sessions[token] = expiresAt;
        return new(token, expiresAt);
    }

    public bool TryConsume(string token)
    {
        return _sessions.TryRemove(token, out var expiresAt) && expiresAt >= DateTimeOffset.UtcNow;
    }
}
