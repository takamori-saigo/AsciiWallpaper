namespace AsciiWallpaper.Engine;

/// <summary>Что лежит в одной ячейке сетки.</summary>
public struct Cell
{
    public float R, G, B;     // цвет 0..1
    public float Bright;      // 0..1 — от неё зависит «плотность» символа
    public bool Sparkle;      // мерцающая белая искорка

    // «Распускание»: часть рисунка, которая растёт из маленькой копии вокруг точки (Gx, Gy).
    public int Group;         // 0 — нет, 1 — цветок (из узкого бутона), 2 — лист (равномерно)
    public float Gx, Gy;      // точка роста
    public float GStart, GDur;// когда растёт (доля от времени сборки)

    // Свой порядок появления (для дождя): 0 — символ падает первым, 1 — последним.
    public bool HasOrder;
    public float Order;
}

public enum AssembleOrder { Radial, BottomUp }

/// <summary>Как появляется рисунок.</summary>
public enum AssembleStyle
{
    Burst, // из маленькой точки
    Rain,  // символы падают сверху каждый на своё место
    Seed,  // все символы падают в одну точку, из неё всё прорастает и распускается
}

/// <summary>
/// Сцена = функция «есть ли в точке (x, y) что-то и какого оно цвета».
/// Координаты изотропные: y ∈ [-1, 1] снизу вверх, x ∈ [-Aspect, Aspect].
/// </summary>
public abstract class Scene
{
    public abstract string Id { get; }
    public abstract string Name { get; }
    public virtual float Aspect => 1f;
    /// <summary>Откуда «вырастает» рисунок.</summary>
    public virtual (float X, float Y) Origin => (0f, 0f);
    public virtual AssembleOrder Order => AssembleOrder.Radial;

    // ---------- характер анимации (переопределяется спокойными сценами) ----------
    /// <summary>Множитель размера символов: меньше — детальнее.</summary>
    public virtual float DetailScale => 1f;
    /// <summary>Множитель размера рисунка относительно настройки «Размер рисунка».</summary>
    public virtual float SizeBoost => 1f;
    /// <summary>Прижать рисунок к нижнему краю экрана (стебли растут от самого низа при любом размере).</summary>
    public virtual bool AnchorBottom => false;
    /// <summary>Спокойная сцена: без бегущего блика и мерцания, мягкое исчезновение.</summary>
    public virtual bool Calm => false;
    public virtual AssembleStyle Assemble => AssembleStyle.Burst;
    /// <summary>Сколько белых «капель» падает на рисунок в спокойной фазе.</summary>
    public virtual int AmbientDrops => 0;
    public virtual float AssembleSeconds => 3.6f;
    /// <summary>Скорость падения символов при сборке дождём (доля высоты экрана в секунду).</summary>
    public virtual float FallSpeed => 0.42f;
    public virtual float DisperseSeconds => 3.4f;

    /// <summary>Вызывается перед построением: сцена может взять нужные ей настройки.</summary>
    public virtual void Configure(Settings s) { }

    public virtual void BeginSample(int cols, int rows) { }
    public abstract bool Sample(float x, float y, int col, int row, out Cell cell);

    /// <summary>Движение в спокойной фазе (взмахи крыльев, покачивание…).</summary>
    public virtual void Animate(ref float x, ref float y, float t) { }

    // ---------- общие утилиты ----------
    protected static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;
    protected static float Lerp(float a, float b, float t) => a + (b - a) * t;
    protected static float Smooth(float e0, float e1, float x)
    {
        float t = Clamp01((x - e0) / (e1 - e0));
        return t * t * (3 - 2 * t);
    }
    protected static float Frac(float v) => v - MathF.Floor(v);

    public static float Hash(int n)
    {
        unchecked
        {
            uint x = (uint)n;
            x ^= x >> 16; x *= 0x7feb352d;
            x ^= x >> 15; x *= 0x846ca68b;
            x ^= x >> 16;
            return (x & 0xFFFFFF) / 16777216f;
        }
    }
    protected static float Hash(int a, int b) => Hash(a * 73856093 ^ b * 19349663);

