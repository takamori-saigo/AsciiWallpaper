using SkiaSharp;

namespace AsciiWallpaper.Engine;

public struct Particle
{
    public float Hx, Hy;        // «дом» в координатах сцены
    public float Dx, Dy;        // где символ сейчас на экране (px)
    public float Vx, Vy;        // скорость при распаде
    public float R, G, B;       // базовый цвет
    public float Cr, Cg, Cb, A; // итоговый цвет кадра
    public float Size;
    public int BaseGlyph, Glyph;
    public float Seed;
    public float Delay;         // задержка сборки 0..1
    public float DDelay;        // задержка распада 0..1
    public float Flash;         // вспышка белым, когда на символ упала капля
    public int Group;           // 0 — нет, 1 — цветок, 2 — лист (см. Cell)
    public float Gx, Gy, GStart, GDur;
    public bool Released;
    public bool Sparkle;
}

/// <summary>Белый символ, падающий сверху на рисунок в спокойной фазе.</summary>
public struct Drop
{
    public float X, Y, Speed;
    public int Target;          // индекс частицы, на которую упадёт
    public int Glyph;
    public float Alpha;
}

enum Phase { Assemble, Hold, Disperse }

enum DisperseStyle { Wind, Collapse, Explode, Fade }

/// <summary>Кадр для отрисовки: массивы для SKCanvas.DrawAtlas.</summary>
public sealed class Frame
{
    public SKRect[] Sprites = Array.Empty<SKRect>();
    public SKRotationScaleMatrix[] Xforms = Array.Empty<SKRotationScaleMatrix>();
    public SKColor[] Colors = Array.Empty<SKColor>();
    public SKImage? Atlas;
    public bool Glow;
    public float GlowRadius;
    public int Count => Sprites.Length;

    public void Ensure(int n)
    {
        if (Sprites.Length == n) return;
        Sprites = new SKRect[n];
        Xforms = new SKRotationScaleMatrix[n];
        Colors = new SKColor[n];
    }
}

/// <summary>
/// Анимация: точка (или дождь из символов) → рисунок собирается → «живёт» → исчезает → следующая сцена.
/// </summary>
public sealed class AsciiEngine
{
    const string RampLong = ".'`^\",:;Il!i><~+_-?][}{1)(|/tfjrxnuvczXYUJCLQ0OZmwqpdbkhao*#MW&8%B@$";
    const string RampShort = ".:-=+*#%@";
    const string Dust = ".'`:*+";
    const string RainChars = "01|:.*+'`!ilI";

    const float GapDur = 0.7f;

    readonly Settings _s;
    readonly Random _rng = new();

    Particle[] _ps = Array.Empty<Particle>();
    Drop[] _drops = Array.Empty<Drop>();
    int[] _colTop = Array.Empty<int>();       // для каждого столбца — индекс самой верхней частицы
    int[] _rampIdx = Array.Empty<int>();
    readonly int[] _scrambleIdx;
    readonly int[] _dustIdx;
    readonly int[] _rainIdx;

    Scene? _scene;
    Phase _phase;
    DisperseStyle _style;
    float _pt, _time;
    float _w, _h, _renderScale = 2f;
    float _unit, _cx, _cy;
    float _fallSpeed;
    GlyphAtlas? _atlas;
    int _builtVersion = -1;
    bool _needBuild = true;

    /// <summary>Если задано — показываем только эту сцену (превью в галерее).</summary>
    public Scene? FixedScene { get; init; }
    /// <summary>Время показа для превью (иначе берётся из настроек).</summary>
    public float? HoldOverride { get; init; }
    /// <summary>Множитель размера шрифта (превью мельче).</summary>
    public float FontScale { get; init; } = 1f;

    public int Count => _ps.Length + _drops.Length;
    public string? CurrentSceneId => _scene?.Id;

    float AssembleDur => _scene?.AssembleSeconds ?? 3.6f;
    float DisperseDur => _scene?.DisperseSeconds ?? 3.4f;

