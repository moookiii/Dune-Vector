using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace DuneVector
{
    /// <summary>
    /// Reinforces Unity's gameplay cursor lock against the active Windows player
    /// client area. Borderless fullscreen can otherwise let the native pointer
    /// cross onto another display even while Unity reports a Locked cursor.
    /// </summary>
    [DefaultExecutionOrder(32000)]
    internal sealed class DuneVectorCursorBoundary : MonoBehaviour
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        private static readonly uint CurrentProcessId = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        private static DuneVectorCursorBoundary _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (_instance != null)
            {
                return;
            }

            GameObject host = new GameObject(nameof(DuneVectorCursorBoundary))
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            DontDestroyOnLoad(host);
            _instance = host.AddComponent<DuneVectorCursorBoundary>();
        }

        private void LateUpdate()
        {
            if (Application.isFocused && Cursor.lockState == CursorLockMode.Locked)
            {
                ConfineToPlayerClientArea();
                return;
            }

            ReleaseNativeClip();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                ReleaseNativeClip();
            }
        }

        private void OnDisable()
        {
            ReleaseNativeClip();
        }

        private void OnApplicationQuit()
        {
            ReleaseNativeClip();
        }

        private static void ConfineToPlayerClientArea()
        {
            IntPtr window = GetForegroundWindow();
            if (window == IntPtr.Zero)
            {
                return;
            }

            GetWindowThreadProcessId(window, out uint processId);
            if (processId != CurrentProcessId || !GetClientRect(window, out NativeRect clientRect))
            {
                return;
            }

            NativePoint topLeft = new NativePoint(clientRect.Left, clientRect.Top);
            NativePoint bottomRight = new NativePoint(clientRect.Right, clientRect.Bottom);
            if (!ClientToScreen(window, ref topLeft) || !ClientToScreen(window, ref bottomRight))
            {
                return;
            }

            NativeRect screenRect = new NativeRect(
                topLeft.X,
                topLeft.Y,
                bottomRight.X,
                bottomRight.Y);
            ClipCursor(ref screenRect);
        }

        private static void ReleaseNativeClip()
        {
            ClipCursor(IntPtr.Zero);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;

            public NativePoint(int x, int y)
            {
                X = x;
                Y = y;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public NativeRect(int left, int top, int right, int bottom)
            {
                Left = left;
                Top = top;
                Right = right;
                Bottom = bottom;
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(IntPtr window, out NativeRect rect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ClientToScreen(IntPtr window, ref NativePoint point);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ClipCursor(ref NativeRect rect);

        [DllImport("user32.dll", EntryPoint = "ClipCursor")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ClipCursor(IntPtr rect);
#endif
    }
}
