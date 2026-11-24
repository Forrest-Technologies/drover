namespace Drover.Native.Controls;

/// <summary>Draws a bot's character on its 64 unit grid, and moves its visor while the bot works.</summary>
internal sealed class AvatarCanvas : SKCanvasElement
{
    #region Private Fields

    private const float Grid = 64;

    private const int SmallestSizeWithDetail = 24;

    private const double Dim = 0.35;

    // An outlined visor's edge: its width on the 64 unit grid, and its strength.
    private const float EdgeWidth = 1.6f;

    private const byte EdgeAlpha = 230;

    // The visor's light at 45 percent while a tool runs, so the bar crossing it stands out.
    private const byte DimmedAlpha = 115;

    private const float SweepWidth = 16;

    // The bar crosses from a quarter of the grid before the visor to a tenth past it, and rests at two fifths.
    private const float SweepFrom = -0.25f * Grid;

    private const float SweepTo = 1.1f * Grid;

    private const float SweepAtRest = 0.4f * Grid;

    private static readonly TimeSpan frame = TimeSpan.FromMilliseconds(33);

    private readonly DispatcherTimer clock = new() { Interval = frame };

    private readonly System.Diagnostics.Stopwatch elapsed = new();

    private BotLook look = BotLook.From(0);

    private int hue;

    private int size = 32;

    private AvatarState state;

    #endregion

    #region Constructors

    public AvatarCanvas()
    {
        IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        clock.Tick += (_, _) => Tick();
    }

    #endregion

    #region Public Methods

    public void Show(BotLook character, int hueStep, int edge, AvatarState doing)
    {
        (look, hue, size, state) = (character, hueStep, edge, doing);

        // Only a live state moves, and only while motion is wanted. Nothing loops at rest. A theme that fades its
        // states gives thinking one fade up and holds a running tool still.
        var moves = Motion.Loops
            ? state is AvatarState.Thinking or AvatarState.Tool
            : Motion.Fades && state is AvatarState.Thinking;
        if (moves)
        {
            elapsed.Start();
            clock.Start();
        }
        else
        {
            StopMoving();
        }

        Invalidate();
    }

    public void StopMoving()
    {
        clock.Stop();
        elapsed.Stop();
    }

    #endregion

    #region Drawing

    // A fading theme has one fade to draw, and the clock ends with it.
    private void Tick()
    {
        Invalidate();
        if (Motion.Fades && elapsed.Elapsed >= Motion.FadeTime)
        {
            StopMoving();
        }
    }

    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        var head = Tokens.Color(this, $"DvHead{hue}").ToRgb();
        var onHead = Tokens.Color(this, "DvOnHead").ToRgb();
        var visor = Tokens.Color(this, "DvVisor").ToRgb();
        var topColor = head.MixWith(visor, 0.78).ToSkia();
        var markColor = head.MixWith(onHead, 0.40).ToSkia();
        var showsDetail = size >= SmallestSizeWithDetail;
        using var headOutline = SKPath.ParseSvgPathData(look.Head);

        canvas.Scale((float)area.Width / Grid);
        canvas.RotateDegrees(look.Tilt, 32, 36);

        if (showsDetail)
        {
            DrawParts(canvas, look.Top, topColor, markColor);
        }

        using (var paint = new SKPaint { Color = head.ToSkia(), IsAntialias = true })
        {
            canvas.DrawPath(headOutline, paint);
        }

        canvas.Save();
        canvas.ClipPath(headOutline, SKClipOperation.Intersect, antialias: true);
        var facetAlpha = (byte)Math.Round(Tokens.Number("DvCharacterFacet") * byte.MaxValue);
        using (var facet = new SKPaint { Color = onHead.ToSkia().WithAlpha(facetAlpha) })
        {
            canvas.DrawRect(0, 0, Grid / 2, Grid, facet);
        }

        if (showsDetail)
        {
            DrawParts(canvas, look.Mark, markColor, markColor);
        }

