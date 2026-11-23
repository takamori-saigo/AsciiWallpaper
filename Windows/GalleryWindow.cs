using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using AsciiWallpaper.Controls;
using AsciiWallpaper.Engine;

namespace AsciiWallpaper.Windows;

/// <summary>Главное окно: большой предпросмотр «Роз под дождём» и настройки.</summary>
public sealed class GalleryWindow : Window
{
    static readonly IBrush PanelBg = new SolidColorBrush(Color.Parse("#14161c"));
    static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#8b90a0"));
    static readonly IBrush Idle = new SolidColorBrush(Color.Parse("#262a35"));
    static readonly IBrush On = new SolidColorBrush(Color.Parse("#3ddc84"));

    readonly Settings _s;
    readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
    readonly Button _toggle = new() { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(12, 10), FontWeight = FontWeight.SemiBold };
    readonly Button _pause = new() { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(12, 8) };
    TextBox? _word;

    public GalleryWindow(Settings settings)
    {
        _s = settings;
        Title = "ASCII Обои";
        Width = 1280;
        Height = 820;
        MinWidth = 980;
        MinHeight = 640;
        Background = new SolidColorBrush(Color.Parse("#0e1015"));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var icon = typeof(GalleryWindow).Assembly.GetManifestResourceStream("icon.png");
        if (icon != null) Icon = new WindowIcon(icon);

        Content = BuildLayout();
        Refresh();

        _s.Changed += Refresh;
        WallpaperManager.StateChanged += Refresh;
    }

    protected override void OnClosed(EventArgs e)
    {
        _s.Changed -= Refresh;
        WallpaperManager.StateChanged -= Refresh;
        base.OnClosed(e);
    }

    // ------------------------------------------------------------------
    Control BuildLayout()
    {
        var root = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,330"),
            RowDefinitions = new RowDefinitions("Auto,*"),
            Margin = new Thickness(20),
        };

        // заголовок
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        title.Children.Add(new TextBlock { Text = "ASCII Обои", FontSize = 24, FontWeight = FontWeight.Bold });
        title.Children.Add(new TextBlock
        {
            Text = "· " + SceneLibrary.All[0].Name,
            FontSize = 13,
            Foreground = Muted,
            VerticalAlignment = VerticalAlignment.Center,
        });
        DockPanel.SetDock(_status, Dock.Right);
        header.Children.Add(_status);
        header.Children.Add(title);
        Grid.SetColumnSpan(header, 2);
        root.Children.Add(header);

        // большой предпросмотр
        var preview = new Border
        {
            Background = Brushes.Black,
            CornerRadius = new CornerRadius(14),
            ClipToBounds = true,
            BorderBrush = Idle,
            BorderThickness = new Thickness(1),
            Child = new AsciiCanvas(_s, new AsciiEngine(_s) { FontScale = 0.62f }),
        };
        Grid.SetRow(preview, 1);
        root.Children.Add(preview);

        // настройки
        var side = new Border
        {
            Background = PanelBg,
            CornerRadius = new CornerRadius(14),
            Margin = new Thickness(16, 0, 0, 0),
            Padding = new Thickness(18),
            Child = new ScrollViewer { Content = BuildSettings() },
        };
        Grid.SetColumn(side, 1);
        Grid.SetRow(side, 1);
        root.Children.Add(side);

