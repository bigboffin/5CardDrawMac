using Avalonia.Media.Imaging;
using SkiaSharp;
using Svg.Skia;

namespace FiveCardDraw;

public static class CardImageProvider
{
    private static readonly Dictionary<string, Bitmap> _cache = new();
    private static Bitmap? _backImage;

    public static Action<string>? Log { get; set; }

    private static readonly string SvgPath =
        Path.Combine(AppContext.BaseDirectory, "Assets", "svg-cards");

    private static readonly string PngPath =
        Path.Combine(AppContext.BaseDirectory, "Assets", "png-cards");

    public static Bitmap GetCardImage(Card card, int width, int height)
    {
        string key = $"{card.Rank.ToLower()}_of_{card.Suit.ToLower()}_{width}x{height}";
        if (!_cache.TryGetValue(key, out Bitmap? img))
        {
            string file = $"{card.Rank.ToLower()}_of_{card.Suit.ToLower()}.svg";
            string full = Path.Combine(SvgPath, file);
            try
            {
                img = LoadSvgAsBitmap(full, width, height);
                Log?.Invoke($"[CARD] loaded {file} → {width}x{height}");
            }
            catch (Exception ex)
            {
                Log?.Invoke($"[CARD ERROR] {file}: {ex.GetType().Name}: {ex.Message}");
                img = MakePlaceholder(card, width, height);
            }
            _cache[key] = img;
        }
        return img;
    }

    public static Bitmap GetCardBack()
    {
        _backImage ??= new Bitmap(Path.Combine(PngPath, "back.png"));
        return _backImage;
    }

    private static Bitmap LoadSvgAsBitmap(string path, int width, int height)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("SVG not found", path);

        var svg = new SKSvg();
        svg.Load(path);

        var picture = svg.Picture
            ?? throw new InvalidOperationException("Failed to parse SVG");

        var info = new SKImageInfo(width, height);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        float scaleX = (float)width / picture.CullRect.Width;
        float scaleY = (float)height / picture.CullRect.Height;
        float scale = Math.Min(scaleX, scaleY);
        canvas.Scale(scale, scale);
        canvas.DrawPicture(picture);

        using var skImage = surface.Snapshot();
        using var data = skImage.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(data.ToArray());
        return new Bitmap(stream);
    }

    private static Bitmap MakePlaceholder(Card card, int width, int height)
    {
        var info = new SKImageInfo(width, height);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        using var borderPaint = new SKPaint
        {
            Color = SKColors.Black,
            StrokeWidth = 2,
            Style = SKPaintStyle.Stroke
        };
        canvas.DrawRect(1, 1, width - 2, height - 2, borderPaint);

        SKColor textColor = card.Suit is "Hearts" or "Diamonds" ? SKColors.Red : SKColors.Black;
        using var textPaint = new SKPaint
        {
            Color = textColor,
            TextSize = Math.Max(8f, height / 10f),
            IsAntialias = true
        };
        canvas.DrawText(card.Rank, 4, textPaint.TextSize + 4, textPaint);
        canvas.DrawText(card.Suit[..1], 4, textPaint.TextSize * 2 + 8, textPaint);

        using var skImage = surface.Snapshot();
        using var data = skImage.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(data.ToArray());
        return new Bitmap(stream);
    }
}