        canvas.Restore();
        DrawVisor(canvas, headOutline, visor.ToSkia());
    }

    // A part drawn as a line takes its color as a stroke; a dot on a top takes the lighter color.
    private static void DrawParts(SKCanvas canvas, string markup, SKColor color, SKColor dotColor)
    {
        foreach (var shape in SvgParts.Read(markup))
        {
            switch (shape.Class)
            {
                case "dv-character-line":
                    ShapePainter.Stroke(canvas, shape, color, (float)Tokens.Number("DvCharacterLine"), round: true);
                    break;
                case "dv-character-dot":
                    ShapePainter.Fill(canvas, shape, dotColor);
                    break;
                default:
                    ShapePainter.Fill(canvas, shape, color);
                    break;
            }
        }
    }

    private void DrawVisor(SKCanvas canvas, SKPath headOutline, SKColor dark)
    {
        using var inner = new SKPath(headOutline);
        inner.Transform(SKMatrix.CreateScale(0.8f, 0.8f, 32, 32));
        using var band = ShapePainter.Outline(SvgParts.Read(look.Visor));

        canvas.Save();
        canvas.ClipPath(inner, SKClipOperation.Intersect, antialias: true);
        canvas.ClipPath(band, SKClipOperation.Intersect, antialias: true);
        canvas.DrawColor(dark);

        var lit = Tokens.Color(this, "DvVisorLit").ToSkia();
        switch (state)
        {
            case AvatarState.Thinking:
                canvas.DrawColor(lit.WithAlpha((byte)(255 * Breath())), SKBlendMode.SrcOver);
                break;
            case AvatarState.Writing:
                canvas.DrawColor(lit, SKBlendMode.SrcOver);
                break;
            case AvatarState.Tool:
                canvas.DrawColor(lit.WithAlpha(DimmedAlpha), SKBlendMode.SrcOver);
                using (var bar = new SKPaint { Color = Tokens.Color(this, "DvOnHead").ToSkia() })
                {
                    canvas.DrawRect(Sweep(), 0, SweepWidth, Grid, bar);
                }

                break;
            case AvatarState.Failed:
                canvas.DrawColor(Tokens.Color(this, "DvVisorFailed").ToSkia(), SKBlendMode.SrcOver);
                break;
        }

        if (ThemeTokens.Current.Characters.Visor is VisorStyle.Outlined)
        {
            DrawEdge(canvas, band);
        }

        canvas.Restore();
    }

    // A theme without bloom draws the visor's edge so it reads without light: lit while the bot works, pale at rest.
    private void DrawEdge(SKCanvas canvas, SKPath band)
    {
        var working = state is AvatarState.Thinking or AvatarState.Writing or AvatarState.Tool;
        var edge = Tokens.Color(this, working ? "DvVisorLit" : "DvOnHead").ToSkia();
        using var stroke = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = EdgeWidth,
            Color = edge.WithAlpha(EdgeAlpha),
            IsAntialias = true,
        };
        canvas.DrawPath(band, stroke);
    }

    // A slow breath between dim and full, eased at both ends. Fully lit with reduced motion.
    private double Breath()
    {
        if (Motion.IsReduced)
        {
            return 1;
        }

        if (Motion.Fades)
        {
            var faded = Math.Min(1, elapsed.Elapsed.TotalMilliseconds / Motion.FadeTime.TotalMilliseconds);
            return Dim + ((1 - Dim) * faded);
        }

        var turn = elapsed.Elapsed.TotalMilliseconds / Tokens.Number("DvDurBreath") % 1;
        return Dim + ((1 - Dim) * (0.5 - (0.5 * Math.Cos(turn * 2 * Math.PI))));
    }

    // The bar's place on its way across. It holds still, on the visor, with reduced motion.
    private float Sweep()
    {
        if (!Motion.Loops)
        {
            return SweepAtRest;
        }

        var turn = (float)(elapsed.Elapsed.TotalMilliseconds / Tokens.Number("DvDurTravel") % 1);
        return SweepFrom + ((SweepTo - SweepFrom) * turn);
    }

    #endregion
}