    public AsciiEngine(Settings s)
    {
        _s = s;
        _scrambleIdx = ToIdx(RampLong);
        _dustIdx = ToIdx(Dust);
        _rainIdx = ToIdx(RainChars);
    }

    static int[] ToIdx(string chars) =>
        chars.Select(GlyphAtlas.Index).Where(i => i >= 0).DefaultIfEmpty(GlyphAtlas.Index('*')).ToArray();

    public void SetViewport(float w, float h, float renderScale)
    {
        if (MathF.Abs(w - _w) < 0.5f && MathF.Abs(h - _h) < 0.5f && MathF.Abs(renderScale - _renderScale) < 0.01f) return;
        _w = w; _h = h; _renderScale = renderScale;
        _needBuild = true;
    }

    public void Restart() => _needBuild = true;

    // ------------------------------------------------------------------
    // Построение сетки частиц
    // ------------------------------------------------------------------
    void Build(Scene scene)
    {
        _scene = scene;
        _builtVersion = _s.Version;
        _needBuild = false;
        _phase = Phase.Assemble;
        _pt = 0;
        _drops = Array.Empty<Drop>();

        if (_w < 10 || _h < 10) { _ps = Array.Empty<Particle>(); return; }

        _atlas = GlyphAtlas.Get(MathF.Max(3f, _s.FontSize * FontScale * scene.DetailScale), _renderScale);
        float cw = _atlas.CellW, ch = _atlas.CellH;

        float boxH = _h * Math.Clamp(_s.SceneSize * scene.SizeBoost, 0.2f, 0.98f);
        float boxW = boxH * scene.Aspect;
        if (boxW > _w * 0.96f) { boxW = _w * 0.96f; boxH = boxW / scene.Aspect; }
        _unit = boxH / 2f;
        _cx = _w / 2f;
        _cy = scene.AnchorBottom ? _h - boxH / 2f : _h / 2f;
        _fallSpeed = _h * MathF.Max(0.02f, scene.FallSpeed);

        int cols = Math.Max(1, (int)(boxW / cw));
        int rows = Math.Max(1, (int)(boxH / ch));
        float ox = _cx - cols * cw / 2f;
        float oy = scene.AnchorBottom ? _h - rows * ch : _cy - rows * ch / 2f; // нижняя строка — у самого края

        string ramp = _s.CharMode switch
        {
            CharMode.RampShort => RampShort,
            CharMode.Binary => "01",
            _ => RampLong,
        };
        _rampIdx = ToIdx(ramp);
        string word = string.IsNullOrWhiteSpace(_s.Word) ? "ascii" : _s.Word.Trim() + " ";

        scene.Configure(_s);
        scene.BeginSample(cols, rows);
        var list = new List<Particle>(cols * rows / 2);
        _colTop = new int[cols];
        Array.Fill(_colTop, -1);

        for (int j = 0; j < rows; j++)
        for (int i = 0; i < cols; i++)
        {
            float px = ox + (i + 0.5f) * cw, py = oy + (j + 0.5f) * ch;
            float x = (px - _cx) / _unit, y = -(py - _cy) / _unit;
            if (!scene.Sample(x, y, i, j, out var cell)) continue;

            int glyph;
            if (_s.CharMode == CharMode.Word)
            {
                glyph = GlyphAtlas.Index(word[(i + j * 3) % word.Length]);
                if (glyph < 0) continue; // пробел между словами
            }
            else
            {
                float b = 0.25f + 0.75f * cell.Bright;
                glyph = _rampIdx[Math.Clamp((int)(b * (_rampIdx.Length - 1) + 0.5f), 0, _rampIdx.Length - 1)];
            }

            if (_colTop[i] < 0) _colTop[i] = list.Count; // строки идут сверху вниз → первая встреченная = верхняя

            float seed = Scene.Hash(i * 7919 + j * 104729 + 17);
            list.Add(new Particle
            {
                Hx = x, Hy = y,
                R = cell.R, G = cell.G, B = cell.B,
                BaseGlyph = glyph, Glyph = glyph,
                Seed = seed,
                Sparkle = cell.Sparkle,
                Size = 1f,
                Group = cell.Group, Gx = cell.Gx, Gy = cell.Gy, GStart = cell.GStart, GDur = MathF.Max(0.01f, cell.GDur),
                Delay = cell.HasOrder ? cell.Order : -1f,
            });
        }

        _ps = list.ToArray();

        // порядок сборки
        var (oxs, oys) = scene.Origin;
        float maxD = 0.001f, minY = float.MaxValue, maxY = float.MinValue;
        foreach (ref readonly var p in _ps.AsSpan())
        {
            maxD = MathF.Max(maxD, Dist(p.Hx, p.Hy, oxs, oys));
            minY = MathF.Min(minY, p.Hy); maxY = MathF.Max(maxY, p.Hy);
        }
        float jitter = scene.Assemble switch
        {
            AssembleStyle.Rain => 0.25f, // у дождя порядок «рыхлее», не сплошная линия
            AssembleStyle.Seed => 0.1f,
            _ => 0.08f,
        };
        // если сцена сама задала порядок — растягиваем его на всё время сборки
        bool ownOrder = _ps.Length > 0 && _ps[0].Delay >= 0;
        float maxOrder = 0.001f;
        if (ownOrder) foreach (ref readonly var p in _ps.AsSpan()) maxOrder = MathF.Max(maxOrder, p.Delay);

        for (int k = 0; k < _ps.Length; k++)
        {
            ref var p = ref _ps[k];
            if (ownOrder)
            {
                p.Delay = Math.Clamp(p.Delay / maxOrder * 0.97f + p.Seed * 0.03f, 0f, 1f);
                p.Dx = _cx + oxs * _unit;
                p.Dy = -100;
                continue;
            }
            float d = scene.Order == AssembleOrder.BottomUp
                ? (p.Hy - minY) / MathF.Max(0.001f, maxY - minY)
                : Dist(p.Hx, p.Hy, oxs, oys) / maxD;
            p.Delay = Math.Clamp(d * (1 - jitter) + p.Seed * jitter, 0f, 1f);
            p.Dx = _cx + oxs * _unit;
            p.Dy = _cy - oys * _unit;
        }

        // капли для спокойной фазы
        if (scene.AmbientDrops > 0 && _ps.Length > 0)
        {
            _drops = new Drop[scene.AmbientDrops];
            for (int k = 0; k < _drops.Length; k++) SpawnDrop(ref _drops[k], initial: true);
        }
    }

