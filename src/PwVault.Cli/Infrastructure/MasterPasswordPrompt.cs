using Spectre.Console;

namespace PwVault.Cli.Infrastructure;

public static class MasterPasswordPrompt
{
    public static string Ask(string message = "Master password")
    {
        return AnsiConsole.Prompt(
            new TextPrompt<string>($"{message}:")
                .PromptStyle("yellow")
                .Secret('*'));
    }

    public static string AskWithConfirmation()
    {
        while (true)
        {
            var first = Ask("Set master password");
            if (first.Length == 0)
            {
                AnsiConsole.MarkupLine("[red]Master password cannot be empty.[/]");
                continue;
            }
            var second = Ask("Confirm master password");
            if (first == second) return first;
            AnsiConsole.MarkupLine("[red]Passwords did not match. Try again.[/]");
        }
    }
}
