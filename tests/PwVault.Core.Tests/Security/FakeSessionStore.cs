using PwVault.Core.Security;

namespace PwVault.Core.Tests.Security;

internal sealed class FakeSessionStore : ISessionStore
{
    public string? Master { get; set; }
    public int SaveCount { get; private set; }
    public int GetCount { get; private set; }
    public int ClearCount { get; private set; }

    public void Save(string masterPassword)
    {
        Master = masterPassword;
        SaveCount++;
    }

    public string? TryGetAndExtend()
    {
        GetCount++;
        return Master;
    }

    public void Clear()
    {
        ClearCount++;
        Master = null;
    }
}