    static float Dist(float ax, float ay, float bx, float by) => MathF.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

    void SpawnDrop(ref Drop d, bool initial)
    {
        int col;
        int tries = 0;
        do col = _rng.Next(_colTop.Length); while (_colTop[col] < 0 && ++tries < 50);
        d.Target = _colTop[col] >= 0 ? _colTop[col] : 0;
        ref var p = ref _ps[d.Target];
        d.X = _cx + p.Hx * _unit;
        float ch = _atlas?.CellH ?? 14;
        d.Y = initial ? -ch - (float)_rng.NextDouble() * _h : -ch - (float)_rng.NextDouble() * _h * 0.3f;
        d.Speed = _fallSpeed * (0.45f + 0.35f * (float)_rng.NextDouble());
        d.Glyph = _rainIdx[_rng.Next(_rainIdx.Length)];
        d.Alpha = 0.55f + 0.35f * (float)_rng.NextDouble();
    }

    Scene TargetScene()
    {
        if (FixedScene != null) return FixedScene;
        return SceneLibrary.Find(_s.CurrentSceneId);
    }

    void BeginDisperse()
    {
        _phase = Phase.Disperse;
        _pt = 0;
        if (_scene?.Calm == true) _style = DisperseStyle.Fade;
        else
        {
            var styles = new[] { DisperseStyle.Wind, DisperseStyle.Wind, DisperseStyle.Collapse, DisperseStyle.Explode };
            _style = styles[_rng.Next(styles.Length)];
        }

        float minX = float.MaxValue, maxX = float.MinValue, maxR = 0.001f;
        foreach (ref readonly var p in _ps.AsSpan())
        {
            minX = MathF.Min(minX, p.Hx); maxX = MathF.Max(maxX, p.Hx);
            maxR = MathF.Max(maxR, MathF.Sqrt(p.Hx * p.Hx + p.Hy * p.Hy));
        }
        for (int k = 0; k < _ps.Length; k++)
        {
            ref var p = ref _ps[k];
            p.Released = false;
            p.Vx = p.Vy = 0;
            float d = _style switch
            {
                DisperseStyle.Wind => 1f - (p.Hx - minX) / MathF.Max(0.001f, maxX - minX),
                DisperseStyle.Collapse => 1f - MathF.Sqrt(p.Hx * p.Hx + p.Hy * p.Hy) / maxR,
                DisperseStyle.Fade => p.Seed,
                _ => MathF.Sqrt(p.Hx * p.Hx + p.Hy * p.Hy) / maxR,
            };
            p.DDelay = Math.Clamp(d * 0.85f + p.Seed * 0.15f, 0f, 1f);
        }
    }

