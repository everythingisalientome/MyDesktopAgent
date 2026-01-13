using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WindowsDesktopAgent.Configuration
{
    public class AgentConfig
    {
        public bool CaptureKeyboard { get; set; } = true;
        public bool CaptureMouse { get; set; } = true;
        public bool CaptureUIElements { get; set; } = true;
        public bool DetailedBrowserCapture { get; set; } = true;
        
        // Filtering options
        public HashSet<string> IgnoredProcesses { get; set; } = new HashSet<string>();
        public HashSet<string> MonitoredProcesses { get; set; } = new HashSet<string>(); // Empty means all
        public bool IgnorePasswordFields { get; set; } = true;
        public bool IgnoreSystemApps { get; set; } = true;
        
        // Performance options
        public int MouseMoveThrottleMs { get; set; } = 100; // Throttle mouse move events
        public int MaxTextLength { get; set; } = 1000;
        public bool EnableAsyncProcessing { get; set; } = true;
        
        // Storage options
        public string OutputDirectory { get; set; } = "./captures";
        public string OutputFormat { get; set; } = "json"; // json, csv, xml
        public bool EnableRealTimeOutput { get; set; } = true;
        public int BufferSize { get; set; } = 1000;
        
        // Privacy options
        public HashSet<string> SensitiveFieldNames { get; set; } = new HashSet<string>
        {
            "password", "pwd", "pass", "secret", "token", "key", "ssn", "social",
            "credit", "card", "cvv", "pin", "bank", "account"
        };
        
        public static AgentConfig LoadFromFile(string filePath)
        {
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<AgentConfig>(json) ?? new AgentConfig();
            }
            return new AgentConfig();
        }
        
        public void SaveToFile(string filePath)
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            File.WriteAllText(filePath, json);
        }
    }

    public class EventFilter
    {
        private readonly AgentConfig config;
        
        public EventFilter(AgentConfig config)
        {
            this.config = config;
        }
        
        public bool ShouldCaptureProcess(string processName)
        {
            if (config.IgnoredProcesses.Contains(processName.ToLower()))
                return false;
                
            if (config.MonitoredProcesses.Count > 0)
                return config.MonitoredProcesses.Contains(processName.ToLower());
                
            if (config.IgnoreSystemApps && IsSystemProcess(processName))
                return false;
                
            return true;
        }
        
        public bool ShouldCaptureUIElement(DetailedUIElementInfo elementInfo)
        {
            if (!config.CaptureUIElements)
                return false;
                
            // Check for password fields
            if (config.IgnorePasswordFields && IsPasswordField(elementInfo))
                return false;
                
            // Check for sensitive field names
            if (ContainsSensitiveData(elementInfo))
                return false;
                
            return true;
        }
        
        public string SanitizeText(string text, DetailedUIElementInfo elementInfo)
        {
            if (string.IsNullOrEmpty(text))
                return text;
                
            // Mask sensitive fields
            if (IsPasswordField(elementInfo) || ContainsSensitiveData(elementInfo))
                return new string('*', Math.Min(text.Length, 8));
                
            // Truncate long text
            if (text.Length > config.MaxTextLength)
                return text.Substring(0, config.MaxTextLength) + "...";
                
            return text;
        }
        
        private bool IsSystemProcess(string processName)
        {
            var systemProcesses = new[]
            {
                "dwm", "winlogon", "csrss", "smss", "wininit", "services",
                "lsass", "svchost", "explorer", "taskhost", "dllhost"
            };
            
            return Array.Exists(systemProcesses, p => 
                processName.ToLower().Contains(p));
        }
        
        private bool IsPasswordField(DetailedUIElementInfo elementInfo)
        {
            if (elementInfo?.AutomationInfo == null)
                return false;
                
            var name = elementInfo.AutomationInfo.Name?.ToLower() ?? "";
            var automationId = elementInfo.AutomationInfo.AutomationId?.ToLower() ?? "";
            var controlType = elementInfo.AutomationInfo.ControlType?.ToLower() ?? "";
            
            return (controlType.Contains("password") || 
                    name.Contains("password") || 
                    automationId.Contains("password"));
        }
        
        private bool ContainsSensitiveData(DetailedUIElementInfo elementInfo)
        {
            if (elementInfo?.AutomationInfo == null)
                return false;
                
            var name = elementInfo.AutomationInfo.Name?.ToLower() ?? "";
            var automationId = elementInfo.AutomationInfo.AutomationId?.ToLower() ?? "";
            
            return config.SensitiveFieldNames.Any(sensitive => 
                name.Contains(sensitive) || automationId.Contains(sensitive));
        }
    }

    public class EventLogger
    {
        private readonly AgentConfig config;
        private readonly Queue<object> eventBuffer;
        private readonly object lockObject = new object();
        private DateTime lastMouseMove = DateTime.MinValue;
        
        public EventLogger(AgentConfig config)
        {
            this.config = config;
            this.eventBuffer = new Queue<object>();
            
            if (!Directory.Exists(config.OutputDirectory))
                Directory.CreateDirectory(config.OutputDirectory);
        }
        
        public void LogKeyboardEvent(KeyboardEventArgs keyboardEvent)
        {
            if (!config.CaptureKeyboard)
                return;
                
            var logEntry = new
            {
                Type = "Keyboard",
                Timestamp = keyboardEvent.Timestamp,
                Key = keyboardEvent.Key,
                IsKeyDown = keyboardEvent.IsKeyDown,
                Window = new
                {
                    Title = keyboardEvent.ActiveWindow.Title,
                    Process = keyboardEvent.ActiveWindow.ProcessName,
                    ClassName = keyboardEvent.ActiveWindow.ClassName
                }
            };
            
            WriteLogEntry(logEntry);
        }
        
        public void LogMouseEvent(MouseEventArgs mouseEvent)
        {
            if (!config.CaptureMouse)
                return;
                
            // Throttle mouse move events
            if (mouseEvent.Action == "Move")
            {
                if (DateTime.Now.Subtract(lastMouseMove).TotalMilliseconds < config.MouseMoveThrottleMs)
                    return;
                lastMouseMove = DateTime.Now;
            }
            
            var logEntry = new
            {
                Type = "Mouse",
                Timestamp = mouseEvent.Timestamp,
                Action = mouseEvent.Action,
                Button = mouseEvent.Button,
                Position = new { X = mouseEvent.X, Y = mouseEvent.Y },
                Window = new
                {
                    Title = mouseEvent.ActiveWindow.Title,
                    Process = mouseEvent.ActiveWindow.ProcessName,
                    ClassName = mouseEvent.ActiveWindow.ClassName
                }
            };
            
            WriteLogEntry(logEntry);
        }
        
        public void LogUIElementEvent(DetailedUIElementInfo elementInfo, EventFilter filter)
        {
            if (!config.CaptureUIElements || !filter.ShouldCaptureUIElement(elementInfo))
                return;
                
            var logEntry = new
            {
                Type = "UIElement",
                Timestamp = elementInfo.Timestamp,
                Position = elementInfo.Position,
                Window = new
                {
                    Title = elementInfo.WindowInfo.Title,
                    Process = elementInfo.WindowInfo.ProcessName,
                    ClassName = elementInfo.WindowInfo.ClassName
                },
                Element = elementInfo.AutomationInfo != null ? new
                {
                    Name = filter.SanitizeText(elementInfo.AutomationInfo.Name, elementInfo),
                    AutomationId = elementInfo.AutomationInfo.AutomationId,
                    ControlType = elementInfo.AutomationInfo.ControlType,
                    ClassName = elementInfo.AutomationInfo.ClassName,
                    IsEnabled = elementInfo.AutomationInfo.IsEnabled,
                    Value = filter.SanitizeText(elementInfo.AutomationInfo.Value, elementInfo),
                    TextContent = filter.SanitizeText(elementInfo.AutomationInfo.TextContent, elementInfo),
                    Parent = new
                    {
                        Name = elementInfo.AutomationInfo.ParentName,
                        ControlType = elementInfo.AutomationInfo.ParentControlType
                    }
                } : null,
                Browser = elementInfo.BrowserInfo != null ? new
                {
                    Url = elementInfo.BrowserInfo.Url,
                    Type = elementInfo.BrowserInfo.BrowserType,
                    Title = elementInfo.BrowserInfo.PageTitle
                } : null
            };
            
            WriteLogEntry(logEntry);
        }
        
        private void WriteLogEntry(object logEntry)
        {
            lock (lockObject)
            {
                if (config.EnableRealTimeOutput)
                {
                    WriteToFile(logEntry);
                }
                else
                {
                    eventBuffer.Enqueue(logEntry);
                    if (eventBuffer.Count >= config.BufferSize)
                    {
                        FlushBuffer();
                    }
                }
            }
        }
        
        public void FlushBuffer()
        {
            lock (lockObject)
            {
                while (eventBuffer.Count > 0)
                {
                    WriteToFile(eventBuffer.Dequeue());
                }
            }
        }
        
        private void WriteToFile(object logEntry)
        {
            var fileName = $"capture_{DateTime.Now:yyyyMMdd}.{config.OutputFormat.ToLower()}";
            var filePath = Path.Combine(config.OutputDirectory, fileName);
            
            switch (config.OutputFormat.ToLower())
            {
                case "json":
                    WriteJsonEntry(filePath, logEntry);
                    break;
                case "csv":
                    WriteCsvEntry(filePath, logEntry);
                    break;
                default:
                    WriteJsonEntry(filePath, logEntry);
                    break;
            }
        }
        
        private void WriteJsonEntry(string filePath, object logEntry)
        {
            var json = JsonSerializer.Serialize(logEntry, new JsonSerializerOptions
            {
                WriteIndented = false
            });
            
            File.AppendAllText(filePath, json + Environment.NewLine);
        }
        
        private void WriteCsvEntry(string filePath, object logEntry)
        {
            // Simplified CSV output - you'd want to implement proper CSV serialization
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var type = logEntry.GetType().GetProperty("Type")?.GetValue(logEntry)?.ToString() ?? "Unknown";
            var summary = JsonSerializer.Serialize(logEntry);
            
            var csvLine = $"\"{timestamp}\",\"{type}\",\"{summary.Replace("\"", "\"\"")}\"{Environment.NewLine}";
            
            // Write header if file doesn't exist
            if (!File.Exists(filePath))
            {
                File.WriteAllText(filePath, "Timestamp,Type,Data" + Environment.NewLine);
            }
            
            File.AppendAllText(filePath, csvLine);
        }
    }

    public static class AgentUtilities
    {
        public static bool IsElevated()
        {
            try
            {
                using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
                {
                    var principal = new System.Security.Principal.WindowsPrincipal(identity);
                    return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }
        
        public static void RequestElevation()
        {
            if (!IsElevated())
            {
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    UseShellExecute = true,
                    WorkingDirectory = Environment.CurrentDirectory,
                    FileName = System.Reflection.Assembly.GetExecutingAssembly().Location,
                    Verb = "runas"
                };
                
                try
                {
                    System.Diagnostics.Process.Start(processInfo);
                    Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to elevate privileges: {ex.Message}");
                }
            }
        }
        
        public static string GetApplicationDataPath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var agentPath = Path.Combine(appData, "WindowsDesktopAgent");
            
            if (!Directory.Exists(agentPath))
                Directory.CreateDirectory(agentPath);
                
            return agentPath;
        }
        
        public static void SetupLogging()
        {
            var logPath = Path.Combine(GetApplicationDataPath(), "logs");
            if (!Directory.Exists(logPath))
                Directory.CreateDirectory(logPath);
        }
    }

    // Complete example usage
    public class DesktopAgentService
    {
        private readonly DesktopAgent agent;
        private readonly AgentConfig config;
        private readonly EventFilter filter;
        private readonly EventLogger logger;
        
        public DesktopAgentService(string configPath = null)
        {
            configPath ??= Path.Combine(AgentUtilities.GetApplicationDataPath(), "config.json");
            
            config = AgentConfig.LoadFromFile(configPath);
            filter = new EventFilter(config);
            logger = new EventLogger(config);
            agent = new DesktopAgent();
            
            SetupEventHandlers();
        }
        
        private void SetupEventHandlers()
        {
            agent.KeyboardEvent += (sender, e) =>
            {
                if (filter.ShouldCaptureProcess(e.ActiveWindow.ProcessName))
                {
                    logger.LogKeyboardEvent(e);
                }
            };
            
            agent.MouseEvent += (sender, e) =>
            {
                if (filter.ShouldCaptureProcess(e.ActiveWindow.ProcessName))
                {
                    logger.LogMouseEvent(e);
                }
            };
            
            agent.UIElementEvent += (sender, e) =>
            {
                // Convert to detailed info
                var detailedInfo = EnhancedUICapture.GetDetailedElementInfo(
                    e.Handle, e.Position.X, e.Position.Y);
                    
                if (filter.ShouldCaptureProcess(detailedInfo.WindowInfo.ProcessName))
                {
                    logger.LogUIElementEvent(detailedInfo, filter);
                }
            };
        }
        
        public void Start()
        {
            Console.WriteLine("Starting Desktop Agent...");
            
            if (!AgentUtilities.IsElevated())
            {
                Console.WriteLine("Warning: Running without administrator privileges. Some features may be limited.");
            }
            
            agent.StartMonitoring();
            Console.WriteLine("Desktop Agent started successfully.");
            Console.WriteLine($"Capturing to: {config.OutputDirectory}");
            Console.WriteLine("Press 'q' to quit, 'p' to pause/resume, 's' to save config...");
        }
        
        public void Stop()
        {
            agent.StopMonitoring();
            logger.FlushBuffer();
            Console.WriteLine("Desktop Agent stopped.");
        }
        
        public void SaveConfig(string path = null)
        {
            path ??= Path.Combine(AgentUtilities.GetApplicationDataPath(), "config.json");
            config.SaveToFile(path);
            Console.WriteLine($"Configuration saved to: {path}");
        }
    }
}
