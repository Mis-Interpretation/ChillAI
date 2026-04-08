using System;
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
#endif
    }
}