    // ------------------------------------------------------------------
    // Кадр анимации
    // ------------------------------------------------------------------
    public void Update(float dt)
    {
        dt = MathF.Min(dt, 0.1f);
        float adt = dt * Math.Clamp(_s.Speed, 0.1f, 5f);

        if (_needBuild || _builtVersion != _s.Version || _scene == null)
        {
            // если рисунок уже вырос, а поменяли настройку формы — перестраиваем мгновенно, без нового дождя
            var target = TargetScene();
            bool keepHold = _scene == target && _phase == Phase.Hold;
            float heldFor = _pt;
            Build(target);
            if (keepHold) { _phase = Phase.Hold; _pt = heldFor; }
        }

        _time += adt;
        _pt += _phase == Phase.Hold ? dt : adt;

        switch (_phase)
        {
            case Phase.Assemble:
                if (_pt >= AssembleDur) { _phase = Phase.Hold; _pt = 0; }
                else if (FixedScene == null && _scene!.Id != _s.CurrentSceneId && _pt > 0.5f) BeginDisperse();
                break;

            case Phase.Hold:
                if (FixedScene == null && _scene!.Id != _s.CurrentSceneId)
                {
                    BeginDisperse();
                    break;
                }
                float hold = HoldOverride ?? _s.HoldSeconds;
                if (_scene!.Calm && HoldOverride == null) hold *= 1.5f; // спокойные сцены показываем дольше
                if (_pt >= hold)
                {
                    if (FixedScene != null) BeginDisperse();
                    else switch (_s.PlayMode)
                    {
                        case PlayMode.Cycle:
                        case PlayMode.Shuffle:
                            SceneLibrary.Advance(_scene.Id); // остальные мониторы подхватят смену сами
                            BeginDisperse();
                            break;
                        case PlayMode.Repeat:
                            BeginDisperse();
                            break;
                        case PlayMode.Static:
                            _pt = 0; // висит бесконечно
                            break;
                    }
                }
                break;

            case Phase.Disperse:
                if (_pt >= DisperseDur + GapDur)
                {
                    Build(TargetScene());
                    return;
                }
                break;
        }

        UpdateParticles(adt);
        UpdateDrops(adt);
    }

