using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using AOT;
using UnityEngine;

namespace ChillAI.Service.Platform
{
    public class Win32WindowService : IWindowService
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        IntPtr _hwnd;

        IntPtr Hwnd
        {
            get
            {
                if (_hwnd == IntPtr.Zero)
                    _hwnd = Win32Interop.GetActiveWindow();
                return _hwnd;
            }
        }

        public void MakeTransparent(float alpha)
        {
            var hwnd = Hwnd;
            if (hwnd == IntPtr.Zero)
            {
                Debug.LogWarning("[ChillAI] Could not get Unity window handle.");
                return;
            }

            // Remove window border — DWM per-pixel alpha requires borderless (WS_POPUP).
            Win32Interop.SetWindowLongPtr(hwnd, Win32Interop.GWL_STYLE,
                (IntPtr)(Win32Interop.WS_POPUP | Win32Interop.WS_VISIBLE));
            Win32Interop.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                Win32Interop.SWP_NOMOVE | Win32Interop.SWP_NOSIZE |
                Win32Interop.SWP_FRAMECHANGED | Win32Interop.SWP_SHOWWINDOW);

            // DWM per-pixel alpha: extend glass frame over entire client area.
            // Camera background (alpha=0) becomes transparent to desktop,
            // sprites and UI (alpha>0) stay fully opaque.
            var margins = new Win32Interop.MARGINS
            {
                cxLeftWidth = -1,
                cxRightWidth = -1,
                cyTopHeight = -1,
                cyBottomHeight = -1
            };
            int hr = Win32Interop.DwmExtendFrameIntoClientArea(hwnd, ref margins);

