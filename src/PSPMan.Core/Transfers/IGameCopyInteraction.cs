namespace PSPMan.Core.Transfers;

/// <summary>Responses carry operation/prompt identity. Dismissal is cancellation, never consent.</summary>
public interface IGameCopyInteraction
{
    ValueTask<CopyPasswordResponse> RequestPasswordAsync(CopyPasswordPrompt prompt,
        CancellationToken cancellationToken);
    ValueTask<CopyOverwriteResponse> ConfirmOverwriteAsync(CopyOverwritePrompt prompt,
        CancellationToken cancellationToken);
}
