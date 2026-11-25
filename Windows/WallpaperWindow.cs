using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using AsciiWallpaper.Controls;
using AsciiWallpaper.Engine;
using AsciiWallpaper.Platform;

namespace AsciiWallpaper.Windows;

/// <summary>Окно без рамки на весь экран, опущенное под иконки рабочего стола.</summary>
public sealed class WallpaperWindow : Window
{
    public WallpaperWindow(Settings settings)
    {
        Title = "ASCII Wallpaper";
        SystemDecorations = SystemDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Background = Brushes.Black;
        WindowStartupLocation = WindowStartupLocation.Manual;

        Content = new AsciiCanvas(settings, new AsciiEngine(settings));
    }

    public void PlaceOn(Screen screen)
    {
        var b = screen.Bounds;
        Position = b.Position;
        Width = b.Width / screen.Scaling;
        Height = b.Height / screen.Scaling;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        MacDesktop.PinToDesktop(this);
        // Avalonia может переставить окно после показа — закрепляем ещё раз
        DispatcherTimer.RunOnce(() => MacDesktop.PinToDesktop(this), TimeSpan.FromMilliseconds(400));
    }
}

/// <summary>Включает/выключает обои на всех мониторах.</summary>
public static class WallpaperManager
{
    static readonly List<WallpaperWindow> Windows = new();

    public static bool IsRunning => Windows.Count > 0;

    public static event Action? StateChanged;

    public static void Start(Settings settings)
    {
        CloseWindows();

        var first = new WallpaperWindow(settings);
        var screens = first.Screens.All.ToList();
        if (screens.Count == 0)
        {
            first.Show();
            Windows.Add(first);
        }
        else
        {
            for (int i = 0; i < screens.Count; i++)
            {
                var w = i == 0 ? first : new WallpaperWindow(settings);
                w.PlaceOn(screens[i]);
                w.Show();
                Windows.Add(w);
            }
        }

        if (settings.DarkMenuBar) MacDesktop.UseBlackDesktopPicture(settings);

        settings.WallpaperOn = true;
        settings.Save();
        StateChanged?.Invoke();
    }

    /// <summary>Убрать обои. rememberOff = false — при выходе из приложения: в следующий раз обои включатся сами.</summary>
    public static void Stop(Settings settings, bool rememberOff = true)
    {
        bool wasRunning = IsRunning;
        CloseWindows();
        if (wasRunning || settings.SavedDesktopPictures.Count > 0) MacDesktop.RestoreDesktopPicture(settings);
        if (rememberOff)
        {
            settings.WallpaperOn = false;
            settings.Save();
        }
        StateChanged?.Invoke();
    }

    /// <summary>Включили/выключили «тёмную строку меню», пока обои работают.</summary>
    public static void ApplyMenuBarSetting(Settings settings)
    {
        if (!IsRunning) return;
        if (settings.DarkMenuBar) MacDesktop.UseBlackDesktopPicture(settings);
        else MacDesktop.RestoreDesktopPicture(settings);
    }

    static void CloseWindows()
    {
        foreach (var w in Windows) w.Close();
        Windows.Clear();
    }
}