    void UpdateParticles(float dt)
    {
        if (_scene == null) return;
        var scene = _scene;
        bool calm = scene.Calm;
        var (oxs, oys) = scene.Origin;
        float originX = _cx + oxs * _unit, originY = _cy - oys * _unit;
        float cw = _atlas?.CellW ?? 7, ch = _atlas?.CellH ?? 14;
        int tick = (int)(_time * (calm ? 4f : 9f));
        float A = AssembleDur, D = DisperseDur;

        // блик, пробегающий по рисунку слева направо (только у «живых» сцен)
        float sweep = ((_time * 0.22f) % 3f - 1.5f) * scene.Aspect;

        for (int k = 0; k < _ps.Length; k++)
        {
            ref var p = ref _ps[k];

            float hx = p.Hx, hy = p.Hy;
            float bloomLight = 1f;
            if (p.Group != 0 && _phase == Phase.Assemble)
            {
                // распускание: часть рисунка растёт из маленькой копии вокруг точки роста
                float b = Smooth01((_pt / A - p.GStart) / p.GDur);
                float sx = p.Group == 1 ? Lerp(0.3f, 1f, b) : Lerp(0.1f, 1f, b);
                float sy = p.Group == 1 ? Lerp(0.5f, 1f, b) : sx;
                hx = p.Gx + (hx - p.Gx) * sx;
                hy = p.Gy + (hy - p.Gy) * sy;
                if (p.Group == 1) bloomLight = Lerp(0.65f, 1f, b); // бутон темнее раскрытого цветка
            }
            scene.Animate(ref hx, ref hy, _time);
            float homeX = _cx + hx * _unit, homeY = _cy - hy * _unit;

            float shimmer;
            float glint = 0f, hue = 0f;
            if (calm)
            {
                // перелив: две медленные волны света идут по рисунку навстречу друг другу,
                // где они совпадают — светлый блик, в «провалах» лепестки уходят в сиреневый
                float amp = Math.Clamp(_s.Shimmer, 0f, 3f);
                float wave = MathF.Sin(_time * 0.7f + p.Hx * 2.2f - p.Hy * 1.6f);
                float wave2 = MathF.Sin(_time * 0.45f - p.Hx * 1.3f + p.Hy * 2.4f + 1.7f);
                shimmer = 1f + amp * (0.08f * wave + 0.05f * wave2 + 0.03f * MathF.Sin(_time * 0.5f + p.Seed * 6.283f));
                glint = amp * 0.35f * MathF.Pow(MathF.Max(0f, wave * wave2), 2f);
                hue = amp * 0.07f * (0.5f - 0.5f * wave);
                // редкие искорки
                if (amp > 0 && Scene.Hash(k * 977 + (int)(_time * 2f) * 7919) < 0.0015f * amp) glint = MathF.Max(glint, 0.6f);
                if (p.Sparkle) shimmer *= 0.8f + 0.3f * MathF.Max(0, MathF.Sin(_time * 0.9f + p.Seed * 40f));
            }
            else
            {
                shimmer = 0.8f + 0.2f * MathF.Sin(_time * 1.6f + p.Hx * 2.3f + p.Seed * 6.283f);
                float dx0 = p.Hx - sweep;
                shimmer += 0.4f * MathF.Exp(-dx0 * dx0 * 7f);
                if (p.Sparkle) shimmer *= 0.6f + 0.6f * MathF.Max(0, MathF.Sin(_time * 3f + p.Seed * 40f));
            }

            int glyph = p.BaseGlyph;
            float alpha = 1f, light = shimmer;
            float white = 0f; // 0 — свой цвет, 1 — белый
            p.Size = 1f;

            switch (_phase)
            {
                case Phase.Assemble when scene.Assemble == AssembleStyle.Seed:
                {
                    float f = _pt / A;
                    float pourStart = p.Seed * 0.2f * A;   // когда символ начинает падать
                    const float fall = 1.3f;               // сколько секунд падает
                    float sproutStart = (0.3f + 0.33f * p.Delay) * A; // когда вылетает из точки к своему месту
                    float move = 0.07f * A;

                    if (_pt < pourStart) { alpha = 0f; p.Dx = -100; p.Dy = -100; break; }

                    // место в светящемся холмике, куда «сыпятся» символы
                    float fill = Clamp01(f / 0.28f);
                    float hh = Scene.Hash(k * 3 + 1), ang = Scene.Hash(k * 5 + 2) * MathF.PI;
                    float pileR = cw * (3f + 12f * fill) * MathF.Sqrt(hh);
                    float pileX = originX + MathF.Cos(ang) * pileR;
                    float pileY = originY - MathF.Abs(MathF.Sin(ang)) * pileR * 0.45f;

                    if (_pt < pourStart + fall)
                    {
                        float u = (_pt - pourStart) / fall;
                        float x0 = _w * Scene.Hash(k * 11 + 7);
                        float y0 = -ch - Scene.Hash(k * 13) * _h * 0.2f;
                        p.Dx = x0 + (pileX - x0) * u * u; // сначала падает почти отвесно, потом стекается в точку
                        p.Dy = y0 + (pileY - y0) * u;
                        white = 1f;
                        alpha = 0.8f;
                        glyph = _rainIdx[(int)(Scene.Hash(k * 7 + tick) * _rainIdx.Length) % _rainIdx.Length];
                        break;
                    }
                    if (_pt < sproutStart)
                    {
                        p.Dx = pileX;
                        p.Dy = pileY;
                        white = 0.85f;
                        light = 1.2f;
                        alpha = p.Seed < 0.15f ? 0.8f : 0f; // показываем только часть, иначе холмик сплошной
                        glyph = _rainIdx[(int)(Scene.Hash(k) * _rainIdx.Length) % _rainIdx.Length];
                        break;
                    }
                    float e = 1 - MathF.Pow(1 - Clamp01((_pt - sproutStart) / move), 3);
                    p.Dx = pileX + (homeX - pileX) * e;
                    p.Dy = pileY + (homeY - pileY) * e;
                    white = 1f - e;
                    light = shimmer * bloomLight;
                    if (e < 0.7f && Scene.Hash(k * 31 + tick) < 0.35f)
                        glyph = _scrambleIdx[(int)(Scene.Hash(k + tick * 7) * _scrambleIdx.Length) % _scrambleIdx.Length];
                    break;
                }

                case Phase.Assemble when scene.Assemble == AssembleStyle.Rain:
                {
                    // каждый символ падает в своём столбце и приземляется ровно в свой момент (p.Delay),
                    // так что последний символ падает в конце сборки — и дождь прекращается
                    float maxFall = (_h + ch) / _fallSpeed;
                    float land = maxFall + p.Delay * MathF.Max(0.1f, A - 1.2f - maxFall);
                    float start = land - (homeY + ch) / _fallSpeed;
                    p.Dx = homeX;
                    if (_pt < start) { alpha = 0f; p.Dy = -ch; break; }
                    if (_pt < land)
                    {
                        p.Dy = -ch + (_pt - start) * _fallSpeed;
                        white = 1f;
                        alpha = 0.75f;
                        glyph = _rainIdx[(int)(Scene.Hash(k * 7 + tick) * _rainIdx.Length) % _rainIdx.Length];
                        break;
                    }
                    p.Dy = homeY;
                    float g = Clamp01((_pt - land) / 0.8f);    // белый → свой цвет
                    white = 1f - g;
                    light = shimmer * (1f + 0.4f * (1f - g)); // лёгкая вспышка при приземлении
                    break;
                }

                case Phase.Assemble:
                {
                    float a = _pt / A;
                    float local = Clamp01((a - p.Delay * 0.6f) / 0.4f);
                    float e = 1 - MathF.Pow(1 - local, 3);
                    float sx = originX + (p.Seed - 0.5f) * cw * 5f;
                    float sy = originY + (Frac(p.Seed * 13.7f) - 0.5f) * ch * 0.5f;
                    p.Dx = sx + (homeX - sx) * e;
                    p.Dy = sy + (homeY - sy) * e;
                    if (local <= 0)
                        alpha = p.Seed < 0.04f ? 0.8f : 0f; // маленькая светящаяся точка в начале
                    else
                        alpha = 0.35f + 0.65f * e;
                    if (local < 1 && Scene.Hash(k * 31 + tick) < 0.45f)
                        glyph = _scrambleIdx[(int)(Scene.Hash(k + tick * 7) * _scrambleIdx.Length) % _scrambleIdx.Length];
                    light = local < 1 ? 1.35f - 0.35f * e : shimmer;
                    break;
                }

                case Phase.Hold:
                {
                    p.Dx = homeX;
                    p.Dy = homeY;
                    float flickerRate = calm ? 0.0015f : 0.012f;
                    if (_s.CharMode != CharMode.Word && Scene.Hash(k * 131 + tick * 977) < flickerRate)
                        glyph = _rampIdx[(int)(Scene.Hash(k + tick) * _rampIdx.Length) % _rampIdx.Length];
                    break;
                }

                case Phase.Disperse when _style == DisperseStyle.Fade:
                {
                    // мягко гаснет и чуть оседает, как лепестки
                    float local = Clamp01((_pt / D - p.DDelay * 0.6f) / 0.4f);
                    float e = local * local;
                    p.Dx = homeX;
                    p.Dy = homeY + e * ch * 2.5f;
                    alpha = 1f - e;
                    white = 0.35f * local;
                    if (local > 0.3f && Scene.Hash(k * 17 + tick) < 0.2f)
                        glyph = _dustIdx[(int)(Scene.Hash(k + tick) * _dustIdx.Length) % _dustIdx.Length];
                    break;
                }

                case Phase.Disperse:
                {
                    float a = _pt / D;
                    float rel = p.DDelay * 0.65f;
                    if (_style == DisperseStyle.Collapse)
                    {
                        float local = Clamp01((a - rel * 0.5f) / 0.55f);
                        float e = local * local * local;
                        p.Dx = homeX + (originX - homeX) * e;
                        p.Dy = homeY + (originY - homeY) * e;
                        light = shimmer * (1 + 0.6f * e);
                        alpha = 1f - 0.4f * e;
                        if (local >= 1)
                        {
                            float gap = Clamp01((_pt - D) / GapDur);
                            alpha = p.Seed < 0.05f ? 0.9f * (1 - gap) : 0f;
                        }
                        if (local > 0 && Scene.Hash(k * 17 + tick) < 0.3f)
                            glyph = _dustIdx[(int)(Scene.Hash(k + tick) * _dustIdx.Length) % _dustIdx.Length];
                        break;
                    }

                    if (!p.Released)
                    {
                        if (a < rel)
                        {
                            p.Dx = homeX;
                            p.Dy = homeY;
                            break;
                        }
                        p.Released = true;
                        p.Dx = homeX;
                        p.Dy = homeY;
                        if (_style == DisperseStyle.Wind)
                        {
                            p.Vx = 40f + 120f * p.Seed;
                            p.Vy = (Frac(p.Seed * 91.3f) - 0.5f) * 60f;
                        }
                        else
                        {
                            float ddx = homeX - _cx, ddy = homeY - _cy;
                            float len = MathF.Max(1f, MathF.Sqrt(ddx * ddx + ddy * ddy));
                            float sp = 60f + 220f * p.Seed;
                            p.Vx = ddx / len * sp - ddy / len * 90f;
                            p.Vy = ddy / len * sp + ddx / len * 90f;
                        }
                    }

                    float age = MathF.Max(0, (a - rel) * D);
                    if (_style == DisperseStyle.Wind)
                    {
                        p.Vx += (260f + 240f * p.Seed) * dt;
                        p.Vy += (MathF.Sin(p.Dy * 0.015f + _time * 2.2f + p.Seed * 6f) * 140f - 25f) * dt;
                    }
                    else
                    {
                        p.Vx *= 1f - 0.6f * dt;
                        p.Vy = p.Vy * (1f - 0.6f * dt) + 30f * dt;
                    }
                    p.Dx += p.Vx * dt;
                    p.Dy += p.Vy * dt;

                    alpha = Clamp01(1f - age / 1.9f);
                    p.Size = 1f - 0.4f * Clamp01(age / 1.9f);
                    light = 1.2f;
                    if (Scene.Hash(k * 13 + tick) < 0.5f)
                        glyph = _dustIdx[(int)(Scene.Hash(k * 3 + tick) * _dustIdx.Length) % _dustIdx.Length];
                    break;
                }
            }

            // вспышка от упавшей капли
            if (p.Flash > 0)
            {
                white = MathF.Max(white, p.Flash * 0.85f);
                light *= 1f + 0.4f * p.Flash;
                p.Flash = MathF.Max(0, p.Flash - dt * 0.8f);
            }

            if (glint > 0) white = MathF.Max(white, MathF.Min(glint, 0.85f));

            p.Glyph = glyph;
            p.A = alpha;
            p.Cr = Lerp(p.R * light, 0.95f, white);
            p.Cg = Lerp(p.G * light, 0.95f, white);
            p.Cb = Lerp(MathF.Min(1f, p.B * light + (p.R > p.G ? hue : 0f)), 1.0f, white);
        }
    }

