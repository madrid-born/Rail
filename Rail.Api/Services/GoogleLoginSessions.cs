using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;

namespace Rail.Api.Services;

// A bounded, short-lived handoff from the system browser to the API client.
// The browser sees only the session ID; only the initiating client gets the exchange key.
public sealed class GoogleLoginSessions(TimeProvider clock) : IDisposable
{
    public const string PropertyKey = "rail:google-session";
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 1000 });

    public (string Id, string ExchangeKey, DateTimeOffset ExpiresAt) Create(string? linkUserId)
    {
        var id = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var key = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var expiresAt = clock.GetUtcNow() + Lifetime;
        cache.Set(id, new Session(Hash(key), expiresAt, linkUserId),
            new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = Lifetime, Size = 1 });
        return (id, key, expiresAt);
    }

    public Session? Find(string id) => cache.TryGetValue(id, out Session? session)
        && session is not null && session.ExpiresAt > clock.GetUtcNow() ? session : null;

    public Session? Find(string id, string key)
    {
        var session = Find(id);
        return session is not null && CryptographicOperations.FixedTimeEquals(session.KeyHash, Hash(key))
            ? session : null;
    }

    public bool Begin(string id)
    {
        var session = Find(id);
        if (session is null) return false;
        lock (session)
        {
            if (session.Started) return false;
            session.Started = true;
            return true;
        }
    }

    public void Complete(string? id, GoogleAccount? account)
    {
        var session = id is null ? null : Find(id);
        if (session is null) return;
        lock (session)
        {
            if (!session.Started || session.Completed) return;
            session.Account = account;
            session.Completed = true;
        }
    }

    public void Remove(string id) => cache.Remove(id);
    public void Dispose() => cache.Dispose();
    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));

    public sealed class Session(byte[] keyHash, DateTimeOffset expiresAt, string? linkUserId)
    {
        public byte[] KeyHash { get; } = keyHash;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
        public string? LinkUserId { get; } = linkUserId;
        public SemaphoreSlim ExchangeLock { get; } = new(1, 1);
        public bool Started { get; set; }
        public bool Completed { get; set; }
        public bool Consumed { get; set; }
        public GoogleAccount? Account { get; set; }
    }
}

public sealed record GoogleAccount(string Subject, string Email, bool EmailVerified);
