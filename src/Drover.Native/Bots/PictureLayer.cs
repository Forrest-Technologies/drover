namespace Drover.Native.Bots;

/// <summary>
/// Draws the sandbox's screen as the server sent it, and over it the ring that marks where the next keyboard click
/// lands. The ring moves only when a person moves it, and keeps its size at any zoom.
/// </summary>
internal sealed class PictureLayer : SKCanvasElement
{
    #region Private Fields

    private byte[]? shown;

    private SKImage? image;

    private (int X, int Y)? pointerAt;

    private int screenWidth = 1;

    private int screenHeight = 1;

    private double zoom = 1;

    #endregion

    #region Constructors

    public PictureLayer()
    {
        IsHitTestVisible = false;
        Unloaded += (_, _) => Forget();
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Shows a picture, decoded once for as long as it is the one shown, with the pointer at a pixel of a screen
    /// <paramref name="width"/> by <paramref name="height"/>, or without one.
    /// </summary>
    public void Show(byte[]? png, (int X, int Y)? pointer, int width, int height, double scale)
    {
        if (!ReferenceEquals(png, shown))
        {
            Forget();
            shown = png;
            image = png is null ? null : SKImage.FromEncodedData(png);
        }

        (pointerAt, screenWidth, screenHeight, zoom) = (pointer, width, height, scale);
        Invalidate();
    }

    #endregion

    #region Private Methods

    protected override AutomationPeer OnCreateAutomationPeer() => new RolePeer(this, AutomationControlType.Image);

    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        var (width, height) = ((float)area.Width, (float)area.Height);

        if (image is not null)
        {
            canvas.DrawImage(image, new SKRect(0, 0, width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        }

        if (pointerAt is { } point)
        {
            var (left, top) = ScreenPoint.Percent(point, screenWidth, screenHeight);
            var stroke = (float)(Tokens.Number("DvFocusWidth") / zoom);
            var radius = (float)(Tokens.Number("DvSpace4") / 2 / zoom) - (stroke / 2);
            using var ring = new SKPaint
            {
                Color = Tokens.Color(this, "DvEmber").ToSkia(),
                IsAntialias = true,
                IsStroke = true,
                StrokeWidth = stroke,
            };
            canvas.DrawCircle((float)(left / 100 * width), (float)(top / 100 * height), radius, ring);
        }
    }

    private void Forget()
    {
        image?.Dispose();
        (image, shown) = (null, null);
    }

    #endregion
}
