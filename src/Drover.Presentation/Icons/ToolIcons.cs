namespace Drover.Ui.Icons;

public readonly record struct ToolIcon(IconName Shape, string ColorToken = "DvText3");


/// <summary>The icon and theme color for a tool in the conversation ledger.</summary>
public static class ToolIcons
{
    /// <summary>Resolves the tool ID carried by a live event or a stored message part.</summary>
    public static ToolIcon For(string tool) => tool switch
    {
        "web.search" or "web_search" => new(IconName.Search, "DvHue8"),
        "web.fetch" or "web_fetch" => new(IconName.ExternalLink, "DvHue6"),
        "sandbox.run" => new(IconName.Command),
        "sandbox.read_file" or "sandbox.list_files" => new(IconName.File),
        "sandbox.write_file" or "sandbox.share_file" => new(IconName.Edit),
        _ when tool.StartsWith("computer.", StringComparison.Ordinal) => new(IconName.Screen),
        _ when tool.StartsWith("memory.", StringComparison.Ordinal) => new(IconName.Memory),
        _ when tool.StartsWith("crew.", StringComparison.Ordinal) => new(IconName.Account),
        _ => new(IconName.Tool),
    };
}
