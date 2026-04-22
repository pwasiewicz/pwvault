namespace PwVault.Core.Storage;

internal sealed class EntryFileModel
{
    public int SchemaVersion { get; set; } = 1;
    public string Title { get; set; } = "";
    public string? Username { get; set; }
    public string? Url { get; set; }
    public string PasswordAge { get; set; } = "";
    public string? NotesAge { get; set; }
    public List<string>? Tags { get; set; }
    public DateTimeOffset Created { get; set; }
    public DateTimeOffset Updated { get; set; }
}
