namespace Drover.Ui.Components;

/// <summary>
/// Machine text in a block that scrolls sideways inside itself, with a button that copies it.
/// </summary>
public sealed partial class CodeBlock(IJSRuntime js, ILogger<CodeBlock> logger) : IAsyncDisposable
{
    #region Private Fields

    private const string ModulePath = "./_content/Drover.Ui/Components/CodeBlock.razor.js";

    private const string Copied = "Copied";

    private const string CopyFailed = "Copy failed. Select the code and copy it by hand.";

    private static readonly TimeSpan confirmationTime = TimeSpan.FromSeconds(2);

    private readonly CancellationTokenSource disposal = new();

    private IJSObjectReference? module;

    private string status = string.Empty;

    private int copies;

    #endregion

    #region Properties

    [Parameter, EditorRequired]
    public string Code { get; set; } = string.Empty;

    [Parameter]
    public string? Language { get; set; }

    #endregion

    #region Interface Implementations

    public async ValueTask DisposeAsync()
    {
        await disposal.CancelAsync();
        disposal.Dispose();

        if (module is not null)
        {
            await DisposeModule(module);
        }
    }

    #endregion

    #region Copying

    private async Task Copy()
    {
        var copy = ++copies;
        status = await WriteToClipboard() ? Copied : CopyFailed;
        StateHasChanged();

        if (await Elapsed(confirmationTime) && copy == copies)
        {
            status = string.Empty;
        }
    }

    private async Task<bool> WriteToClipboard()
    {
        try
        {
            module ??= await js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            await module.InvokeVoidAsync("copyText", Code);
            return true;
        }
        catch (JSException exception)
        {
            logger.LogWarning(exception, "The browser refused to copy a code block");
            return false;
        }
    }

    /// <summary>Waits, and says whether the component is still on screen afterward.</summary>
    private async Task<bool> Elapsed(TimeSpan time)
    {
        try
        {
            await Task.Delay(time, disposal.Token);
            return true;
        }
        catch (OperationCanceledException exception)
        {
            logger.LogTrace(exception, "A code block left the screen while its copy confirmation was showing");
            return false;
        }
    }

    private async Task DisposeModule(IJSObjectReference loaded)
    {
        try
        {
            await loaded.DisposeAsync();
        }
        catch (JSDisconnectedException exception)
        {
            logger.LogTrace(exception, "The browser was gone before the copy module could be released");
        }
    }

    #endregion
}
