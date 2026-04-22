using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using PwVault.Cli.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PwVault.Cli.Commands;

public sealed class ConfigCommand : Command<ConfigCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[action]")]
        [Description("show (default) | set | path")]
        public string? Action { get; init; }

        [CommandArgument(1, "[key]")]
        [Description("Config key in snake_case (e.g. vault_path).")]
        public string? Key { get; init; }

        [CommandArgument(2, "[value]")]
        public string? Value { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var action = (settings.Action ?? "show").ToLowerInvariant();
        return action switch
        {
            "show" => Show(),
            "path" => PrintPath(),
            "set" => Set(settings.Key, settings.Value),
            _ => Unknown(action),
        };
    }

    private static int Show()
    {
        var configPath = ConfigLoader.GetConfigPath();
        var fromFile = ConfigLoader.LoadFromFile();
        var effective = ConfigLoader.Load();

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Key");
        table.AddColumn("Value");
        table.AddColumn("Source");

        foreach (var prop in KeyedProperties())
        {
            var fromFileValue = fromFile is null ? null : prop.GetValue(fromFile);
            var effectiveValue = prop.GetValue(effective);
            var source = fromFileValue is not null ? "file" : "default";
            if (prop.Name == nameof(PwVaultConfig.VaultPath)
                && Environment.GetEnvironmentVariable("PWVAULT_PATH") is { Length: > 0 })
                source = "PWVAULT_PATH";

            table.AddRow(
                ToSnakeCase(prop.Name),
                Markup.Escape(Display(effectiveValue)),
                $"[dim]{source}[/]");
        }

        AnsiConsole.MarkupLine($"[dim]Config file: {Markup.Escape(configPath)}[/]");
        if (fromFile is null) AnsiConsole.MarkupLine("[dim](does not exist yet — all values are defaults)[/]");
        AnsiConsole.Write(table);
        return 0;
    }

    private static int PrintPath()
    {
        AnsiConsole.WriteLine(ConfigLoader.GetConfigPath());
        return 0;
    }

    private static int Set(string? key, string? value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            AnsiConsole.MarkupLine("[red]Missing key. Usage: pwvault config set <key> <value>[/]");
            return 2;
        }
        if (value is null)
        {
            AnsiConsole.MarkupLine("[red]Missing value. Usage: pwvault config set <key> <value>[/]");
            return 2;
        }

        var prop = KeyedProperties().FirstOrDefault(p =>
            string.Equals(ToSnakeCase(p.Name), key, StringComparison.OrdinalIgnoreCase));

        if (prop is null)
        {
            var known = string.Join(", ", KeyedProperties().Select(p => ToSnakeCase(p.Name)));
            AnsiConsole.MarkupLine($"[red]Unknown key '{Markup.Escape(key)}'. Known: {Markup.Escape(known)}[/]");
            return 2;
        }

        object? parsed;
        try
        {
            parsed = ParseValue(prop.PropertyType, value);
        }
        catch (FormatException ex)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
            return 2;
        }

        var config = ConfigLoader.LoadFromFile() ?? new PwVaultConfig();
        prop.SetValue(config, parsed);
        ConfigLoader.Save(config);

        AnsiConsole.MarkupLine($"[green]Set {ToSnakeCase(prop.Name)} = {Markup.Escape(Display(parsed))}[/]");
        AnsiConsole.MarkupLine($"[dim]Saved to {Markup.Escape(ConfigLoader.GetConfigPath())}[/]");
        return 0;
    }

    private static int Unknown(string action)
    {
        AnsiConsole.MarkupLine($"[red]Unknown action '{Markup.Escape(action)}'. Use 'show', 'set', or 'path'.[/]");
        return 2;
    }

    private static IEnumerable<PropertyInfo> KeyedProperties() =>
        typeof(PwVaultConfig)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite);

    private static string ToSnakeCase(string pascalName) =>
        JsonNamingPolicy.SnakeCaseLower.ConvertName(pascalName);

    private static object? ParseValue(Type target, string raw)
    {
        var type = Nullable.GetUnderlyingType(target) ?? target;

        if (type == typeof(string))
            return ExpandPath(raw);

        if (type == typeof(bool))
        {
            return raw.ToLowerInvariant() switch
            {
                "true" or "yes" or "1" or "on" => true,
                "false" or "no" or "0" or "off" => false,
                _ => throw new FormatException($"Cannot parse '{raw}' as bool (expected true/false).")
            };
        }

        if (type == typeof(int))
        {
            if (int.TryParse(raw, out var i)) return i;
            throw new FormatException($"Cannot parse '{raw}' as int.");
        }

        throw new FormatException($"Unsupported config type: {type.Name}.");
    }

    private static string ExpandPath(string raw) =>
        raw.StartsWith("~/", StringComparison.Ordinal)
            ? Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? "", raw[2..])
            : raw;

    private static string Display(object? value) =>
        value switch
        {
            null => "(unset)",
            bool b => b ? "true" : "false",
            _ => value.ToString() ?? "",
        };
}
