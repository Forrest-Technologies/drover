namespace Drover.Native.Controls;

/// <summary>
/// A theme drawn small, so a person can see it before choosing it: its working area and yard, a bot's head, a reply,
/// a running tool's card in its still state, and the composer, in the mode the app shows. The app's tokens hold the
/// theme in use, so this draws from the theme's own resolved values instead. It is a picture: the option it sits in
/// carries the theme's name.
/// </summary>
public sealed class ThemeSample : SKCanvasElement
{
    #region Private Fields

    private const float DrawingWidth = 280;

    private const float DrawingHeight = 132;

    private const float Pad = 12;

    private const float Head = 28;

    private const float Grid = 64;

    #endregion

    #region Constructors

    /// <summary>A picture <paramref name="width"/> wide, as tall as its drawing is for that width.</summary>
    public ThemeSample(double width)
    {
        IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        Width = width;
        Height = width * DrawingHeight / DrawingWidth;
        this.WhenShownOrRethemed(Invalidate);
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty ThemeProperty = Dp.Register<ThemeSample, ThemeFile?>(nameof(Theme), null, sample => sample.Invalidate());

    /// <summary>The theme to draw.</summary>
    public ThemeFile? Theme
    {
        get => (ThemeFile?)GetValue(ThemeProperty);
        set => SetValue(ThemeProperty, value);
    }

    #endregion

    #region Drawing

    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        if (Theme is not { } theme)
        {
            return;
        }

        var set = theme.Resolve(ActualTheme is ElementTheme.Light ? ColorMode.Light : ColorMode.Dark);
        canvas.Scale((float)area.Width / DrawingWidth);
        canvas.Clear(Paint(set, "bg-0"));
        DrawYard(canvas, theme, set);

        var size = (float)theme.Type.Scale[2];
        DrawHead(canvas, set, new SKPoint(Pad, Pad));
        DrawText(canvas, set, "A reply in this theme", "text", Pad + Head + 8, Pad + 18, size);

        var card = SKRect.Create(Pad, 50, DrawingWidth - (2 * Pad), 32);
        DrawFrame(canvas, theme, set, card, "bg-1", "line", theme.Shape.Cut.Frame, theme.Shape.CardRadius);
        DrawText(canvas, set, "Ran a command", "text", card.Left + 10, card.MidY + 4, size - 1);
        DrawText(canvas, set, "Running", "ion", card.Right - 58, card.MidY + 4, size - 2);
        using (var line = new SKPaint { Color = Paint(set, "ion"), IsAntialias = true })
        {
            canvas.DrawRect(card.Left, card.Top, card.Width, theme.Motion.TravelLine, line);
        }

        var composer = SKRect.Create(Pad, 92, DrawingWidth - (2 * Pad), 30);
        DrawFrame(canvas, theme, set, composer, "bg-1", "text-3", theme.Shape.Cut.Control, theme.Shape.ControlRadius);
        DrawText(canvas, set, "Write a message", "text-3", composer.Left + 10, composer.MidY + 4, size - 1);
        var send = SKRect.Create(composer.Right - 44, composer.Top + 5, 36, composer.Height - 10);
        DrawFrame(canvas, theme, set, send, "ember", "ember", Math.Min(theme.Shape.Cut.Control, 6), theme.Shape.ControlRadius);
    }

    private static SKColor Paint(ThemeTokenSet set, string token)
    {
        var color = set.Colors[token];
        return new(color.Color.Red, color.Color.Green, color.Color.Blue, color.Alpha);
    }

    private static void DrawYard(SKCanvas canvas, ThemeFile theme, ThemeTokenSet set)
    {
        var step = (float)theme.Surfaces.YardStep / 2;
        using var paint = new SKPaint { Color = Paint(set, "yard-dot"), IsAntialias = true, StrokeWidth = 1 };

        if (theme.Surfaces.Texture is YardTexture.Lines or YardTexture.Grid)
        {
            for (var y = step; y < DrawingHeight; y += step)
            {
                canvas.DrawLine(0, y, DrawingWidth, y, paint);
            }
        }

        if (theme.Surfaces.Texture is YardTexture.Grid)
        {
            for (var x = step; x < DrawingWidth; x += step)
            {
                canvas.DrawLine(x, 0, x, DrawingHeight, paint);
            }
        }

        if (theme.Surfaces.Texture is YardTexture.Dots)
        {
            for (var y = step / 2; y < DrawingHeight; y += step)
            {
                for (var x = step / 2; x < DrawingWidth; x += step)
                {
                    canvas.DrawCircle(x, y, 0.75f, paint);
                }
            }
        }
    }

