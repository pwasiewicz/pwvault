namespace PwVault.Core.Security;

public interface ISessionStore
{
    void Save(string masterPassword);

    string? TryGetAndExtend();

    void Clear();
}
