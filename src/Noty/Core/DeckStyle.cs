namespace Noty.Core;

/// How the deck draws itself once it fans out.
public enum DeckStyle
{
    /// Labelled vertical tabs — the full deck.
    Tabs,
    /// Colour chips only — barely touches the screen.
    Compact,
}

public static class DeckStyleNames
{
    public static string Title(this DeckStyle s) => s switch
    {
        DeckStyle.Tabs => "标签页",
        _ => "色块",
    };
}