    protected static float EllipseSdf(float px, float py, float cx, float cy, float a, float b, float rot)
    {
        float dx = px - cx, dy = py - cy;
        float c = MathF.Cos(rot), s = MathF.Sin(rot);
        float x = dx * c + dy * s, y = -dx * s + dy * c;
        float k = MathF.Sqrt(x * x / (a * a) + y * y / (b * b));
        return (k - 1) * MathF.Min(a, b);
    }

    protected static float SMin(float a, float b, float k)
    {
        float h = Clamp01(0.5f + 0.5f * (b - a) / k);
        return Lerp(b, a, h) - k * h * (1 - h);
    }

    protected static float SegDist(float px, float py, float ax, float ay, float bx, float by)
    {
        float pax = px - ax, pay = py - ay, bax = bx - ax, bay = by - ay;
        float h = Clamp01((pax * bax + pay * bay) / (bax * bax + bay * bay));
        float dx = pax - bax * h, dy = pay - bay * h;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    protected static void Set(out Cell c, float r, float g, float b, bool sparkle = false)
    {
        c = new Cell
        {
            R = Clamp01(r), G = Clamp01(g), B = Clamp01(b),
            Bright = Clamp01(MathF.Max(r, MathF.Max(g, b))),
            Sparkle = sparkle,
        };
    }

    protected static void Rotate(ref float x, ref float y, float angle)
    {
        float c = MathF.Cos(angle), s = MathF.Sin(angle);
        float nx = x * c - y * s;
        y = x * s + y * c;
        x = nx;
    }
}

// =====================================================================
// Розы под дождём: медленный дождь из белых символов. Каждый символ падает на своё место
// и становится частью растения: стебель нарастает снизу вверх, потом бутон из середины,
// потом лепестки наружу. Растения вырастают по очереди, дождь кончается вместе с ростом.
// Спокойная и детальная сцена, стебли всегда начинаются у нижнего края экрана.
// =====================================================================
public sealed class RoseGardenScene : Scene
{
    public override string Id => "rose-garden";
    public override string Name => "Розы под дождём";
    public override float Aspect => 1.8f;
    public override (float X, float Y) Origin => (0f, -1f);
    public override AssembleOrder Order => AssembleOrder.BottomUp;
    public override bool AnchorBottom => true;

    public override float DetailScale => 0.45f;
    public override float SizeBoost => 1.1f;
    public override bool Calm => true;
    public override AssembleStyle Assemble => AssembleStyle.Rain;
    public override int AmbientDrops => 0;          // дождь кончается, как только всё выросло
    public override float AssembleSeconds => 55f;   // медленный дождь
    public override float FallSpeed => 0.2f;

    // ---------- палитры роз: основа, середина, светлый край ----------
    static readonly (float, float, float)[][] Palettes =
    {
        new[] { (0.84f, 0.22f, 0.48f), (0.97f, 0.50f, 0.70f), (1.0f, 0.86f, 0.93f) }, // розовая
        new[] { (0.90f, 0.14f, 0.42f), (1.0f, 0.38f, 0.62f), (1.0f, 0.78f, 0.88f) },   // ярко-розовая
        new[] { (0.86f, 0.42f, 0.60f), (0.99f, 0.70f, 0.82f), (1.0f, 0.93f, 0.96f) },  // нежно-розовая
        new[] { (0.66f, 0.08f, 0.30f), (0.88f, 0.28f, 0.50f), (1.0f, 0.70f, 0.82f) }, // тёмная
    };

    static readonly (float, float, float) Sepal = (0.2f, 0.55f, 0.25f);

    // голова: x, y, радиус, изгиб стебля, бутон?, палитра, очередь роста
    record struct Rose(float X, float Y, float R, float Bend, bool Bud, int Pal, int Slot);

