using System.Runtime.InteropServices;
using Avalonia.Controls;
using SkiaSharp;

namespace AsciiWallpaper.Platform;

/// <summary>
/// Нативные вызовы AppKit через Objective-C runtime:
/// опускаем окно на уровень рабочего стола (под иконки), растягиваем на весь экран,
/// делаем «прозрачным» для мыши и видимым на всех рабочих столах (Spaces).
/// Плюс тёмное оформление окон и чёрная картинка рабочего стола, чтобы строка меню не была белой.
/// </summary>
public static class MacDesktop
{
    const string ObjC = "/usr/lib/libobjc.A.dylib";
    const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    [StructLayout(LayoutKind.Sequential)]
    struct CGRect { public double X, Y, W, H; }

    [DllImport(ObjC)] static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC)] static extern IntPtr objc_getClass(string name);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr recv, IntPtr sel);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr SendPtr(IntPtr recv, IntPtr sel, IntPtr arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr SendStr(IntPtr recv, IntPtr sel, [MarshalAs(UnmanagedType.LPUTF8Str)] string arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr SendIndex(IntPtr recv, IntPtr sel, nuint index);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern nuint SendCount(IntPtr recv, IntPtr sel);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr SendColor(IntPtr recv, IntPtr sel, double r, double g, double b, double a);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern void SendNInt(IntPtr recv, IntPtr sel, nint arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern void SendNUInt(IntPtr recv, IntPtr sel, nuint arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern void SendBool(IntPtr recv, IntPtr sel, [MarshalAs(UnmanagedType.I1)] bool arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern CGRect SendRect(IntPtr recv, IntPtr sel);
    [DllImport(ObjC, EntryPoint = "objc_msgSend_stret")] static extern void SendRectStret(out CGRect ret, IntPtr recv, IntPtr sel);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern void SendSetFrame(IntPtr recv, IntPtr sel, CGRect frame, [MarshalAs(UnmanagedType.I1)] bool display);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    static extern bool SendSetDesktopImage(IntPtr recv, IntPtr sel, IntPtr url, IntPtr screen, IntPtr options, IntPtr error);

    [DllImport(CoreGraphics)] static extern int CGWindowLevelForKey(int key);

    const int kCGDesktopWindowLevelKey = 2;
    const nuint CanJoinAllSpaces = 1 << 0, Stationary = 1 << 4, IgnoresCycle = 1 << 6;
    const nuint StyleBorderless = 0;

    static IntPtr Sel(string s) => sel_registerName(s);
    static IntPtr Cls(string s) => objc_getClass(s);
    static IntPtr NSString(string s) => SendStr(Cls("NSString"), Sel("stringWithUTF8String:"), s);

    static string? FromNSString(IntPtr ns) =>
        ns == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(Send(ns, Sel("UTF8String")));

    static IntPtr Color(double r, double g, double b) =>
        SendColor(Cls("NSColor"), Sel("colorWithSRGBRed:green:blue:alpha:"), r, g, b, 1.0);

    static IntPtr GetNSWindow(Window w)
    {
        var handle = w.TryGetPlatformHandle();
        if (handle == null || handle.Handle == IntPtr.Zero) return IntPtr.Zero;
        // Avalonia на macOS отдаёт NSView, окно берём через [view window]
        return handle.HandleDescriptor == "NSWindow" ? handle.Handle : Send(handle.Handle, Sel("window"));
    }

    static CGRect Frame(IntPtr obj)
    {
        if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            SendRectStret(out var r, obj, Sel("frame"));
            return r;
        }
        return SendRect(obj, Sel("frame"));
    }

    // ------------------------------------------------------------------
    // Окно-обои
    // ------------------------------------------------------------------
    public static void PinToDesktop(Window window)
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            var ns = GetNSWindow(window);
            if (ns == IntPtr.Zero) return;

            // без рамки и заголовка, фон чёрный — никаких белых полос, даже если что-то не дорисовалось
            SendNUInt(ns, Sel("setStyleMask:"), StyleBorderless);
            SendPtr(ns, Sel("setBackgroundColor:"), Color(0, 0, 0));
            SendBool(ns, Sel("setOpaque:"), true);

            SendNInt(ns, Sel("setLevel:"), CGWindowLevelForKey(kCGDesktopWindowLevelKey));
            SendNUInt(ns, Sel("setCollectionBehavior:"), CanJoinAllSpaces | Stationary | IgnoresCycle);
            SendBool(ns, Sel("setIgnoresMouseEvents:"), true);
            SendBool(ns, Sel("setHasShadow:"), false);

            var screen = Send(ns, Sel("screen"));
            if (screen != IntPtr.Zero)
                SendSetFrame(ns, Sel("setFrame:display:"), Frame(screen), true);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("PinToDesktop failed: " + ex.Message);
        }
    }

    // ------------------------------------------------------------------
    // Тёмное оформление
    // ------------------------------------------------------------------

    /// <summary>Тёмные меню и диалоги у всего приложения, даже если в системе светлая тема.</summary>
    public static void UseDarkAppearance()
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            var dark = SendPtr(Cls("NSAppearance"), Sel("appearanceNamed:"), NSString("NSAppearanceNameDarkAqua"));
            var app = Send(Cls("NSApplication"), Sel("sharedApplication"));
            if (dark != IntPtr.Zero) SendPtr(app, Sel("setAppearance:"), dark);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("UseDarkAppearance failed: " + ex.Message);
        }
    }

    /// <summary>Тёмный заголовок окна того же цвета, что и фон (вместо белой полосы на светлой теме).</summary>
    public static void DarkTitleBar(Window window, byte r, byte g, byte b)
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            var ns = GetNSWindow(window);
            if (ns == IntPtr.Zero) return;
            var dark = SendPtr(Cls("NSAppearance"), Sel("appearanceNamed:"), NSString("NSAppearanceNameDarkAqua"));
            if (dark != IntPtr.Zero) SendPtr(ns, Sel("setAppearance:"), dark);
            SendBool(ns, Sel("setTitlebarAppearsTransparent:"), true);
            SendPtr(ns, Sel("setBackgroundColor:"), Color(r / 255.0, g / 255.0, b / 255.0));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("DarkTitleBar failed: " + ex.Message);
        }
    }

    // ------------------------------------------------------------------
    // Картинка рабочего стола.
    // Цвет строки меню macOS берёт из настоящей картинки рабочего стола (не из наших окон).
    // Если она светлая — строка меню белая поверх чёрных обоев. Поэтому, пока обои включены,
    // ставим чёрную картинку, а при выключении возвращаем прежнюю.
    // ------------------------------------------------------------------
    static string BlackPicturePath
    {
        get
        {
            var path = Path.Combine(Settings.Folder, "black-desktop.png");
            if (!File.Exists(path))
            {
                using var bmp = new SKBitmap(16, 16);
                bmp.Erase(SKColors.Black);
                using var img = SKImage.FromBitmap(bmp);
                using var data = img.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(path, data.ToArray());
            }
            return path;
        }
    }

    static IEnumerable<IntPtr> Screens()
    {
        var screens = Send(Cls("NSScreen"), Sel("screens"));
        if (screens == IntPtr.Zero) yield break;
        nuint n = SendCount(screens, Sel("count"));
        for (nuint i = 0; i < n; i++) yield return SendIndex(screens, Sel("objectAtIndex:"), i);
    }

    static bool SetDesktopPicture(IntPtr workspace, IntPtr screen, string path)
    {
        var url = SendPtr(Cls("NSURL"), Sel("fileURLWithPath:"), NSString(path));
        var options = Send(Cls("NSDictionary"), Sel("dictionary"));
        return SendSetDesktopImage(workspace, Sel("setDesktopImageURL:forScreen:options:error:"), url, screen, options, IntPtr.Zero);
    }

    public static void UseBlackDesktopPicture(Settings s)
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            var black = BlackPicturePath;
            var ws = Send(Cls("NSWorkspace"), Sel("sharedWorkspace"));
            int i = 0;
            foreach (var screen in Screens())
            {
                var current = FromNSString(Send(SendPtr(ws, Sel("desktopImageURLForScreen:"), screen), Sel("path")));
                // запоминаем прежнюю картинку (если там уже наша чёрная — значит, запомнили раньше)
                if (current != null && current != black)
                {
                    while (s.SavedDesktopPictures.Count <= i) s.SavedDesktopPictures.Add("");
                    s.SavedDesktopPictures[i] = current;
                }
                SetDesktopPicture(ws, screen, black);
                i++;
            }
            s.Save();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("UseBlackDesktopPicture failed: " + ex.Message);
        }
    }

    public static void RestoreDesktopPicture(Settings s)
    {
        if (!OperatingSystem.IsMacOS() || s.SavedDesktopPictures.Count == 0) return;
        try
        {
            var black = BlackPicturePath;
            var ws = Send(Cls("NSWorkspace"), Sel("sharedWorkspace"));
            int i = 0;
            foreach (var screen in Screens())
            {
                var current = FromNSString(Send(SendPtr(ws, Sel("desktopImageURLForScreen:"), screen), Sel("path")));
                var saved = i < s.SavedDesktopPictures.Count ? s.SavedDesktopPictures[i] : s.SavedDesktopPictures[0];
                // возвращаем, только если сейчас стоит наша чёрная (вдруг пользователь сам что-то поставил)
                if (current == black && !string.IsNullOrEmpty(saved) && File.Exists(saved))
                    SetDesktopPicture(ws, screen, saved);
                i++;
            }
            s.SavedDesktopPictures.Clear();
            s.Save();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("RestoreDesktopPicture failed: " + ex.Message);
        }
    }

    /// <summary>Показывать ли иконку в Доке (когда открыта только «обоина», иконка не нужна).</summary>
    public static void SetDockIconVisible(bool visible)
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            var app = Send(Cls("NSApplication"), Sel("sharedApplication"));
            SendNInt(app, Sel("setActivationPolicy:"), visible ? 0 : 1); // Regular / Accessory
            if (visible) SendBool(app, Sel("activateIgnoringOtherApps:"), true);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("SetDockIconVisible failed: " + ex.Message);
        }
    }
}
