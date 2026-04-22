using Microsoft.Extensions.Time.Testing;
using PwVault.Core.Security;
using Xunit;

namespace PwVault.Core.Tests.Security;

public sealed class WindowsSessionStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _sessionPath;
    private readonly FakeTimeProvider _time;
    private static readonly DateTimeOffset BaseTime = new(2026, 4, 22, 10, 0, 0, TimeSpan.Zero);

    public WindowsSessionStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "pwvault-winsess-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _sessionPath = Path.Combine(_tempDir, "session");
        _time = new FakeTimeProvider(BaseTime);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Save_then_load_returns_master_password()
    {
        if (!OperatingSystem.IsWindows()) return;

        var store = new WindowsSessionStore(_sessionPath, _time);
        store.Save("hunter2");

        Assert.True(File.Exists(_sessionPath));
        Assert.Equal("hunter2", store.TryGetAndExtend());
    }

    [Fact]
    public void File_content_is_not_plaintext_json()
    {
        if (!OperatingSystem.IsWindows()) return;

        var store = new WindowsSessionStore(_sessionPath, _time);
        store.Save("hunter2");

        var bytes = File.ReadAllBytes(_sessionPath);
        var asText = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain("hunter2", asText);
        Assert.DoesNotContain("master_password", asText);
    }

    [Fact]
    public void Expired_session_is_discarded()
    {
        if (!OperatingSystem.IsWindows()) return;

        var store = new WindowsSessionStore(_sessionPath, _time);
        store.Save("hunter2");

        _time.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1));

        Assert.Null(store.TryGetAndExtend());
        Assert.False(File.Exists(_sessionPath));
    }

    [Fact]
    public void Extend_bumps_to_min_30min_when_less_remains()
    {
        if (!OperatingSystem.IsWindows()) return;

        var store = new WindowsSessionStore(_sessionPath, _time);
        store.Save("hunter2");

        _time.Advance(TimeSpan.FromMinutes(45));
        Assert.Equal("hunter2", store.TryGetAndExtend());

        _time.Advance(TimeSpan.FromMinutes(25));
        Assert.Equal("hunter2", store.TryGetAndExtend());
    }

    [Fact]
    public void Corrupt_file_returns_null_and_clears()
    {
        if (!OperatingSystem.IsWindows()) return;

        Directory.CreateDirectory(Path.GetDirectoryName(_sessionPath)!);
        File.WriteAllBytes(_sessionPath, [0x01, 0x02, 0x03]);

        var store = new WindowsSessionStore(_sessionPath, _time);

        Assert.Null(store.TryGetAndExtend());
        Assert.False(File.Exists(_sessionPath));
    }
}
