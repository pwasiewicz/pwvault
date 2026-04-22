using Spectre.Console;
using TextCopy;

namespace PwVault.Cli.Infrastructure;

public static class ClipboardCopier
{
    public static void CopyWithAutoClear(string value, int clearAfterSeconds)
    {
        ClipboardService.SetText(value);

        AnsiConsole.Progress()
            .AutoClear(false)
            .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new RemainingTimeColumn())
            .Start(ctx =>
            {
                var task = ctx.AddTask("[yellow]Clipboard clears in[/]", maxValue: clearAfterSeconds);
                for (var i = 0; i < clearAfterSeconds; i++)
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(intercept: true);
                        if (key.Key == ConsoleKey.Escape || key.Key == ConsoleKey.Enter)
                        {
                            task.Value = clearAfterSeconds;
                            break;
                        }
                    }
                    Thread.Sleep(1000);
                    task.Increment(1);
                }
            });

        try
        {
            var current = ClipboardService.GetText();
            if (current == value)
                ClipboardService.SetText("");
        }
        catch { /* best-effort */ }

        AnsiConsole.MarkupLine("[dim]Clipboard cleared.[/]");
    }
}
