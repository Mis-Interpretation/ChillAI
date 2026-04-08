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

            // Add WS_EX_LAYERED style
            var exStyle = Win32Interop.GetWindowLong(hwnd, Win32Interop.GWL_EXSTYLE);
            Win32Interop.SetWindowLong(hwnd, Win32Interop.GWL_EXSTYLE,
                (uint)exStyle | Win32Interop.WS_EX_LAYERED);

            // Set alpha (0-255)
            byte alphaByte = (byte)Mathf.Clamp(alpha * 255f, 0f, 255f);
            Win32Interop.SetLayeredWindowAttributes(hwnd, 0, alphaByte, Win32Interop.LWA_ALPHA);

            Debug.Log($"[ChillAI] Window transparency set to {alpha:F2} (byte: {alphaByte})");
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

            var exStyle = (uint)Win32Interop.GetWindowLong(hwnd, Win32Interop.GWL_EXSTYLE);

            if (enabled)
                exStyle |= Win32Interop.WS_EX_TRANSPARENT;
            else
                exStyle &= ~Win32Interop.WS_EX_TRANSPARENT;

            Win32Interop.SetWindowLong(hwnd, Win32Interop.GWL_EXSTYLE, exStyle);
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
#endif
    }
}