        return root;
    }

    Control BuildSettings()
    {
        var p = new StackPanel { Spacing = 14 };

        _toggle.Classes.Add("accent");
        _toggle.Click += (_, _) =>
        {
            if (WallpaperManager.IsRunning) WallpaperManager.Stop(_s);
            else WallpaperManager.Start(_s);
        };
        p.Children.Add(_toggle);

        _pause.Click += (_, _) =>
        {
            AsciiCanvas.Paused = !AsciiCanvas.Paused;
            Refresh();
        };
        p.Children.Add(_pause);

        p.Children.Add(Hint("Обои лежат под иконками рабочего стола, не мешают кликам и видны на всех столах (Spaces). Работают, пока приложение открыто — оно живёт в строке меню."));

        p.Children.Add(Section("Символы"));
        p.Children.Add(Combo(new[] { "Плотность (70 символов)", "Плотность (короткая)", "Слово", "Только 0 и 1" },
            (int)_s.CharMode, i => { _s.CharMode = (CharMode)i; _s.Touch(rebuild: true); }));

        _word = new TextBox { Text = _s.Word, Watermark = "например: become" };
        _word.TextChanged += (_, _) =>
        {
            _s.Word = _word.Text ?? "";
            if (_s.CharMode == CharMode.Word) _s.Touch(rebuild: true);
        };
        p.Children.Add(Labeled("Слово для режима «Слово»", _word));

        p.Children.Add(SliderRow("Размер символов", 6, 22, _s.FontSize, 1, v => $"{v:0} pt",
            v => { _s.FontSize = (float)v; _s.Touch(rebuild: true); }));
        p.Children.Add(SliderRow("Размер рисунка", 30, 100, _s.SceneSize * 100, 5, v => $"{v:0}%",
            v => { _s.SceneSize = (float)v / 100f; _s.Touch(rebuild: true); }));

        p.Children.Add(Section("Розы"));
        p.Children.Add(SliderRow("Ширина голов", 60, 110, _s.RoseWidth * 100, 5, v => $"{v:0}%",
            v => { _s.RoseWidth = (float)v / 100f; _s.Touch(rebuild: true); }));
        p.Children.Add(SliderRow("Наклон в стороны", 0, 200, _s.RoseTilt * 100, 10, v => v < 1 ? "строго вверх" : $"{v:0}%",
            v => { _s.RoseTilt = (float)v / 100f; _s.Touch(rebuild: true); }));
        p.Children.Add(SliderRow("Размер листьев", 40, 160, _s.LeafSize * 100, 10, v => $"{v:0}%",
            v => { _s.LeafSize = (float)v / 100f; _s.Touch(rebuild: true); }));
        p.Children.Add(SliderRow("Ветер", 0, 200, _s.Wind * 100, 10, v => v < 1 ? "штиль" : $"{v:0}%",
            v => { _s.Wind = (float)v / 100f; _s.Touch(); }));
        p.Children.Add(SliderRow("Переливание", 0, 200, _s.Shimmer * 100, 10, v => v < 1 ? "выкл." : $"{v:0}%",
            v => { _s.Shimmer = (float)v / 100f; _s.Touch(); }));
        p.Children.Add(Hint("Ширина, наклон и листья меняются сразу, без нового дождя — если розы уже выросли."));

        p.Children.Add(Section("Анимация"));
        p.Children.Add(SliderRow("Скорость", 0.3, 3, _s.Speed, 0.1, v => $"×{v:0.0}",
            v => { _s.Speed = (float)v; _s.Touch(); }));
        p.Children.Add(SliderRow("Сколько розы стоят", 5, 180, _s.HoldSeconds, 5, v => $"{v:0} сек",
            v => { _s.HoldSeconds = (float)v; _s.Touch(); }));

        var repeat = new CheckBox
        {
            Content = "Повторять: розы гаснут и вырастают заново",
            IsChecked = _s.PlayMode != PlayMode.Static,
        };
        repeat.IsCheckedChanged += (_, _) =>
        {
            _s.PlayMode = repeat.IsChecked == true ? PlayMode.Repeat : PlayMode.Static;
            _s.Touch();
        };
        p.Children.Add(repeat);

        var glow = new CheckBox { Content = "Свечение", IsChecked = _s.Glow };
        glow.IsCheckedChanged += (_, _) => { _s.Glow = glow.IsChecked == true; _s.Touch(); };
        p.Children.Add(glow);

        p.Children.Add(Labeled("Частота кадров (меньше — экономнее батарея)", Combo(new[] { "30 FPS", "60 FPS" },
            _s.Fps >= 60 ? 1 : 0, i => { _s.Fps = i == 1 ? 60 : 30; _s.Touch(); })));

        return p;
    }

    // ------------------------------------------------------------------
    void Refresh()
    {
        bool on = WallpaperManager.IsRunning;
        _status.Text = on ? "● Стоит на рабочем столе" : "○ Обои выключены";
        _status.Foreground = on ? On : Muted;
        _toggle.Content = on ? "Убрать с рабочего стола" : "Поставить на рабочий стол";
        _pause.Content = AsciiCanvas.Paused ? "▶ Продолжить анимацию" : "❚❚ Пауза";
        if (_word != null) _word.IsEnabled = _s.CharMode == CharMode.Word;
    }

    // ---------- маленькие фабрики контролов ----------
    static TextBlock Section(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        FontSize = 11,
        FontWeight = FontWeight.SemiBold,
        Foreground = Muted,
        Margin = new Thickness(0, 8, 0, -4),
    };

    static TextBlock Hint(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Foreground = Muted,
        TextWrapping = TextWrapping.Wrap,
    };

    static Control Labeled(string label, Control control) => new StackPanel
    {
        Spacing = 6,
        Children = { new TextBlock { Text = label, FontSize = 13 }, control },
    };

    static ComboBox Combo(string[] items, int selected, Action<int> onChange)
    {
        var c = new ComboBox { ItemsSource = items, SelectedIndex = selected, HorizontalAlignment = HorizontalAlignment.Stretch };
        c.SelectionChanged += (_, _) => { if (c.SelectedIndex >= 0) onChange(c.SelectedIndex); };
        return c;
    }

    static Control SliderRow(string title, double min, double max, double value, double step,
        Func<double, string> format, Action<double> onChange)
    {
        var label = new TextBlock { FontSize = 13 };
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            TickFrequency = step,
            IsSnapToTickEnabled = true,
        };
        label.Text = $"{title}: {format(slider.Value)}";
        slider.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase.ValueProperty) return;
            label.Text = $"{title}: {format(slider.Value)}";
            onChange(slider.Value);
        };
        return new StackPanel { Spacing = 0, Children = { label, slider } };
    }
}
