namespace AsciiWallpaper.Engine;

/// <summary>
/// В приложении одни обои — «Розы под дождём». Другие сцены выбрать нельзя.
/// </summary>
public static class SceneLibrary
{
    static readonly Scene Roses = new RoseGardenScene();

    public static void Init(Settings s)
    {
        // старые настройки могли указывать на другие сцены — всегда показываем розы
        s.CurrentSceneId = Roses.Id;
        s.DisabledScenes.Clear();
        s.ImagePaths.Clear();
        s.Save();
    }

    public static IReadOnlyList<Scene> All { get; } = new[] { Roses };

    public static Scene Find(string id) => Roses;

    /// <summary>Сцена одна, переключаться некуда.</summary>
    public static void Advance(string fromId) { }
}
