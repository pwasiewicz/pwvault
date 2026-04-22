namespace PwVault.Core.Security;

public enum DecryptionStatus
{
    Success,
    MasterNeeded,
}

public sealed record DecryptionResult(DecryptionStatus Status, string? PlainText)
{
    public static DecryptionResult Success(string? plainText) => new(DecryptionStatus.Success, plainText);

    public static readonly DecryptionResult MasterNeeded = new(DecryptionStatus.MasterNeeded, null);
}