    static readonly Rose[] Roses =
    {
        new(-1.05f, 0.30f, 0.25f, 0.10f, false, 0, 2),
        new(-0.55f, 0.62f, 0.27f, -0.08f, false, 1, 6),
        new(0.00f, 0.40f, 0.30f, 0.07f, false, 0, 0),
        new(0.55f, 0.68f, 0.26f, 0.09f, false, 2, 10),
        new(1.05f, 0.28f, 0.25f, -0.10f, false, 3, 4),
        new(-0.30f, 0.00f, 0.20f, 0.06f, false, 2, 8),
        new(0.33f, 0.04f, 0.21f, -0.06f, false, 1, 12),
        new(-1.42f, 0.05f, 0.12f, 0.06f, true, 1, 1),
        new(-0.88f, 0.80f, 0.11f, -0.05f, true, 0, 7),
        new(0.25f, 0.93f, 0.11f, 0.06f, true, 3, 11),
        new(0.88f, 0.90f, 0.10f, 0.05f, true, 1, 9),
        new(1.42f, 0.55f, 0.12f, -0.06f, true, 0, 3),
        new(-0.08f, 0.86f, 0.10f, -0.04f, true, 2, 5),
    };

    // сложный лист розы: какая роза, высота на стебле (0..1), угол, длина черешка
    static readonly (int Rose, float T, float Angle, float Len)[] Leaves =
    {
        (0, 0.35f, 2.5f, 0.3f), (0, 0.6f, 0.5f, 0.24f), (1, 0.45f, 2.6f, 0.28f), (2, 0.3f, 0.6f, 0.32f),
        (2, 0.55f, 2.5f, 0.26f), (3, 0.4f, 0.55f, 0.28f), (4, 0.35f, 0.7f, 0.3f), (4, 0.6f, 2.6f, 0.22f),
        (5, 0.45f, 2.4f, 0.24f), (6, 0.5f, 0.7f, 0.24f), (7, 0.4f, 2.7f, 0.18f), (11, 0.45f, 0.5f, 0.2f),
        (1, 0.75f, 0.6f, 0.18f), (3, 0.72f, 2.4f, 0.18f),
    };

    const float PlantLen = 0.3f;    // доля времени, за которую вырастает одно растение
    static float PlantStart(int slot) => 0.06f * slot;

    // ---------- настройки из приложения ----------
    Settings? _settings;
    float _narrow = 0.8f;           // ширина голов
    float _tilt = 1f;               // сила наклона в стороны
    float _leaf = 1f;               // размер листьев

    public override void Configure(Settings s)
    {
        _settings = s;
        _narrow = Math.Clamp(s.RoseWidth, 0.4f, 1.3f);
        _tilt = Math.Clamp(s.RoseTilt, 0f, 3f);
        _leaf = Math.Clamp(s.LeafSize, 0.2f, 2f);
    }

    // наклон головы: чуть в сторону от центра, но в целом розы растут вверх
    float Tilt(in Rose r) => Math.Clamp(r.X * 0.13f * _tilt, -0.18f * _tilt, 0.18f * _tilt);

    (float X, float Y) HeadBase(in Rose r)
    {
        float th = Tilt(r);
        return (r.X - 0.88f * r.R * MathF.Sin(th), r.Y - 0.88f * r.R * MathF.Cos(th));
    }

    float StemX(in Rose r, float y)
    {
        var (tx, ty) = HeadBase(r);
        float t = Clamp01((y + 1f) / (ty + 1f));
        return Lerp(r.X * 0.8f, tx, t) + r.Bend * MathF.Sin(t * MathF.PI);
    }

    static (float, float, float) Mix((float, float, float) a, (float, float, float) b, float t) =>
        (Lerp(a.Item1, b.Item1, t), Lerp(a.Item2, b.Item2, t), Lerp(a.Item3, b.Item3, t));
    static (float, float, float) Mul((float, float, float) a, float k) => (a.Item1 * k, a.Item2 * k, a.Item3 * k);

