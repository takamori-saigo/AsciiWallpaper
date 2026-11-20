using SkiaSharp;

namespace AsciiWallpaper.Engine;

/// <summary>
/// Один раз рисует все печатные ASCII-символы белым цветом в текстуру.
/// Потом каждый символ на экране — это просто спрайт из этой текстуры,
/// перекрашенный в нужный цвет (SKCanvas.DrawAtlas). Так тысячи символов рисуются за один вызов.
/// </summary>
public sealed class GlyphAtlas
{
    /// <summary>Печатный ASCII + кириллица (чтобы в режиме «Слово» можно было писать по-русски).</summary>
    public static readonly string Charset = BuildCharset();
    static readonly int[] Map = BuildMap();

    static string BuildCharset()
    {
        var sb = new System.Text.StringBuilder();
        for (int c = 33; c <= 126; c++) sb.Append((char)c);
        for (int c = 0x410; c <= 0x44F; c++) sb.Append((char)c);
        sb.Append('Ё').Append('ё');
        return sb.ToString();
    }

    static int[] BuildMap()
    {
        var map = new int[0x460];
        Array.Fill(map, -1);
        for (int i = 0; i < Charset.Length; i++) map[Charset[i]] = i;
        return map;
    }

    public SKImage Image { get; }
    public SKRect[] Rects { get; }
    /// <summary>Ширина/высота ячейки сетки в DIP.</summary>
    public float CellW { get; }
    public float CellH { get; }
    /// <summary>Во сколько раз атлас крупнее, чем символ на экране (для Retina).</summary>
    public float Scale { get; }

    readonly SKBitmap _bitmap; // держим живым, SKImage может ссылаться на его пиксели

    static readonly Dictionary<(int, int), GlyphAtlas> Cache = new();

    public static GlyphAtlas Get(float fontSize, float renderScale)
    {
        var key = ((int)MathF.Round(fontSize * 4), (int)MathF.Round(renderScale * 4));
        lock (Cache)
        {
            if (!Cache.TryGetValue(key, out var a))
                Cache[key] = a = new GlyphAtlas(fontSize, MathF.Max(1f, renderScale));
            return a;
        }
    }

    GlyphAtlas(float fontSize, float scale)
    {
        Scale = scale;
        var typeface = SKTypeface.FromFamilyName("Menlo", SKFontStyle.Bold)
                       ?? SKTypeface.FromFamilyName("Courier New", SKFontStyle.Bold)
                       ?? SKTypeface.Default;

        using var paint = new SKPaint
        {
            Typeface = typeface,
            TextSize = fontSize * scale,
            IsAntialias = true,
            Color = SKColors.White,
            SubpixelText = true,
        };

        float advance = paint.MeasureText("M");
        var m = paint.FontMetrics;
        float lineH = m.Descent - m.Ascent;

        const int pad = 2;
        int gw = (int)MathF.Ceiling(advance) + pad * 2;
        int gh = (int)MathF.Ceiling(lineH) + pad * 2;
        int count = Charset.Length;
        const int perRow = 16;
        int rows = (count + perRow - 1) / perRow;

        _bitmap = new SKBitmap(perRow * gw, rows * gh, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(_bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            Rects = new SKRect[count];
            for (int i = 0; i < count; i++)
            {
                int cx = (i % perRow) * gw, cy = (i / perRow) * gh;
                canvas.DrawText(Charset[i].ToString(), cx + pad, cy + pad - m.Ascent, paint);
                Rects[i] = SKRect.Create(cx, cy, gw, gh);
            }
        }
        _bitmap.SetImmutable();
        Image = SKImage.FromBitmap(_bitmap);

        CellW = advance / scale;
        CellH = lineH * 0.92f / scale; // чуть плотнее строки, как в ASCII-арте
    }

    /// <summary>Индекс символа в атласе или -1 для пробела/неподдерживаемого символа.</summary>
    public static int Index(char c) => c < Map.Length ? Map[c] : -1;
}
