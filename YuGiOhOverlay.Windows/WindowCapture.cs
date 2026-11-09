using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using YuGiOhOverlay.Application;
using YuGiOhOverlay.Domain;

namespace YuGiOhOverlay.Windows;

public sealed record TargetWindow(nint Handle, uint ProcessId, string Title)
{
    public override string ToString() => Title;
}
public sealed record ClientGeometry(int X, int Y, int Width, int Height, uint Dpi);
public sealed record CaptureProfile(NormalizedRect Hand, double AspectRatio, uint Dpi);

public static class WindowCatalog
{
    public static IReadOnlyList<TargetWindow> List()
    {
        var windows = new List<TargetWindow>();
        Native.EnumWindows((handle, _) =>
        {
            Native.GetWindowThreadProcessId(handle, out var pid);
            var title = new StringBuilder(512);
            Native.GetWindowText(handle, title, title.Capacity);
            if (Native.IsWindowVisible(handle) && pid != Environment.ProcessId && title.Length > 0)
                windows.Add(new(handle, pid, title.ToString()));
            return true;
        }, 0);
        return windows;
    }

    public static ClientGeometry Geometry(TargetWindow target)
    {
        Native.GetWindowThreadProcessId(target.Handle, out var pid);
        if (pid != target.ProcessId || !Native.IsWindow(target.Handle) || Native.IsIconic(target.Handle)
            || !Native.GetClientRect(target.Handle, out var rect) || rect.Right <= 0 || rect.Bottom <= 0)
            throw new InvalidOperationException("fenêtre cible indisponible");
        var point = new Native.Point();
        if (!Native.ClientToScreen(target.Handle, ref point)) throw new InvalidOperationException("position de fenêtre indisponible");
        return new(point.X, point.Y, rect.Right, rect.Bottom, Native.GetDpiForWindow(target.Handle));
    }
}

public sealed class WindowCapture(TargetWindow target, CaptureProfile profile, IClock clock, int maximumPixels) : IScreenCapture
{
    public Task<CapturedFrame> CaptureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Native.GetForegroundWindow() != target.Handle) throw new InvalidOperationException("remettre le jeu au premier plan");
        var geometry = WindowCatalog.Geometry(target);
        if (!profile.Hand.IsValid || profile.AspectRatio <= 0 || !double.IsFinite(profile.AspectRatio)
            || Math.Abs(geometry.Width / (double)geometry.Height / profile.AspectRatio - 1) > 0.01
            || profile.Dpi != geometry.Dpi) throw new InvalidOperationException("recalibrer la main après le changement de ratio ou DPI");
        var roi = profile.Hand;
        int x = (int)(geometry.Width * roi.X), y = (int)(geometry.Height * roi.Y);
        int width = Math.Min(geometry.Width - x, (int)(geometry.Width * roi.Width));
        int height = Math.Min(geometry.Height - y, (int)(geometry.Height * roi.Height));
        if (width < 1 || height < 1 || (long)width * height > maximumPixels)
            throw new InvalidOperationException("zone de main invalide ou trop grande");
        var capturedAt = clock.UtcNow;
        using var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            var source = Native.GetDC(target.Handle);
            if (source == 0) throw new InvalidOperationException("capture de la fenêtre impossible");
            var destination = graphics.GetHdc();
            try
            {
                if (!Native.BitBlt(destination, 0, 0, width, height, source, x, y, 0x00CC0020))
                    throw new InvalidOperationException("capture indisponible ; essayer le mode fenêtré sans bordure");
            }
            finally { graphics.ReleaseHdc(destination); Native.ReleaseDC(target.Handle, source); }
        }
        if (Native.GetForegroundWindow() != target.Handle || WindowCatalog.Geometry(target) != geometry)
            throw new InvalidOperationException("fenêtre déplacée pendant la capture");
        using var output = new MemoryStream();
        bitmap.Save(output, ImageFormat.Png);
        return Task.FromResult(new CapturedFrame(output.ToArray(), capturedAt,
            $"{target.Handle}:{target.ProcessId}:{geometry.Width}:{geometry.Height}:{geometry.Dpi}"));
    }
}

internal static class Native
{
    internal delegate bool EnumCallback(nint handle, nint parameter);
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumCallback callback, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(nint handle, StringBuilder text, int count);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint handle);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint handle);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint handle);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint handle, out uint pid);
    [DllImport("user32.dll")] internal static extern bool GetClientRect(nint handle, out Rect rect);
    [DllImport("user32.dll")] internal static extern bool ClientToScreen(nint handle, ref Point point);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint handle);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern nint GetDC(nint handle);
    [DllImport("user32.dll")] internal static extern int ReleaseDC(nint handle, nint dc);
    [DllImport("gdi32.dll")] internal static extern bool BitBlt(nint destination, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint operation);
}
