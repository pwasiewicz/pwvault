using PwVault.Core.Domain;
using PwVault.Core.Security;
using PwVault.Core.Security.Age;
using PwVault.Core.Storage;
using Terminal.Gui;
using TextCopy;

namespace PwVault.Cli.Infrastructure;

public sealed class VaultGuiApp
{
    private readonly IVaultStorage _storage;
    private readonly ICryptoService _crypto;
    private readonly PwVaultConfig _config;

    private readonly List<EntryRow> _all;
    private List<EntryRow> _filtered;

    private TextField _filter = null!;
    private ListView _list = null!;
    private Label _detailPath = null!;
    private Label _detailTitle = null!;
    private Label _detailUsername = null!;
    private Label _detailUrl = null!;
    private Label _detailUpdated = null!;
    private Label _detailTags = null!;
    private Label _detailNotes = null!;
    private Label _status = null!;

    private string? _lastCopiedValue;
    private int _copyEpoch;

    public VaultGuiApp(IVaultStorage storage, ICryptoService crypto, PwVaultConfig config)
    {
        _storage = storage;
        _crypto = crypto;
        _config = config;

        _all = _storage.List()
            .OrderBy(e => e.Entry.Path.Value, StringComparer.Ordinal)
            .Select(EntryRow.From)
            .ToList();
        _filtered = _all;
    }

    public void Run()
    {
        Application.Init();
        try
        {
            ApplyDarkTheme();
            Application.Top.Add(BuildUi());
            RefreshList();
            Application.Run();
        }
        finally
        {
            Application.Shutdown();
        }
    }

    private static void ApplyDarkTheme()
    {
        var drv = Application.Driver;
        var normal = drv.MakeAttribute(Color.Gray, Color.Black);
        var focus = drv.MakeAttribute(Color.White, Color.DarkGray);
        var hot = drv.MakeAttribute(Color.BrightYellow, Color.Black);
        var hotFocus = drv.MakeAttribute(Color.BrightYellow, Color.DarkGray);
        var disabled = drv.MakeAttribute(Color.DarkGray, Color.Black);

        var dark = new ColorScheme
        {
            Normal = normal, Focus = focus, HotNormal = hot, HotFocus = hotFocus, Disabled = disabled
        };
        var status = new ColorScheme
        {
            Normal = drv.MakeAttribute(Color.Black, Color.Gray),
            Focus = drv.MakeAttribute(Color.Black, Color.Gray),
            HotNormal = drv.MakeAttribute(Color.BrightYellow, Color.Gray),
            HotFocus = drv.MakeAttribute(Color.BrightYellow, Color.Gray),
            Disabled = drv.MakeAttribute(Color.DarkGray, Color.Gray),
        };

        Colors.Base = dark;
        Colors.TopLevel = dark;
        Colors.Dialog = dark;
        Colors.Error = new ColorScheme
        {
            Normal = drv.MakeAttribute(Color.BrightRed, Color.Black),
            Focus = drv.MakeAttribute(Color.BrightRed, Color.DarkGray),
            HotNormal = drv.MakeAttribute(Color.BrightYellow, Color.Black),
            HotFocus = drv.MakeAttribute(Color.BrightYellow, Color.DarkGray),
            Disabled = disabled,
        };
        Colors.Menu = status;

        Application.Top.ColorScheme = dark;
    }

    private View BuildUi()
    {
        var win = new Window($"pwvault — {_storage.RootPath}")
        {
            X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill()
        };

        var filterLabel = new Label("Filter: ") { X = 1, Y = 0 };
        _filter = new TextField("")
        {
            X = Pos.Right(filterLabel),
            Y = 0,
            Width = Dim.Fill(1)
        };
        _filter.TextChanged += _ => ApplyFilter();
        _filter.KeyDown += OnFilterKey;

        var listFrame = new FrameView("Entries")
        {
            X = 0,
            Y = 2,
            Width = Dim.Percent(60),
            Height = Dim.Fill(1)
        };
        _list = new ListView(new List<string>())
        {
            X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(),
            AllowsMarking = false,
            CanFocus = true
        };
        _list.OpenSelectedItem += _ => CopyCurrent();
        _list.SelectedItemChanged += _ => RefreshDetails();
        _list.KeyDown += OnListKey;
        listFrame.Add(_list);

        var detailsFrame = new FrameView("Details")
        {
            X = Pos.Right(listFrame),
            Y = 2,
            Width = Dim.Fill(),
            Height = Dim.Fill(1)
        };
        _detailPath = MakeDetailLabel(0);
        _detailTitle = MakeDetailLabel(1);
        _detailUsername = MakeDetailLabel(2);
        _detailUrl = MakeDetailLabel(3);
        _detailUpdated = MakeDetailLabel(4);
        _detailTags = MakeDetailLabel(5);
        _detailNotes = MakeDetailLabel(7);
        detailsFrame.Add(_detailPath, _detailTitle, _detailUsername, _detailUrl,
            _detailUpdated, _detailTags, _detailNotes);

        _status = new Label("[Enter] copy password   [/] filter   [Tab] switch pane   [q] quit")
        {
            X = 0,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(),
            ColorScheme = Colors.Menu
        };

        win.Add(filterLabel, _filter, listFrame, detailsFrame, _status);

        Application.Top.KeyDown += OnGlobalKey;
        _filter.SetFocus();
        return win;
    }

