using System.ComponentModel;
using Spectre.Console.Cli;

namespace PwVault.Cli.Infrastructure;

public class BaseCommandSettings : CommandSettings
{
    [CommandOption("--vault <PATH>")]
    [Description("Override vault directory (takes precedence over PWVAULT_PATH and config file).")]
    public string? VaultPath { get; init; }

    [CommandOption("-y|--yes")]
    [Description("Skip confirmation prompts.")]
    public bool SkipConfirmations { get; init; }
}
