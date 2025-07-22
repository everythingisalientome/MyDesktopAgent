using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Drawing;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WindowsDesktopAgent
{
    public class DesktopAgent
    {
        // Windows API Constants
        private const int WH_KEYBOARD_LL = 13;
        private const int WH_MOUSE_LL = 14;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_MOUSEMOVE = 0x0200;

        // Windows API Delegates
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        // Hook handles
        private IntPtr keyboardHookID = IntPtr.Zero;
        private IntPtr mouseHookID = IntPtr.Zero;
        private LowLevelKeyboardProc keyboardProc;
        private LowLevelMouseProc mouseProc;

        public event EventHandler<KeyboardEventArgs> KeyboardEvent;
        public event EventHandler<MouseEventArgs> MouseEvent;
        public event EventHandler<UIElementEventArgs> UIElementEvent;

        public DesktopAgent()
        {
            keyboardProc = KeyboardHookCallback;
            mouseProc = MouseHookCallback;
        }

        public void StartMonitoring()
        {
            keyboardHookID = SetHook(keyboardProc, WH_KEYBOARD_LL);
            mouseHookID = SetHook(mouseProc, WH_MOUSE_LL);
        }

        public void StopMonitoring()
        {
            if (keyboardHookID != IntPtr.Zero)
                UnhookWindowsHookEx(keyboardHookID);
            if (mouseHookID != IntPtr.Zero)
                UnhookWindowsHookEx(mouseHookID);
        }

        private IntPtr SetHook(Delegate proc, int hookType)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(hookType,
                    Marshal.GetFunctionPointerForDelegate(proc),
                    GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var vkCode = Marshal.ReadInt32(lParam);
                var keyEvent = new KeyboardEventArgs
                {
                    VirtualKeyCode = vkCode,
                    Key = ((Keys)vkCode).ToString(),
                    IsKeyDown = wParam == (IntPtr)WM_KEYDOWN,
                    Timestamp = DateTime.Now,
                    ActiveWindow = GetActiveWindowInfo()
                };

                KeyboardEvent?.Invoke(this, keyEvent);

                // Capture UI element context when typing
                if (keyEvent.IsKeyDown)
                {
                    var uiElement = GetUIElementAtCursor();
                    if (uiElement != null)
                    {
                        UIElementEvent?.Invoke(this, uiElement);
                    }
                }
            }

            return CallNextHookEx(keyboardHookID, nCode, wParam, lParam);
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var mouseStruct = Marshal.PtrToStructure<POINT>(lParam);
                var mouseEvent = new MouseEventArgs
                {
                    X = mouseStruct.x,
                    Y = mouseStruct.y,
                    Button = GetMouseButton(wParam),
                    Action = GetMouseAction(wParam),
                    Timestamp = DateTime.Now,
                    ActiveWindow = GetActiveWindowInfo()
                };

                MouseEvent?.Invoke(this, mouseEvent);

                // Capture UI element on click
                if (wParam == (IntPtr)WM_LBUTTONDOWN || wParam == (IntPtr)WM_RBUTTONDOWN)
                {
                    var uiElement = GetUIElementAtPoint(mouseStruct.x, mouseStruct.y);
                    if (uiElement != null)
                    {
                        UIElementEvent?.Invoke(this, uiElement);
                    }
                }
            }

            return CallNextHookEx(mouseHookID, nCode, wParam, lParam);
        }

        private WindowInfo GetActiveWindowInfo()
        {
            var hwnd = GetForegroundWindow();
            var sb = new StringBuilder(256);
            GetWindowText(hwnd, sb, sb.Capacity);
            
            var className = new StringBuilder(256);
            GetClassName(hwnd, className, className.Capacity);

            GetWindowThreadProcessId(hwnd, out uint processId);
            var process = Process.GetProcessById((int)processId);

            return new WindowInfo
            {
                Handle = hwnd,
                Title = sb.ToString(),
                ClassName = className.ToString(),
                ProcessName = process.ProcessName,
                ProcessId = processId
            };
        }

        private UIElementEventArgs GetUIElementAtCursor()
        {
            GetCursorPos(out POINT point);
            return GetUIElementAtPoint(point.x, point.y);
        }

        private UIElementEventArgs GetUIElementAtPoint(int x, int y)
        {
            var hwnd = WindowFromPoint(new POINT { x = x, y = y });
            if (hwnd == IntPtr.Zero) return null;

            var sb = new StringBuilder(256);
            GetWindowText(hwnd, sb, sb.Capacity);

            var className = new StringBuilder(256);
            GetClassName(hwnd, className, className.Capacity);

            // Get control ID for native controls
            var controlId = GetDlgCtrlID(hwnd);

            // Try to get accessibility information
            var accessibilityInfo = GetAccessibilityInfo(hwnd);

            return new UIElementEventArgs
            {
                Handle = hwnd,
                Text = sb.ToString(),
                ClassName = className.ToString(),
                ControlId = controlId,
                ElementType = DetermineElementType(className.ToString()),
                Position = new Point(x, y),
                AccessibilityInfo = accessibilityInfo,
                Timestamp = DateTime.Now
            };
        }

        private AccessibilityInfo GetAccessibilityInfo(IntPtr hwnd)
        {
            // This is a simplified version - full implementation would use UI Automation
            try
            {
                var info = new AccessibilityInfo();
                
                // Get window text as fallback
                var sb = new StringBuilder(256);
                GetWindowText(hwnd, sb, sb.Capacity);
                info.Name = sb.ToString();

                // For web browsers, you'd need to inject JavaScript or use browser automation
                // For now, we'll detect if it's a browser
                var windowInfo = GetActiveWindowInfo();
                info.IsBrowser = IsBrowserProcess(windowInfo.ProcessName);
                
                return info;
            }
            catch
            {
                return null;
            }
        }

        private bool IsBrowserProcess(string processName)
        {
            var browsers = new[] { "chrome", "firefox", "edge", "msedge", "opera", "brave", "safari" };
            return Array.Exists(browsers, browser => 
                processName.ToLower().Contains(browser));
        }

        private string DetermineElementType(string className)
        {
            var typeMap = new Dictionary<string, string>
            {
                {"Edit", "TextBox"},
                {"ComboBox", "ComboBox"},
                {"ListBox", "ListBox"},
                {"Button", "Button"},
                {"Static", "Label"},
                {"SysTabControl32", "TabControl"},
                {"SysTreeView32", "TreeView"},
                {"SysListView32", "ListView"},
                {"Chrome_RenderWidgetHostHWND", "BrowserContent"},
                {"MozillaWindowClass", "BrowserContent"}
            };

            return typeMap.ContainsKey(className) ? typeMap[className] : className;
        }

        private string GetMouseButton(IntPtr wParam)
        {
            switch ((int)wParam)
            {
                case WM_LBUTTONDOWN: return "Left";
                case WM_RBUTTONDOWN: return "Right";
                default: return "None";
            }
        }

        private string GetMouseAction(IntPtr wParam)
        {
            switch ((int)wParam)
            {
                case WM_LBUTTONDOWN:
                case WM_RBUTTONDOWN:
                    return "Click";
                case WM_MOUSEMOVE:
                    return "Move";
                default:
                    return "Unknown";
            }
        }

        // Windows API Imports
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook,
            IntPtr lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode,
            IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT Point);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetDlgCtrlID(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }
    }

    // Event argument classes
    public class KeyboardEventArgs : EventArgs
    {
        public int VirtualKeyCode { get; set; }
        public string Key { get; set; }
        public bool IsKeyDown { get; set; }
        public DateTime Timestamp { get; set; }
        public WindowInfo ActiveWindow { get; set; }
    }

    public class MouseEventArgs : EventArgs
    {
        public int X { get; set; }
        public int Y { get; set; }
        public string Button { get; set; }
        public string Action { get; set; }
        public DateTime Timestamp { get; set; }
        public WindowInfo ActiveWindow { get; set; }
    }

    public class UIElementEventArgs : EventArgs
    {
        public IntPtr Handle { get; set; }
        public string Text { get; set; }
        public string ClassName { get; set; }
        public int ControlId { get; set; }
        public string ElementType { get; set; }
        public Point Position { get; set; }
        public AccessibilityInfo AccessibilityInfo { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class WindowInfo
    {
        public IntPtr Handle { get; set; }
        public string Title { get; set; }
        public string ClassName { get; set; }
        public string ProcessName { get; set; }
        public uint ProcessId { get; set; }
    }

    public class AccessibilityInfo
    {
        public string Name { get; set; }
        public string Role { get; set; }
        public string Value { get; set; }
        public string Description { get; set; }
        public bool IsBrowser { get; set; }
    }

    // Usage example
    public class Program
    {
        public static void Main()
        {
            var agent = new DesktopAgent();

            agent.KeyboardEvent += (sender, e) =>
            {
                Console.WriteLine($"Key: {e.Key} ({(e.IsKeyDown ? "Down" : "Up")}) " +
                                $"in {e.ActiveWindow.ProcessName} - {e.ActiveWindow.Title}");
            };

            agent.MouseEvent += (sender, e) =>
            {
                if (e.Action == "Click")
                {
                    Console.WriteLine($"Mouse {e.Button} click at ({e.X}, {e.Y}) " +
                                    $"in {e.ActiveWindow.ProcessName}");
                }
            };

            agent.UIElementEvent += (sender, e) =>
            {
                Console.WriteLine($"UI Element: {e.ElementType} - '{e.Text}' " +
                                $"(Class: {e.ClassName})");
            };

            agent.StartMonitoring();
            
            Console.WriteLine("Desktop agent started. Press any key to stop...");
            Console.ReadKey();
            
            agent.StopMonitoring();
        }
    }
}