    void UpdateDrops(float dt)
    {
        if (_drops.Length == 0) return;
        bool active = _phase == Phase.Hold;
        float ch = _atlas?.CellH ?? 14;
        int tick = (int)(_time * 4f);

        for (int k = 0; k < _drops.Length; k++)
        {
            ref var d = ref _drops[k];
            if (!active)
            {
                // в остальных фазах капли не видны и ждут над экраном
                if (_phase == Phase.Assemble) d.Y = -ch - (float)_rng.NextDouble() * _h;
                continue;
            }

            d.Y += d.Speed * dt;
            if (Scene.Hash(k * 911 + tick) < 0.04f) d.Glyph = _rainIdx[_rng.Next(_rainIdx.Length)];

            ref var target = ref _ps[d.Target];
            if (d.Y >= target.Dy)
            {
                target.Flash = 1f;
                SpawnDrop(ref d, initial: false);
            }
        }
    }

    // ------------------------------------------------------------------
    // Заполнение кадра для DrawAtlas
    // ------------------------------------------------------------------
    public void Fill(Frame f)
    {
        f.Ensure(Count);
        f.Atlas = _atlas?.Image;
        f.Glow = _s.Glow;
        if (_atlas == null) return;
        f.GlowRadius = _atlas.CellH * 0.45f;

        float inv = 1f / _atlas.Scale;
        var rects = _atlas.Rects;
        for (int i = 0; i < _ps.Length; i++)
        {
            ref readonly var p = ref _ps[i];
            var rect = rects[p.Glyph];
            float sc = inv * p.Size;
            f.Sprites[i] = rect;
            f.Xforms[i] = new SKRotationScaleMatrix(sc, 0, p.Dx - rect.Width * sc / 2f, p.Dy - rect.Height * sc / 2f);
            f.Colors[i] = new SKColor(ToByte(p.Cr), ToByte(p.Cg), ToByte(p.Cb), ToByte(p.A));
        }

        bool dropsVisible = _phase == Phase.Hold;
        for (int k = 0; k < _drops.Length; k++)
        {
            ref readonly var d = ref _drops[k];
            int i = _ps.Length + k;
            var rect = rects[d.Glyph];
            f.Sprites[i] = rect;
            f.Xforms[i] = new SKRotationScaleMatrix(inv, 0, d.X - rect.Width * inv / 2f, d.Y - rect.Height * inv / 2f);
            f.Colors[i] = dropsVisible ? new SKColor(242, 242, 255, ToByte(d.Alpha)) : SKColors.Transparent;
        }
    }

    static byte ToByte(float v) => (byte)(v <= 0 ? 0 : v >= 1 ? 255 : v * 255f);
    static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;
    static float Frac(float v) => v - MathF.Floor(v);
    static float Lerp(float a, float b, float t) => a + (b - a) * t;
    static float Smooth01(float x)
    {
        float t = Clamp01(x);
        return t * t * (3 - 2 * t);
    }
}
