namespace PwVault.Core.Security;

public static class SessionTtl
{
    public static readonly TimeSpan Initial = TimeSpan.FromHours(1);

    public static readonly TimeSpan MinAfterUse = TimeSpan.FromMinutes(30);
}
