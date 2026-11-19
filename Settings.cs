using System.Text.Json;
using System.Text.Json.Serialization;

namespace AsciiWallpaper;

public enum CharMode { RampLong, RampShort, Word, Binary }

public enum PlayMode { Cycle, Shuffle, Repeat, Static }

/// <summary>Все настройки приложения. Хранятся в ~/Library/Application Support/AsciiWallpaper/settings.json</summary>
public sealed class Settings
{
    public string CurrentSceneId { get; set; } = "rose-garden";
    public List<string> DisabledScenes { get; set; } = new();
    public List<string> ImagePaths { get; set; } = new();

    public CharMode CharMode { get; set; } = CharMode.RampLong;
    public string Word { get; set; } = "become";
    public float FontSize { get; set; } = 12f;
    public float Speed { get; set; } = 1f;
    public float HoldSeconds { get; set; } = 25f;
    public float SceneSize { get; set; } = 0.8f;
    public bool Glow { get; set; } = true;
    public int Fps { get; set; } = 30;
    public PlayMode PlayMode { get; set; } = PlayMode.Repeat;
    public bool WallpaperOn { get; set; }

    // ---------- розы ----------
    /// <summary>Ширина голов роз (1 = как нарисовано, меньше — уже).</summary>
    public float RoseWidth { get; set; } = 0.8f;
    /// <summary>Наклон голов в стороны (множитель, 0 — строго вверх).</summary>
    public float RoseTilt { get; set; } = 1f;
    /// <summary>Размер листьев (множитель).</summary>
    public float LeafSize { get; set; } = 1f;
    /// <summary>Сила ветра (0 — штиль).</summary>
    public float Wind { get; set; } = 1f;
    /// <summary>Сила перелива цвета (0 — без перелива).</summary>
    public float Shimmer { get; set; } = 1f;

    /// <summary>Растёт, когда меняется что-то, из-за чего нужно пересобрать частицы (шрифт, символы, размер).</summary>
    [JsonIgnore] public int Version { get; private set; }

    /// <summary>Любое изменение настроек (для обновления UI).</summary>
    public event Action? Changed;

    public void Touch(bool rebuild = false)
    {
        if (rebuild) Version++;
        Changed?.Invoke();
        Save();
    }

    public static string Folder
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (OperatingSystem.IsMacOS())
                root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library", "Application Support");
            var dir = Path.Combine(root, "AsciiWallpaper");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    static string FilePath => Path.Combine(Folder, "settings.json");

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Json) ?? new Settings();
        }
        catch
        {
            // битый файл — начинаем с настроек по умолчанию
        }
        return new Settings();
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json)); }
        catch { /* не критично */ }
    }
}
