using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using System.Collections.Generic;
using System.Linq;

namespace WindowsDesktopAgent.Enhanced
{
    public class EnhancedUICapture
    {
        public static DetailedUIElementInfo GetDetailedElementInfo(IntPtr hwnd, int x, int y)
        {
            var info = new DetailedUIElementInfo
            {
                Handle = hwnd,
                Position = new System.Drawing.Point(x, y),
                Timestamp = DateTime.Now
            };

            // Get basic window information
            info.WindowInfo = GetBasicWindowInfo(hwnd);

            // Try UI Automation first (works for most modern applications)
            try
            {
                var element = AutomationElement.FromPoint(new System.Windows.Point(x, y));
                if (element != null)
                {
                    info.AutomationInfo = ExtractAutomationInfo(element);
                    info.IsAccessible = true;
                }
            }
            catch (Exception ex)
            {
                info.AutomationError = ex.Message;
            }

            // For browsers, try to get additional context
            if (IsBrowserWindow(info.WindowInfo.ProcessName))
            {
                info.BrowserInfo = GetBrowserSpecificInfo(hwnd, x, y);
            }

            // Get accessible object information (legacy)
            info.AccessibleObjectInfo = GetAccessibleObjectInfo(hwnd, x, y);

            return info;
        }

        private static AutomationElementInfo ExtractAutomationInfo(AutomationElement element)
        {
            var info = new AutomationElementInfo();

            try
            {
                info.Name = element.Current.Name;
                info.AutomationId = element.Current.AutomationId;
                info.ClassName = element.Current.ClassName;
                info.ControlType = element.Current.ControlType.LocalizedControlType;
                info.IsEnabled = element.Current.IsEnabled;
                info.IsVisible = !element.Current.IsOffscreen;
                info.HasKeyboardFocus = element.Current.HasKeyboardFocus;
                info.BoundingRectangle = element.Current.BoundingRectangle;

                // Try to get value pattern (for text inputs)
                if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object valuePattern))
                {
                    info.Value = ((ValuePattern)valuePattern).Current.Value;
                    info.IsReadOnly = ((ValuePattern)valuePattern).Current.IsReadOnly;
                }

                // Try to get text pattern (for rich text)
                if (element.TryGetCurrentPattern(TextPattern.Pattern, out object textPattern))
                {
                    try
                    {
                        var text = ((TextPattern)textPattern).DocumentRange.GetText(-1);
                        if (!string.IsNullOrEmpty(text) && text.Length <= 1000) // Limit text length
                            info.TextContent = text;
                    }
                    catch { } // Ignore text extraction errors
                }

                // Try to get selection pattern (for dropdowns, lists)
                if (element.TryGetCurrentPattern(SelectionPattern.Pattern, out object selectionPattern))
                {
                    var selectedItems = ((SelectionPattern)selectionPattern).Current.GetSelection();
                    info.SelectedItems = selectedItems?.Select(item => item.Current.Name).ToArray();
                }

                // Get parent context for better understanding
                var parent = element.Parent;
                if (parent != null)
                {
                    info.ParentName = parent.Current.Name;
                    info.ParentControlType = parent.Current.ControlType.LocalizedControlType;
                }
            }
            catch (Exception ex)
            {
                info.ErrorMessage = ex.Message;
            }