    private static Label MakeDetailLabel(int y) => new("")
    {
        X = 1, Y = y, Width = Dim.Fill(1)
    };

    private void OnGlobalKey(View.KeyEventEventArgs e)
    {
        if (e.KeyEvent.Key == Key.Esc)
        {
            e.Handled = true;
            Application.RequestStop();
        }
    }

    private void OnFilterKey(View.KeyEventEventArgs e)
    {
        switch (e.KeyEvent.Key)
        {
            case Key.CursorDown:
            case Key.Enter:
                _list.SetFocus();
                e.Handled = true;
                break;
        }
    }

    private void OnListKey(View.KeyEventEventArgs e)
    {
        switch (e.KeyEvent.Key)
        {
            case (Key)'/':
                _filter.SetFocus();
                e.Handled = true;
                break;
            case (Key)'q':
            case (Key)'Q':
                Application.RequestStop();
                e.Handled = true;
                break;
        }
    }

    private void ApplyFilter()
    {
        var q = (_filter.Text ?? string.Empty).ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(q))
        {
            _filtered = _all;
        }
        else
        {
            _filtered = _all
                .Where(r => r.SearchBlob.Contains(q, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        RefreshList();
    }

    private void RefreshList()
    {
        var labels = _filtered.Select(r => r.Display).ToList();
        _list.SetSource(labels);
        if (labels.Count > 0 && _list.SelectedItem >= labels.Count)
            _list.SelectedItem = 0;
        RefreshDetails();
    }

    private void RefreshDetails()
    {
        var row = CurrentRow();
        if (row is null)
        {
            _detailPath.Text = "";
            _detailTitle.Text = "";
            _detailUsername.Text = "";
            _detailUrl.Text = "";
            _detailUpdated.Text = "";
            _detailTags.Text = "";
            _detailNotes.Text = "";
            return;
        }
        var e = row.Stored.Entry;
        _detailPath.Text = $"Path:     {e.Path.Value}";
        _detailTitle.Text = $"Title:    {e.Title}";
        _detailUsername.Text = $"Username: {e.Username ?? "—"}";
        _detailUrl.Text = $"URL:      {e.Url ?? "—"}";
        _detailUpdated.Text = $"Updated:  {e.Updated:u}";
        _detailTags.Text = $"Tags:     {(e.Tags.Count == 0 ? "—" : string.Join(", ", e.Tags))}";
        _detailNotes.Text = e.NotesEncrypted is null ? "Notes:    —" : "Notes:    <encrypted>";
    }

    private EntryRow? CurrentRow()
    {
        if (_filtered.Count == 0) return null;
        var idx = _list.SelectedItem;
        if (idx < 0 || idx >= _filtered.Count) return null;
        return _filtered[idx];
    }

    private void CopyCurrent()
    {
        var row = CurrentRow();
        if (row is null) return;

        string plain;
        try
        {
            var result = _crypto.DecryptPassword(row.Stored.Entry);
            if (result.Status != DecryptionStatus.Success || result.PlainText is null)
            {
                SetStatus($"Decrypt failed: {row.Stored.Entry.Path.Value}");
                return;
            }
            plain = result.PlainText;
        }
        catch (Exception ex)
        {
            SetStatus($"Decrypt error: {ex.Message}");
            return;
        }

        ClipboardService.SetText(plain);
        _lastCopiedValue = plain;
        var epoch = ++_copyEpoch;
        var seconds = _config.ClipboardClearSeconds;
        SetStatus($"Copied password for {row.Stored.Entry.Path.Value} — clears in {seconds}s");

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds));
            if (epoch != _copyEpoch) return;
            try
            {
                var current = ClipboardService.GetText();
                if (current == plain)
                    ClipboardService.SetText("");
            }
            catch { /* best-effort */ }
            if (epoch != _copyEpoch) return;
            Application.MainLoop.Invoke(() =>
            {
                if (epoch == _copyEpoch)
                {
                    _lastCopiedValue = null;
                    SetStatus("Clipboard cleared.");
                }
            });
        });
    }

    private void SetStatus(string text)
    {
        _status.Text = text;
        _status.SetNeedsDisplay();
    }

    private sealed record EntryRow(StoredEntry Stored, string Display, string SearchBlob)
    {
        public static EntryRow From(StoredEntry stored)
        {
            var e = stored.Entry;
            var display = $"{e.Path.Value}  —  {e.Title}";
            var parts = new List<string> { e.Path.Value, e.Title };
            if (!string.IsNullOrEmpty(e.Username)) parts.Add(e.Username);
            if (!string.IsNullOrEmpty(e.Url)) parts.Add(e.Url);
            if (e.Tags.Count > 0) parts.Add(string.Join(" ", e.Tags));
            return new EntryRow(stored, display, string.Join(" ", parts));
        }
    }
}
