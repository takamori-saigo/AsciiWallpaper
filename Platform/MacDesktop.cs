using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace AsciiWallpaper.Platform;

/// <summary>
/// Нативные вызовы AppKit через Objective-C runtime:
/// опускаем окно на уровень рабочего стола (под иконки), растягиваем на весь экран,
/// делаем «прозрачным» для мыши и видимым на всех рабочих столах (Spaces).
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
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern void SendNInt(IntPtr recv, IntPtr sel, nint arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern void SendNUInt(IntPtr recv, IntPtr sel, nuint arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern void SendBool(IntPtr recv, IntPtr sel, [MarshalAs(UnmanagedType.I1)] bool arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern CGRect SendRect(IntPtr recv, IntPtr sel);
    [DllImport(ObjC, EntryPoint = "objc_msgSend_stret")] static extern void SendRectStret(out CGRect ret, IntPtr recv, IntPtr sel);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern void SendSetFrame(IntPtr recv, IntPtr sel, CGRect frame, [MarshalAs(UnmanagedType.I1)] bool display);

    [DllImport(CoreGraphics)] static extern int CGWindowLevelForKey(int key);

    const int kCGDesktopWindowLevelKey = 2;
    const nuint CanJoinAllSpaces = 1 << 0, Stationary = 1 << 4, IgnoresCycle = 1 << 6;

    static IntPtr Sel(string s) => sel_registerName(s);

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

    public static void PinToDesktop(Window window)
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            var ns = GetNSWindow(window);
            if (ns == IntPtr.Zero) return;

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

    /// <summary>Показывать ли иконку в Доке (когда открыта только «обоина», иконка не нужна).</summary>
    public static void SetDockIconVisible(bool visible)
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            var app = Send(objc_getClass("NSApplication"), Sel("sharedApplication"));
            SendNInt(app, Sel("setActivationPolicy:"), visible ? 0 : 1); // Regular / Accessory
            if (visible) SendBool(app, Sel("activateIgnoringOtherApps:"), true);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("SetDockIconVisible failed: " + ex.Message);
        }
    }
}
