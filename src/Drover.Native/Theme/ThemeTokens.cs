namespace Drover.Native.Theme;

/// <summary>
/// Puts a theme into the resources of the app, so every group of tokens follows the theme in use. A theme is data
/// (<c>Drover.Presentation</c> reads and resolves it); this is the one place that turns the resolved values into
/// resources. Colors change in place, so a brush that a control already holds repaints at once. Numbers, faces and
/// weights are written into <see cref="Tokens.Dictionary"/>, the derived line heights beside them, and the style
/// file is loaded again so its styles take them. A control that reads a number when it is built needs to be built
/// again, which the shell does for the open screen when another theme is applied.
/// </summary>
public static class ThemeTokens
{
    #region Private Fields

    private const string DarkKey = "Default";

    private const string DarkAliasKey = "Dark";

    private const string LightKey = "Light";

    private static readonly string[] fontWeightTokens = ["weight-body", "weight-label", "weight-heading"];

    #endregion

    #region Properties

    /// <summary>The theme in use. Night Yard until a theme is applied: the token file holds its values.</summary>
    public static ThemeFile Current { get; private set; } = BuiltInThemes.NightYard;

    #endregion

    #region Public Methods

    /// <summary>Applies a theme in both of its modes. The mode a person sees is chosen apart, by the requested theme of the window.</summary>
    public static void Apply(ThemeFile theme)
    {
        var (dark, light) = (theme.Resolve(ColorMode.Dark), theme.Resolve(ColorMode.Light));

        WriteColors(DarkKey, dark);
        WriteColors(DarkAliasKey, dark);
        WriteColors(LightKey, light);
        WriteNumbers(dark);
        WriteDerived(dark);
        WriteFaces(dark.Faces);
        Styles.Reload();

        Current = theme;
    }

    #endregion

    #region Private Methods

    // A number keeps the type its token has in the token file: a count such as the tracking is a whole number, and a
    // client that reads it as one must still find one. (A conditional between the two would be a double either way.)
    private static void WriteNumber(string key, double value)
    {
        if (Tokens.Dictionary.TryGetValue(key, out var held) && held is int)
        {
            Tokens.Dictionary[key] = (int)value;
        }
        else
        {
            Tokens.Dictionary[key] = value;
        }
    }

    private static void WriteColors(string dictionaryKey, ThemeTokenSet set)
    {
        var target = (ResourceDictionary)Tokens.Dictionary.ThemeDictionaries[dictionaryKey];

        foreach (var (token, argb) in set.Colors)
        {
            var key = TokenNames.Of(token);
            var color = Color.FromArgb(argb.Alpha, argb.Color.Red, argb.Color.Green, argb.Color.Blue);
            target[key] = color;

            // A brush is shared by everything that uses it, so changing its color repaints all of them.
            if (target.TryGetValue($"{key}Brush", out var held) && held is SolidColorBrush brush)
            {
                brush.Color = color;
            }
            else
            {
                target[$"{key}Brush"] = new SolidColorBrush(color);
            }
        }
    }

    private static void WriteNumbers(ThemeTokenSet set)
    {
        foreach (var (token, value) in set.Numbers)
        {
            var key = TokenNames.Of(token);
            if (fontWeightTokens.Contains(token))
            {
                Tokens.Dictionary[key] = new Windows.UI.Text.FontWeight((ushort)value);
            }
            else
            {
                WriteNumber(key, value);
            }
        }
    }

    // The line heights are already numbers named DvLine12 to DvLineH3. The lead of a style is the room a line height
    // leaves above the letters, which the native text box does not center: the style pads the letters down by it
    // and takes the same amount back below, so the box keeps the height the line height gives it.
    private static void WriteDerived(ThemeTokenSet set)
    {
        WriteLead("DvLeadIn14", "DvLeadOut14", set.Numbers["lead-body"]);
        WriteLead("DvLeadInMono", "DvLeadOutMono", set.Numbers["lead-mono"]);
    }

    private static void WriteLead(string inKey, string outKey, double lead)
    {
        Tokens.Dictionary[inKey] = new Thickness(0, lead, 0, 0);
        Tokens.Dictionary[outKey] = new Thickness(0, 0, 0, -lead);
    }

    private static void WriteFaces(ThemeFaces faces)
    {
        Tokens.Dictionary["DvFontUi"] = Family(faces.Interface);
        Tokens.Dictionary["DvFontMono"] = Family(faces.Machine);
        Tokens.Dictionary["DvFontReading"] = Family(faces.Reading);
        Tokens.Dictionary["DvFontNumerals"] = Family(faces.Numerals);
        Tokens.Dictionary["DvFontHeading"] = Family(faces.Heading);

        // The one variable face of the web client draws large text with its display cut. The static files the
        // native renderer needs keep the two cuts apart, so the largest heading takes the display cut where a face has one.
        Tokens.Dictionary["DvFontHeadingLarge"] = new FontFamily(faces.Heading.DisplayAsset ?? faces.Heading.Asset);
    }

    private static FontFamily Family(FontFaces.Face face) => new(face.Asset);

    #endregion
}