    // ------------------------------------------------------------------
    public override bool Sample(float x, float y, int col, int row, out Cell c)
    {
        (float, float, float)? res = null;
        float order = 0;

        // головы и бутоны
        for (int i = 0; i < Roses.Length && res == null; i++)
        {
            var r = Roses[i];
            float th = Tilt(r);
            float dx = (x - r.X) / r.R, dy = (y - r.Y) / r.R;
            float u = (dx * MathF.Cos(th) - dy * MathF.Sin(th)) / _narrow;
            float v = dx * MathF.Sin(th) + dy * MathF.Cos(th);
            if (MathF.Abs(u) > 1.3f || MathF.Abs(v) > 1.3f) continue;
            res = r.Bud ? Bud(u, v, r.Pal) : Head(u, v, r.Pal);
            if (res != null)
            {
                // голова нарастает от середины бутона наружу — после того, как вырос стебель
                float d = Clamp01(MathF.Sqrt(u * u + (v + 0.2f) * (v + 0.2f)) / 1.3f);
                order = PlantStart(r.Slot) + PlantLen * (0.45f + 0.55f * d);
            }
        }

        // листья
        for (int i = 0; i < Leaves.Length && res == null; i++)
        {
            if (CompoundLeaf(x, y, Leaves[i], out var leafCol, out float along))
            {
                var L = Leaves[i];
                res = leafCol;
                order = PlantStart(Roses[L.Rose].Slot) + PlantLen * 0.45f * L.T + 0.05f * along;
            }
        }

        // стебли с шипами
        for (int i = 0; i < Roses.Length && res == null; i++)
        {
            var r = Roses[i];
            float top = HeadBase(r).Y;
            if (y > top + 0.02f) continue;
            float sx = StemX(r, y);
            float w = r.Bud ? 0.011f : 0.015f;
            float d = x - sx;
            if (MathF.Abs(d) < w)
            {
                float light = d < -w * 0.2f ? 1.15f : d > w * 0.4f ? 0.8f : 1f; // объём: свет слева
                res = (0.22f * light, 0.58f * light, 0.27f * light);
            }
            else if (MathF.Abs(d) < w + 0.022f && Hash((int)((y + 2) * 55), (int)((r.X + 2) * 100)) < 0.07f)
                res = (0.4f, 0.65f, 0.3f); // шипы
            else continue;

            float t = Clamp01((y + 1f) / (top + 1f));
            order = PlantStart(r.Slot) + PlantLen * 0.45f * t; // стебель нарастает снизу вверх
        }

        if (res is not { } rgb) { c = default; return false; }
        Set(out c, rgb.Item1, rgb.Item2, rgb.Item3);
        c.HasOrder = true;
        c.Order = order;
        return true;
    }

    // ------------------------------------------------------------------
    // Роза сбоку: чашечка с четырьмя слоями лепестков (завитки, тени, складки, прожилки),
    // раскрытый верх со спиралью, отогнутые наружные лепестки, чашелистики.
    static readonly (float C, float A, float Tilt)[] CupEdges =
    {
        (0.02f, 0.24f, 0.06f), (-0.24f, 0.32f, -0.07f), (-0.48f, 0.30f, 0.05f), (-0.70f, 0.24f, -0.04f),
    };

    static readonly (float Cx, float Cy, float A, float B, float Rot, float Dark)[] OuterPetals =
    {
        (-0.74f, 0.04f, 0.34f, 0.52f, 0.45f, 0f), (0.74f, 0.04f, 0.34f, 0.52f, -0.45f, 0f),
        (-0.36f, 0.44f, 0.36f, 0.26f, 0.25f, 0.12f), (0.36f, 0.44f, 0.36f, 0.26f, -0.25f, 0.12f),
        (0f, 0.50f, 0.40f, 0.25f, 0f, 0.2f),
    };

