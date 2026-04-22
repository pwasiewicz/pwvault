using Microsoft.Extensions.Time.Testing;
using PwVault.Core.Security;
using Xunit;

namespace PwVault.Core.Tests.Security;

public sealed class FileSessionStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _sessionPath;
    private readonly FakeTimeProvider _time;
    private static readonly DateTimeOffset BaseTime = new(2026, 4, 22, 10, 0, 0, TimeSpan.Zero);

    public FileSessionStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "pwvault-sess-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _sessionPath = Path.Combine(_tempDir, "session");
        _time = new FakeTimeProvider(BaseTime);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private FileSessionStore NewStore(TimeSpan? initialTtl = null, TimeSpan? minTtlAfterUse = null) =>
        new(_sessionPath, _time, initialTtl, minTtlAfterUse);

    [Fact]
    public void Missing_file_returns_null()
    {
        var store = NewStore();
        Assert.Null(store.TryGetAndExtend());
    }

    [Fact]
    public void Save_then_load_returns_master_password()
    {
        var store = NewStore();
        store.Save("hunter2");

        Assert.True(File.Exists(_sessionPath));
        Assert.Equal("hunter2", store.TryGetAndExtend());
    }

    [Fact]
    public void Clear_removes_file()
    {
        var store = NewStore();
        store.Save("hunter2");
        store.Clear();

        Assert.False(File.Exists(_sessionPath));
        Assert.Null(store.TryGetAndExtend());
    }

    [Fact]
    public void Expired_session_is_discarded()
    {
        var store = NewStore();
        store.Save("hunter2");

        _time.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1));

        Assert.Null(store.TryGetAndExtend());
        Assert.False(File.Exists(_sessionPath));
    }

    [Fact]
    public void Extend_bumps_to_min_30min_when_less_remains()
    {
        var store = NewStore();
        store.Save("hunter2");

        _time.Advance(TimeSpan.FromMinutes(45));
        var before = ReadExpiresAt();
        Assert.Equal(BaseTime.AddHours(1), before);

        store.TryGetAndExtend();

        var after = ReadExpiresAt();
        Assert.Equal(_time.GetUtcNow().AddMinutes(30), after);
    }

    [Fact]
    public void Extend_does_not_shorten_expiration()
    {
        var store = NewStore();
        store.Save("hunter2");

        _time.Advance(TimeSpan.FromMinutes(10));
        var before = ReadExpiresAt();

        store.TryGetAndExtend();

        var after = ReadExpiresAt();
        Assert.Equal(before, after);
    }

    [Fact]
    public void Save_overwrites_previous_session()
    {
        var store = NewStore();
        store.Save("old");
        store.Save("new");

        Assert.Equal("new", store.TryGetAndExtend());
    }

    [Fact]
    public void Corrupt_file_returns_null_and_clears()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_sessionPath)!);
        File.WriteAllText(_sessionPath, "{ not valid json");

        var store = NewStore();

        Assert.Null(store.TryGetAndExtend());
        Assert.False(File.Exists(_sessionPath));
    }

    [Fact]
    public void File_has_owner_only_permissions_on_unix()
    {
        if (OperatingSystem.IsWindows()) return;

        var store = NewStore();
        store.Save("hunter2");

        var mode = File.GetUnixFileMode(_sessionPath);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
    }

    [Fact]
    public void Custom_ttls_are_respected()
    {
        var store = NewStore(
            initialTtl: TimeSpan.FromMinutes(5),
            minTtlAfterUse: TimeSpan.FromMinutes(1));

        store.Save("hunter2");
        _time.Advance(TimeSpan.FromMinutes(4));
        Assert.Equal("hunter2", store.TryGetAndExtend());

        _time.Advance(TimeSpan.FromMinutes(2));
        Assert.Null(store.TryGetAndExtend());
    }

    private DateTimeOffset ReadExpiresAt()
    {
        var json = File.ReadAllText(_sessionPath);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("expires_at").GetDateTimeOffset();
    }
}