    // The corner language of the theme: two cut corners, a small radius, or ticks drawn as lines at the cut corners.
    private static void DrawFrame(SKCanvas canvas, ThemeFile theme, ThemeTokenSet set, SKRect box, string fill, string edge, int cut, int radius)
    {
        using var surface = new SKPaint { Color = Paint(set, fill), IsAntialias = true };
        using var line = new SKPaint { Color = Paint(set, edge), IsAntialias = true, IsStroke = true, StrokeWidth = theme.Shape.Line };

        switch (theme.Shape.Corners)
        {
            case CornerLanguage.Faceted:
                using (var outline = SKPath.ParseSvgPathData(Faceted(box, Math.Min(cut, box.Height / 2))))
                {
                    canvas.DrawPath(outline, surface);
                    canvas.DrawPath(outline, line);
                }

                break;
            case CornerLanguage.Square:
                canvas.DrawRoundRect(box, radius, radius, surface);
                canvas.DrawRoundRect(box, radius, radius, line);
                break;
            default:
                canvas.DrawRect(box, surface);
                canvas.DrawRect(box, line);
                break;
        }

        if (theme.Shape.Corners is CornerLanguage.Ticked)
        {
            var tick = Math.Min(cut, box.Height / 2);
            var reach = theme.Shape.Registration ? 3 : 0;
            using var mark = new SKPaint { Color = Paint(set, "text-3"), IsAntialias = true, IsStroke = true, StrokeWidth = 2 };
            canvas.DrawLine(box.Left - reach, box.Top, box.Left + tick, box.Top, mark);
            canvas.DrawLine(box.Left, box.Top - reach, box.Left, box.Top + tick, mark);
            canvas.DrawLine(box.Right + reach, box.Bottom, box.Right - tick, box.Bottom, mark);
            canvas.DrawLine(box.Right, box.Bottom + reach, box.Right, box.Bottom - tick, mark);
        }
    }

    // Two cut corners, top left and bottom right, as the frames of the app have.
    private static string Faceted(SKRect box, float cut) =>
        $"M{box.Left + cut} {box.Top}L{box.Right} {box.Top}L{box.Right} {box.Bottom - cut}L{box.Right - cut} {box.Bottom}L{box.Left} {box.Bottom}L{box.Left} {box.Top + cut}Z";

    // A head at rest from the stored parts, in the theme's head color, with its visor dark.
    private static void DrawHead(SKCanvas canvas, ThemeTokenSet set, SKPoint at)
    {
        var look = BotLook.From(3);
        using var outline = SKPath.ParseSvgPathData(look.Head);
        using var band = ShapePainter.Outline(SvgParts.Read(look.Visor));

        canvas.Save();
        canvas.Translate(at.X, at.Y);
        canvas.Scale(Head / Grid);
        using (var head = new SKPaint { Color = Paint(set, "head-8"), IsAntialias = true })
        {
            canvas.DrawPath(outline, head);
        }

        canvas.ClipPath(outline, SKClipOperation.Intersect, antialias: true);
        using (var visor = new SKPaint { Color = Paint(set, "visor"), IsAntialias = true })
        {
            canvas.DrawPath(band, visor);
        }

        canvas.Restore();
    }

    private static void DrawText(SKCanvas canvas, ThemeTokenSet set, string words, string token, float x, float baseline, float size)
    {
        using var font = new SKFont { Size = size };
        using var paint = new SKPaint { Color = Paint(set, token), IsAntialias = true };
        canvas.DrawText(words, x, baseline, SKTextAlign.Left, font, paint);
    }

    #endregion
}