            if (hr == 0)
            {
                Debug.Log("[ChillAI] DWM per-pixel transparency enabled.");
            }
            else
            {
                // Fallback: uniform window alpha (old behavior, needs WS_EX_LAYERED).
                // Uses read-modify-write to preserve existing flags (e.g. WS_EX_TRANSPARENT).
                var fallbackStyle = (long)Win32Interop.GetWindowLongPtr(hwnd, Win32Interop.GWL_EXSTYLE);
                Win32Interop.SetWindowLongPtr(hwnd, Win32Interop.GWL_EXSTYLE,
                    (IntPtr)(fallbackStyle | Win32Interop.WS_EX_LAYERED));
                byte alphaByte = (byte)Mathf.Clamp(alpha * 255f, 0f, 255f);
                Win32Interop.SetLayeredWindowAttributes(hwnd, 0, alphaByte, Win32Interop.LWA_ALPHA);
                Debug.LogWarning($"[ChillAI] DWM failed (0x{hr:X8}), falling back to uniform alpha={alpha:F2}");
            }
        }

        public void SetAlwaysOnTop(bool enabled)
        {
            var hwnd = Hwnd;
            if (hwnd == IntPtr.Zero) return;

            var insertAfter = enabled
                ? Win32Interop.HWND_TOPMOST
                : new IntPtr(-2); // HWND_NOTOPMOST

            Win32Interop.SetWindowPos(hwnd, insertAfter, 0, 0, 0, 0,
                Win32Interop.SWP_NOMOVE | Win32Interop.SWP_NOSIZE | Win32Interop.SWP_SHOWWINDOW);

            Debug.Log($"[ChillAI] Always on top: {enabled}");
        }

        public void SetClickThrough(bool enabled)
        {
            var hwnd = Hwnd;
            if (hwnd == IntPtr.Zero) return;

            var exStyle = (long)Win32Interop.GetWindowLongPtr(hwnd, Win32Interop.GWL_EXSTYLE);

            if (enabled)
            {
                // WS_EX_TRANSPARENT alone can be ineffective on some DX11 fullscreen-windowed setups.
                // Ensure WS_EX_LAYERED is present when enabling click-through.
                exStyle |= Win32Interop.WS_EX_LAYERED;
                exStyle |= Win32Interop.WS_EX_TRANSPARENT;
            }
            else
                exStyle &= ~(long)Win32Interop.WS_EX_TRANSPARENT;

            Win32Interop.SetWindowLongPtr(hwnd, Win32Interop.GWL_EXSTYLE, (IntPtr)exStyle);
            if (enabled)
                Win32Interop.SetLayeredWindowAttributes(hwnd, 0, 255, Win32Interop.LWA_ALPHA);

            // Force Windows to apply the new extended style immediately.
            Win32Interop.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                Win32Interop.SWP_NOMOVE | Win32Interop.SWP_NOSIZE |
                Win32Interop.SWP_NOZORDER | Win32Interop.SWP_NOACTIVATE |
                Win32Interop.SWP_FRAMECHANGED);

            Debug.Log($"[ChillAI] Click-through: {enabled} (exStyle=0x{exStyle:X8})");
        }

        public (int x, int y) GetWindowPosition()
        {
            var hwnd = Hwnd;
            if (hwnd == IntPtr.Zero) return (0, 0);
            Win32Interop.GetWindowRect(hwnd, out var rect);
            return (rect.Left, rect.Top);
        }

        public void SetWindowPosition(int x, int y)
        {
            var hwnd = Hwnd;
            if (hwnd == IntPtr.Zero) return;
            Win32Interop.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0,
                Win32Interop.SWP_NOSIZE | Win32Interop.SWP_NOZORDER | Win32Interop.SWP_NOACTIVATE);
        }

        public (int x, int y) GetCursorScreenPosition()
        {
            Win32Interop.GetCursorPos(out var point);
            return (point.X, point.Y);
        }

        sealed class MonitorCollectContext
        {
            public List<Win32Interop.RECT> Monitors;
        }

        sealed class FindMonitorContext
        {
            public IntPtr Target;
            public int Index;
        }

        /// <summary>IL2CPP requires a static method with MonoPInvokeCallback; lambdas enumerate 0 monitors in player builds.</summary>
        [MonoPInvokeCallback(typeof(Win32Interop.MonitorEnumProc))]
        static bool CollectMonitorsProc(IntPtr hMonitor, IntPtr hdcMonitor, ref Win32Interop.RECT lprcMonitor, IntPtr dwData)
        {
            var ctx = (MonitorCollectContext)GCHandle.FromIntPtr(dwData).Target;
            var info = new Win32Interop.MONITORINFOEX();
            info.cbSize = Marshal.SizeOf<Win32Interop.MONITORINFOEX>();
            if (Win32Interop.GetMonitorInfo(hMonitor, ref info))
                ctx.Monitors.Add(info.rcWork);
            return true;
        }

        [MonoPInvokeCallback(typeof(Win32Interop.MonitorEnumProc))]
        static bool FindMonitorIndexProc(IntPtr hMonitor, IntPtr hdcMonitor, ref Win32Interop.RECT lprcMonitor, IntPtr dwData)
        {
            var ctx = (FindMonitorContext)GCHandle.FromIntPtr(dwData).Target;
            if (hMonitor == ctx.Target)
                return false;
            ctx.Index++;
            return true;
        }

        List<Win32Interop.RECT> EnumerateMonitors()
        {
            var monitors = new List<Win32Interop.RECT>();
            var ctx = new MonitorCollectContext { Monitors = monitors };
            var handle = GCHandle.Alloc(ctx);
            try
            {
                Win32Interop.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, CollectMonitorsProc, GCHandle.ToIntPtr(handle));
            }
            finally
            {
                handle.Free();
            }
            return monitors;
        }

        int GetCurrentMonitorIndex(List<Win32Interop.RECT> monitors)
        {
            var hwnd = Hwnd;
            if (hwnd == IntPtr.Zero) return 0;
            var currentMonitor = Win32Interop.MonitorFromWindow(hwnd, Win32Interop.MONITOR_DEFAULTTONEAREST);
            var ctx = new FindMonitorContext { Target = currentMonitor, Index = 0 };
            var handle = GCHandle.Alloc(ctx);
            try
            {
                Win32Interop.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, FindMonitorIndexProc, GCHandle.ToIntPtr(handle));
            }
            finally
            {
                handle.Free();
            }
            return ctx.Index < monitors.Count ? ctx.Index : 0;
        }

        public int GetDisplayCount()
        {
            return EnumerateMonitors().Count;
        }

        public void MoveToDisplay(int displayIndex)
        {
            var hwnd = Hwnd;
            if (hwnd == IntPtr.Zero) return;

            var monitors = EnumerateMonitors();
            if (monitors.Count == 0) return;

            int idx = displayIndex % monitors.Count;
            var target = monitors[idx];

            // Get current window size
            Win32Interop.GetWindowRect(hwnd, out var currentRect);
            int width = currentRect.Right - currentRect.Left;
            int height = currentRect.Bottom - currentRect.Top;

            // Get relative position on current monitor
            var currentMonitors = monitors;
            int curIdx = GetCurrentMonitorIndex(currentMonitors);
            var curMon = currentMonitors[curIdx];

            float relX = (currentRect.Left - curMon.Left) / (float)(curMon.Right - curMon.Left);
            float relY = (currentRect.Top - curMon.Top) / (float)(curMon.Bottom - curMon.Top);

            // Apply relative position to target monitor
            int newX = target.Left + (int)(relX * (target.Right - target.Left));
            int newY = target.Top + (int)(relY * (target.Bottom - target.Top));

            Win32Interop.SetWindowPos(hwnd, IntPtr.Zero, newX, newY, width, height,
                Win32Interop.SWP_NOZORDER | Win32Interop.SWP_NOACTIVATE);

            Debug.Log($"[ChillAI] Moved window to display {idx} at ({newX}, {newY})");
        }
#else
        public void MakeTransparent(float alpha)
        {
            Debug.LogWarning("[ChillAI] Window transparency is only supported on Windows.");
        }

        public void SetAlwaysOnTop(bool enabled)
        {
            Debug.LogWarning("[ChillAI] Always on top is only supported on Windows.");
        }

        public void SetClickThrough(bool enabled)
        {
            Debug.LogWarning("[ChillAI] Click through is only supported on Windows.");
        }

        public (int x, int y) GetWindowPosition()
        {
            Debug.LogWarning("[ChillAI] GetWindowPosition is only supported on Windows.");
            return (0, 0);
        }

        public void SetWindowPosition(int x, int y)
        {
            Debug.LogWarning("[ChillAI] SetWindowPosition is only supported on Windows.");
        }

        public (int x, int y) GetCursorScreenPosition()
        {
            Debug.LogWarning("[ChillAI] GetCursorScreenPosition is only supported on Windows.");
            return (0, 0);
        }

        public int GetDisplayCount()
        {
            Debug.LogWarning("[ChillAI] GetDisplayCount is only supported on Windows.");
            return 1;
        }

        public void MoveToDisplay(int displayIndex)
        {
            Debug.LogWarning("[ChillAI] MoveToDisplay is only supported on Windows.");
        }
#endif
    }
}