    static (float, float, float)? Head(float u, float v, int pal)
    {
        var p = Palettes[pal];
        var baseC = p[0]; var mid = p[1]; var rim = p[2];
        const float rimY = 0.15f;

        // чашечка
        if (v <= rimY + 0.03f)
        {
            float w = 0.8f * MathF.Sqrt(MathF.Max(0, 1 - MathF.Pow((rimY - v) / 1.05f, 2)));
            if (MathF.Abs(u) < w)
            {
                float k = u / w;
                var col = Mix(Mul(baseC, 0.5f), baseC, Smooth(-0.95f, -0.3f, v));
                col = Mix(col, mid, 0.5f * (1 - MathF.Abs(k)) * Smooth(-0.85f, rimY, v));
                for (int i = 0; i < CupEdges.Length; i++)
                {
                    var ed = CupEdges[i];
                    float e = v - (ed.C + ed.A * k * k + ed.Tilt * k);
                    if (e >= 0 && e < 0.055f) return Mix(col, rim, 0.92f - 0.15f * i); // завиток края
                    if (e >= 0.055f && e < 0.075f) return Mix(col, mid, 0.5f);         // переход
                    if (e > -0.13f && e < 0) col = Mul(col, 0.66f);                   // тень под краем
                }
                // складки
                float kk = MathF.Abs(k + 0.06f * MathF.Sin(v * 6f));
                if ((MathF.Abs(kk - 0.32f) < 0.035f || MathF.Abs(kk - 0.66f) < 0.035f) && v < 0.05f) col = Mul(col, 0.72f);
                // прожилки
                if (MathF.Sin(k * 34f + v * 9f) > 0.93f) col = Mul(col, 0.86f);
                return col;
            }
        }

        // раскрытый верх со спиралью внутренних лепестков
        float ob = 0.36f + 0.025f * MathF.Sin(u * 14f);
        if (EllipseSdf(u, v, 0, rimY + 0.03f, 0.82f, ob, 0) < 0)
        {
            float uu = u / 0.82f, vv = (v - rimY - 0.03f) / ob;
            float rr = MathF.Sqrt(uu * uu + vv * vv), th = MathF.Atan2(vv, uu);
            float sp = MathF.Sin(th + rr * 12.5f);
            var col = Mix(Mul(baseC, 0.42f), baseC, 0.25f + 0.6f * rr);
            if (rr > 0.88f) return Mix(col, rim, 0.85f);
            if (sp > 0.72f) return Mix(col, rim, 0.55f + 0.3f * rr);
            if (sp > 0.5f) return Mix(col, mid, 0.5f);
            if (sp < -0.55f) return Mul(col, 0.45f);
            return col;
        }

        // отогнутые наружные лепестки
        foreach (var o in OuterPetals)
        {
            float dd = EllipseSdf(u, v, o.Cx, o.Cy, o.A, o.B, o.Rot);
            if (dd >= 0) continue;
            float depth = -dd / MathF.Min(o.A, o.B);
            var col = Mul(Mix(baseC, mid, 0.45f), 0.78f - o.Dark);
            if (depth < 0.2f) col = Mix(col, rim, 0.85f * (1 - depth / 0.2f));
            else if (depth < 0.32f) col = Mul(col, 0.75f); // складка у завитка
            if (MathF.Sin((u - o.Cx) * 30f + (v - o.Cy) * 12f) > 0.94f) col = Mul(col, 0.85f);
            return col;
        }

        // чашелистики
        if (v < -0.8f && (SegDist(u, v, 0, -0.82f, -0.55f, -1.12f) < 0.045f ||
                          SegDist(u, v, 0, -0.82f, 0.55f, -1.12f) < 0.045f ||
                          SegDist(u, v, 0, -0.82f, 0.12f, -1.25f) < 0.045f))
            return Sepal;
        return null;
    }

    static (float, float, float)? Bud(float u, float v, int pal)
    {
        var p = Palettes[pal];
        float d = EllipseSdf(u, v, 0, 0.08f, 0.4f, 0.68f, 0);
        if (d < 0)
        {
            float depth = -d / 0.4f;
            var col = Mix(Mul(p[0], 0.55f), p[1], Smooth(-0.6f, 0.75f, v));
            if (depth < 0.18f) col = Mix(col, p[2], 0.7f * (1 - depth / 0.18f));
            if (MathF.Abs(u - 0.3f * v + 0.05f) < 0.045f || MathF.Abs(u + 0.25f * v - 0.12f) < 0.04f) col = Mul(col, 0.55f);
            return col;
        }
        if (v < 0.3f && (SegDist(u, v, 0, -0.75f, -0.42f, 0.25f) < 0.075f ||
                         SegDist(u, v, 0, -0.75f, 0.42f, 0.25f) < 0.075f ||
                         SegDist(u, v, 0, -0.75f, 0.05f, 0.35f) < 0.075f))
            return (0.24f, 0.62f, 0.3f);
        if (v < -0.6f && MathF.Abs(u) < 0.12f) return (0.24f, 0.6f, 0.3f);
        return null;
    }

