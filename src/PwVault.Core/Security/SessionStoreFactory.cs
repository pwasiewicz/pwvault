namespace PwVault.Core.Security;

public static class SessionStoreFactory
{
    public static ISessionStore Create(TimeProvider? time = null)
    {
        if (OperatingSystem.IsWindows())
            return new WindowsSessionStore(time: time);
        return new FileSessionStore(time: time);
    }
}
