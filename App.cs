using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using AsciiWallpaper.Controls;
using AsciiWallpaper.Engine;
using AsciiWallpaper.Platform;
using AsciiWallpaper.Windows;

namespace AsciiWallpaper;

public sealed class App : Application
{
    Settings _settings = new();
    GalleryWindow? _gallery;
    IClassicDesktopStyleApplicationLifetime? _desktop;
    NativeMenuItem? _toggleItem, _pauseItem;

    public override void Initialize()
    {
        Name = "ASCII Обои";
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _settings = Settings.Load();
            SceneLibrary.Init(_settings);
            MacDesktop.UseDarkAppearance();
            SetupTray();

            // если в прошлый раз обои были включены — включаем сразу и не мешаем окном галереи
            if (_settings.WallpaperOn)
            {
                WallpaperManager.Start(_settings);
                MacDesktop.SetDockIconVisible(false);
            }
            else
            {
                ShowGallery();
            }

            WallpaperManager.StateChanged += UpdateTrayLabels;
        }
        base.OnFrameworkInitializationCompleted();
    }

    void ShowGallery()
    {
        MacDesktop.SetDockIconVisible(true);
        if (_gallery == null)
        {
            _gallery = new GalleryWindow(_settings);
            _gallery.Closed += (_, _) =>
            {
                _gallery = null;
                if (WallpaperManager.IsRunning) MacDesktop.SetDockIconVisible(false);
                else Quit();
            };
            _gallery.Show();
        }
        else
        {
            _gallery.WindowState = WindowState.Normal;
            _gallery.Activate();
        }
    }

    void Quit()
    {
        WallpaperManager.Stop(_settings, rememberOff: false); // вернёт прежнюю картинку рабочего стола
        _desktop?.Shutdown();
    }

    void SetupTray()
    {
        var menu = new NativeMenu();

        var open = new NativeMenuItem("Открыть галерею…");
        open.Click += (_, _) => ShowGallery();
        menu.Items.Add(open);
        menu.Items.Add(new NativeMenuItemSeparator());

        _toggleItem = new NativeMenuItem("Поставить на рабочий стол");
        _toggleItem.Click += (_, _) =>
        {
            if (WallpaperManager.IsRunning) WallpaperManager.Stop(_settings);
            else WallpaperManager.Start(_settings);
        };
        menu.Items.Add(_toggleItem);

        _pauseItem = new NativeMenuItem("Пауза");
        _pauseItem.Click += (_, _) =>
        {
            AsciiCanvas.Paused = !AsciiCanvas.Paused;
            UpdateTrayLabels();
        };
        menu.Items.Add(_pauseItem);

        menu.Items.Add(new NativeMenuItemSeparator());
        var quit = new NativeMenuItem("Выйти");
        quit.Click += (_, _) => Quit();
        menu.Items.Add(quit);

        var tray = new TrayIcon
        {
            ToolTipText = "ASCII Обои",
            Menu = menu,
            IsVisible = true,
        };
        var iconStream = typeof(App).Assembly.GetManifestResourceStream("tray.png");
        if (iconStream != null) tray.Icon = new WindowIcon(iconStream);

        TrayIcon.SetIcons(this, new TrayIcons { tray });
        UpdateTrayLabels();
    }

    void UpdateTrayLabels()
    {
        if (_toggleItem != null)
            _toggleItem.Header = WallpaperManager.IsRunning ? "Убрать с рабочего стола" : "Поставить на рабочий стол";
        if (_pauseItem != null)
            _pauseItem.Header = AsciiCanvas.Paused ? "Продолжить" : "Пауза";
    }
}
