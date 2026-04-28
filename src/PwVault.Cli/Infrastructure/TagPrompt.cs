using PwVault.Core.Domain;
using Spectre.Console;

namespace PwVault.Cli.Infrastructure;

internal static class TagPrompt
{
    public static IReadOnlyList<string> Prompt(string label, string? defaultValue = null)
    {
        while (true)
        {
            var prompt = new TextPrompt<string>(label).AllowEmpty();
            if (defaultValue is not null)
                prompt = prompt.DefaultValue(defaultValue).ShowDefaultValue();

            var input = AnsiConsole.Prompt(prompt);
            if (string.IsNullOrWhiteSpace(input))
                return Array.Empty<string>();

            var parsed = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            try
            {
                return TagNormalizer.Normalize(parsed);
            }
            catch (ArgumentException ex)
            {
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
            }
        }
    }
}