    // ------------------------------------------------------------------
    // Лист розы: черешок + верхушечный листочек + две пары боковых. Мелкий и приглушённый,
    // чтобы акцент оставался на цветах.
    bool CompoundLeaf(float x, float y, (int Rose, float T, float Angle, float Len) L,
        out (float, float, float) col, out float along)
    {
        col = default; along = 0;
        var r = Roses[L.Rose];
        float ay = Lerp(-1f, HeadBase(r).Y, L.T);
        float ax = StemX(r, ay);
        float ln = L.Len * 0.7f * _leaf;
        float lf = ln * 0.55f;
        float cs = MathF.Cos(L.Angle), sn = MathF.Sin(L.Angle);
        float ex = ax + cs * ln, ey = ay + sn * ln;

        // быстрый отсев
        if (MathF.Abs(x - ax) > ln + lf + 0.05f || MathF.Abs(y - ay) > ln + lf + 0.05f) return false;

        along = Clamp01(MathF.Sqrt((x - ax) * (x - ax) + (y - ay) * (y - ay)) / (ln + lf));

        if (Leaflet(x, y, ex, ey, L.Angle, lf, out col)) return true;
        foreach (var (f, da) in LeafletPairs)
        {
            float px = ax + cs * ln * f, py = ay + sn * ln * f;
            if (Leaflet(x, y, px, py, L.Angle + da, lf * 0.85f, out col)) return true;
            if (Leaflet(x, y, px, py, L.Angle - da, lf * 0.85f, out col)) return true;
        }
        if (SegDist(x, y, ax, ay, ex, ey) < 0.01f) { col = (0.25f, 0.6f, 0.3f); return true; }
        return false;
    }

    static readonly (float F, float Da)[] LeafletPairs = { (0.75f, 0.9f), (0.45f, 1.0f) };

    static bool Leaflet(float x, float y, float lx, float ly, float ang, float len, out (float, float, float) col)
    {
        col = default;
        float cs = MathF.Cos(ang), sn = MathF.Sin(ang);
        float dx = x - lx, dy = y - ly;
        float t = (dx * cs + dy * sn) / len, n = (-dx * sn + dy * cs) / len;
        if (t < 0 || t > 1) return false;
        float w = 0.4f * MathF.Pow(MathF.Sin(MathF.PI * t), 0.75f) * (1 - 0.2f * t);
        float serr = 0.03f * MathF.Abs(MathF.Sin(t * 18f)); // зубчатый край
        if (MathF.Abs(n) > w - serr) return false;
        float k = MathF.Abs(n) / MathF.Max(w, 1e-3f);
        col = Mul(Mix((0.36f, 0.82f, 0.42f), (0.08f, 0.38f, 0.17f), k * 0.85f), 0.78f);
        if (n < 0) col = Mul(col, 0.72f);
        if (MathF.Abs(n) < 0.03f) { col = (0.55f, 0.9f, 0.6f); return true; }        // центральная жилка
        if (MathF.Abs(MathF.Sin(t * 9f - k * 2.4f)) < 0.12f) col = Mul(col, 0.68f);  // боковые жилки
        return true;
    }

    public override void Animate(ref float x, ref float y, float t)
    {
        // ветер: розы покачиваются тем сильнее, чем выше; порывы то усиливаются, то стихают,
        // соседние растения качаются не в такт (фаза зависит от x)
        float wind = Math.Clamp(_settings?.Wind ?? 1f, 0f, 3f);
        if (wind <= 0) return;
        float h = MathF.Max(0f, y + 1f);
        float gust = 0.6f + 0.4f * MathF.Sin(t * 0.23f);
        float sway = MathF.Sin(t * 0.6f + x * 0.5f + y * 0.9f) + 0.35f * MathF.Sin(t * 1.4f + x * 0.8f + y * 2f);
        float bob = MathF.Sin(t * 0.8f + x * 0.7f);
        x += wind * 0.011f * MathF.Pow(h, 1.5f) * gust * sway;
        y += wind * 0.003f * h * bob;
    }
}