            return info;
        }

        private static BrowserInfo GetBrowserSpecificInfo(IntPtr hwnd, int x, int y)
        {
            var browserInfo = new BrowserInfo();
            
            // Get URL from browser (this is simplified - real implementation would vary by browser)
            browserInfo.Url = GetBrowserUrl(hwnd);
            
            // For Chrome/Edge, try to get additional DOM information
            // This would require browser extensions or CDP in a full implementation
            browserInfo.BrowserType = DetectBrowserType(hwnd);
            
            return browserInfo;
        }

        private static string GetBrowserUrl(IntPtr hwnd)
        {
            // This is a simplified approach - real implementation would use:
            // - Chrome DevTools Protocol for Chrome/Edge
            // - Browser-specific accessibility APIs
            // - Browser extensions
            // - DDE for older browsers

            try
            {
                // Try to get URL from window title (works for some browsers)
                var sb = new StringBuilder(512);
                GetWindowText(hwnd, sb, sb.Capacity);
                var title = sb.ToString();
                
                // Some browsers include URL in title or accessible name
                var element = AutomationElement.FromHandle(hwnd);
                var urlElement = element?.FindFirst(TreeScope.Descendants, 
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
                
                if (urlElement != null)
                {
                    if (urlElement.TryGetCurrentPattern(ValuePattern.Pattern, out object pattern))
                    {
                        var url = ((ValuePattern)pattern).Current.Value;
                        if (IsValidUrl(url))
                            return url;
                    }
                }
            }
            catch { }

            return "Unknown";
        }

        private static bool IsValidUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out Uri result) && 
                   (result.Scheme == Uri.UriSchemeHttp || result.Scheme == Uri.UriSchemeHttps);
        }

        private static string DetectBrowserType(IntPtr hwnd)
        {
            var windowInfo = GetBasicWindowInfo(hwnd);
            var processName = windowInfo.ProcessName.ToLower();

            var browserMap = new Dictionary<string, string>
            {
                {"chrome", "Chrome"},
                {"msedge", "Edge"},
                {"firefox", "Firefox"},
                {"opera", "Opera"},
                {"brave", "Brave"},
                {"safari", "Safari"}
            };

            return browserMap.FirstOrDefault(kvp => processName.Contains(kvp.Key)).Value ?? "Unknown";
        }

        private static AccessibleObjectInfo GetAccessibleObjectInfo(IntPtr hwnd, int x, int y)
        {
            // Legacy MSAA support for older applications
            try
            {
                var acc = AccessibleObjectFromWindow(hwnd);
                if (acc != null)
                {
                    return new AccessibleObjectInfo
                    {
                        Name = acc.accName,
                        Role = acc.accRole?.ToString(),
                        Value = acc.accValue,
                        Description = acc.accDescription,
                        State = acc.accState?.ToString()
                    };
                }
            }
            catch { }

            return null;
        }

        private static WindowInfo GetBasicWindowInfo(IntPtr hwnd)
        {
            var sb = new StringBuilder(256);
            GetWindowText(hwnd, sb, sb.Capacity);
            
            var className = new StringBuilder(256);
            GetClassName(hwnd, className, className.Capacity);

            GetWindowThreadProcessId(hwnd, out uint processId);
            var process = System.Diagnostics.Process.GetProcessById((int)processId);

            return new WindowInfo
            {
                Handle = hwnd,
                Title = sb.ToString(),
                ClassName = className.ToString(),
                ProcessName = process.ProcessName,
                ProcessId = processId
            };
        }

        private static bool IsBrowserWindow(string processName)
        {
            var browsers = new[] { "chrome", "firefox", "edge", "msedge", "opera", "brave", "safari", "iexplore" };
            return browsers.Any(browser => processName.ToLower().Contains(browser));
        }

        // Simplified MSAA access (you'd need proper COM interop for full implementation)
        private static dynamic AccessibleObjectFromWindow(IntPtr hwnd)
        {
            // This is a placeholder - real implementation would use:
            // AccessibleObjectFromWindow API with proper COM marshaling
            return null;
        }

        // Windows API imports
        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    }

    // Enhanced data classes
    public class DetailedUIElementInfo
    {
        public IntPtr Handle { get; set; }
        public System.Drawing.Point Position { get; set; }
        public DateTime Timestamp { get; set; }
        public WindowInfo WindowInfo { get; set; }
        public AutomationElementInfo AutomationInfo { get; set; }
        public BrowserInfo BrowserInfo { get; set; }
        public AccessibleObjectInfo AccessibleObjectInfo { get; set; }
        public bool IsAccessible { get; set; }
        public string AutomationError { get; set; }
    }

    public class AutomationElementInfo
    {
        public string Name { get; set; }
        public string AutomationId { get; set; }
        public string ClassName { get; set; }
        public string ControlType { get; set; }
        public bool IsEnabled { get; set; }
        public bool IsVisible { get; set; }
        public bool HasKeyboardFocus { get; set; }
        public System.Windows.Rect BoundingRectangle { get; set; }
        public string Value { get; set; }
        public bool IsReadOnly { get; set; }
        public string TextContent { get; set; }
        public string[] SelectedItems { get; set; }
        public string ParentName { get; set; }
        public string ParentControlType { get; set; }
        public string ErrorMessage { get; set; }
    }

    public class BrowserInfo
    {
        public string Url { get; set; }
        public string BrowserType { get; set; }
        public string PageTitle { get; set; }
        public string DomPath { get; set; } // XPath or CSS selector
        public Dictionary<string, string> Attributes { get; set; }
    }

    public class AccessibleObjectInfo
    {
        public string Name { get; set; }
        public string Role { get; set; }
        public string Value { get; set; }
        public string Description { get; set; }
        public string State { get; set; }
    }
}
