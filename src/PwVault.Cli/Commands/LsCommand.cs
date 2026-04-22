using System.ComponentModel;
using PwVault.Cli.Infrastructure;
using PwVault.Core.Domain;
using PwVault.Core.IO;
using PwVault.Core.Storage;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PwVault.Cli.Commands;

public sealed class LsCommand : Command<LsCommand.Settings>
{
    private readonly IFileSystem _fs;
    private readonly PwVaultConfig _config;

    public LsCommand(IFileSystem fs, PwVaultConfig config)
    {
        _fs = fs;
        _config = config;
    }

    public sealed class Settings : BaseCommandSettings
    {
        [CommandArgument(0, "[path]")]
        [Description("List entries under this subpath.")]
        public string? Path { get; init; }

        [CommandOption("--flat")]
        [Description("Print as flat list instead of tree.")]
        public bool Flat { get; init; }

        [CommandOption("--tag <TAG>")]
        [Description("Only include entries tagged with this value. May be repeated (AND semantics).")]
        public string[]? Tags { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var vaultPath = VaultPathResolver.Resolve(settings, _config);
        using var storage = VaultStorage.Open(vaultPath, _fs);

        EntryPath? under = string.IsNullOrWhiteSpace(settings.Path) ? null : new EntryPath(settings.Path);
        var entries = storage.List(under, settings.Tags).OrderBy(e => e.Entry.Path.Value).ToList();

        if (entries.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]No entries.[/]");
            return 0;
        }

        if (settings.Flat)
        {
            foreach (var e in entries)
                AnsiConsole.MarkupLine($"{Markup.Escape(e.Entry.Path.Value)} [dim]— {Markup.Escape(e.Entry.Title)}[/]");
            return 0;
        }

        var tree = new Tree($"[bold]{Markup.Escape(vaultPath)}[/]");
        var dirs = new Dictionary<string, TreeNode>();

        foreach (var stored in entries)
        {
            var segments = stored.Entry.Path.Segments;
            TreeNode currentNode = null!;
            IHasTreeNodes currentParent = tree;
            var accumulated = "";

            for (var i = 0; i < segments.Count; i++)
            {
                accumulated = i == 0 ? segments[i] : $"{accumulated}/{segments[i]}";
                var isLeaf = i == segments.Count - 1;

                if (isLeaf)
                {
                    var label = $"[green]{Markup.Escape(segments[i])}[/] [dim]— {Markup.Escape(stored.Entry.Title)}[/]";
                    currentParent.AddNode(label);
                    continue;
                }

                if (!dirs.TryGetValue(accumulated, out var node))
                {
                    node = currentParent.AddNode($"[yellow]{Markup.Escape(segments[i])}/[/]");
                    dirs[accumulated] = node;
                }
                currentNode = node;
                currentParent = currentNode;
            }
        }

        AnsiConsole.Write(tree);
        return 0;
    }
}
