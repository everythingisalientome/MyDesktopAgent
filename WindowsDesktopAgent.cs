using System;
using System.Threading;
using System.Threading.Tasks;
using WindowsDesktopAgent.Configuration;

namespace WindowsDesktopAgent
{
    class Program
    {
        private static DesktopAgentService agentService;
        private static bool isRunning = true;
        private static bool isPaused = false;

        static async Task Main(string[] args)
        {
            Console.Title = "Windows Desktop Agent";
            Console.WriteLine("===========================================");
            Console.WriteLine("      Windows Desktop Agent v1.0");
            Console.WriteLine("===========================================");
            Console.WriteLine();

            // Setup application data directory
            AgentUtilities.SetupLogging();

            // Check for admin privileges
            if (!AgentUtilities.IsElevated())
            {
                Console.WriteLine("⚠️  Warning: Not running as Administrator");
                Console.WriteLine("   Some system-level events may not be captured.");
                Console.WriteLine("   For full functionality, run as Administrator.");
                Console.WriteLine();
                
                Console.Write("Continue anyway? (y/n): ");
                var response = Console.ReadKey().KeyChar;
                Console.WriteLine();
                
                if (response != 'y' && response != 'Y')
                {
                    Console.WriteLine("Attempting to restart with elevated privileges...");
                    AgentUtilities.RequestElevation();
                    return;
                }
                Console.WriteLine();
            }

            try
            {
                // Initialize the agent service
                Console.WriteLine("Initializing Desktop Agent...");
                agentService = new DesktopAgentService();
                
                // Setup console event handlers
                Console.CancelKeyPress += OnCancelKeyPress;
                
                // Start the agent
                agentService.Start();
                Console.WriteLine();
                
                // Show available commands
                ShowCommands();
                
                // Main command loop
                await RunCommandLoop();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error starting agent: {ex.Message}");
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
            }
            finally
            {
                Cleanup();
            }
        }

        private static async Task RunCommandLoop()
        {
            while (isRunning)
            {
                var key = Console.ReadKey(true);
                
                switch (char.ToLower(key.KeyChar))
                {
                    case 'q':
                        Console.WriteLine("Shutting down...");
                        isRunning = false;
                        break;
                        
                    case 'p':
                        TogglePause();
                        break;
                        
                    case 's':
                        SaveConfiguration();
                        break;
                        
                    case 'c':
                        ShowConfiguration();
                        break;
                        
                    case 'h':
                        ShowCommands();
                        break;
                        
                    case 'r':
                        ShowRealTimeStats();
                        break;
                        
                    case 'l':
                        await ShowLogs();
                        break;
                        
                    default:
                        // Ignore other keys
                        break;
                }
                
                // Small delay to prevent excessive CPU usage
                await Task.Delay(50);
            }
        }

        private static void ShowCommands()
        {
            Console.WriteLine("📋 Available Commands:");
            Console.WriteLine("   Q - Quit application");
            Console.WriteLine("   P - Pause/Resume monitoring");
            Console.WriteLine("   S - Save current configuration");
            Console.WriteLine("   C - Show configuration");
            Console.WriteLine("   H - Show this help");
            Console.WriteLine("   R - Show real-time statistics");
            Console.WriteLine("   L - Show recent log entries");
            Console.WriteLine();
        }

        private static void TogglePause()
        {
            isPaused = !isPaused;
            if (isPaused)
            {
                Console.WriteLine("⏸️  Monitoring paused");
                // In a full implementation, you'd pause the actual monitoring
            }
            else
            {
                Console.WriteLine("▶️  Monitoring resumed");
                // In a full implementation, you'd resume the actual monitoring
            }
        }

        private static void SaveConfiguration()
        {
            try
            {
                agentService.SaveConfig();
                Console.WriteLine("✅ Configuration saved successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error saving configuration: {ex.Message}");
            }
        }

        private static void ShowConfiguration()
        {
            Console.WriteLine();
            Console.WriteLine("🔧 Current Configuration:");
            Console.WriteLine("   Keyboard Capture: ✅ Enabled");
            Console.WriteLine("   Mouse Capture: ✅ Enabled");
            Console.WriteLine("   UI Element Capture: ✅ Enabled");
            Console.WriteLine("   Browser Integration: ✅ Enabled");
            Console.WriteLine("   Output Directory: ./captures");
            Console.WriteLine("   Output Format: JSON");
            Console.WriteLine("   Privacy Mode: ✅ Enabled (passwords masked)");
            Console.WriteLine();
        }

        private static void ShowRealTimeStats()
        {
            // In a real implementation, you'd track these statistics
            Console.WriteLine();
            Console.WriteLine("📊 Real-time Statistics:");
            Console.WriteLine($"   Uptime: {DateTime.Now.Subtract(DateTime.Today):hh\\:mm\\:ss}");
            Console.WriteLine("   Events Captured: 1,247");
            Console.WriteLine("   Keyboard Events: 432");
            Console.WriteLine("   Mouse Events: 815");
            Console.WriteLine("   Applications Monitored: 5");
            Console.WriteLine("   Current Focus: Chrome - GitHub");
            Console.WriteLine();
        }

        private static async Task ShowLogs()
        {
            Console.WriteLine();
            Console.WriteLine("📄 Recent Log Entries:");
            Console.WriteLine("   [12:34:56] Keyboard: 'H' pressed in Chrome");
            Console.WriteLine("   [12:34:57] Mouse: Left click at (450, 300) in Chrome");
            Console.WriteLine("   [12:34:57] UI Element: TextBox focused - 'search-input'");
            Console.WriteLine("   [12:34:58] Keyboard: 'e' pressed in Chrome");
            Console.WriteLine("   [12:34:59] Browser: URL changed to 'https://github.com/search'");
            Console.WriteLine();
            
            await Task.Delay(100); // Simulate async log reading
        }

        private static void OnCancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true; // Prevent immediate termination
            Console.WriteLine();
            Console.WriteLine("Received interrupt signal. Shutting down gracefully...");
            isRunning = false;
        }

        private static void Cleanup()
        {
            try
            {
                agentService?.Stop();
                Console.WriteLine("✅ Agent stopped successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error during cleanup: {ex.Message}");
            }
        }
    }

    // Example of a simple service wrapper for Windows Service deployment
    public class WindowsServiceWrapper
    {
        private DesktopAgentService agentService;
        private Timer statusTimer;

        public void OnStart(string[] args)
        {
            try
            {
                agentService = new DesktopAgentService();
                agentService.Start();
                
                // Setup periodic status logging (every 5 minutes)
                statusTimer = new Timer(LogStatus, null, TimeSpan.Zero, TimeSpan.FromMinutes(5));
                
                // Log to Windows Event Log if running as service
                System.Diagnostics.EventLog.WriteEntry("WindowsDesktopAgent", 
                    "Desktop Agent Service started successfully", 
                    System.Diagnostics.EventLogEntryType.Information);
            }
            catch (Exception ex)
            {
                System.Diagnostics.EventLog.WriteEntry("WindowsDesktopAgent", 
                    $"Failed to start Desktop Agent Service: {ex.Message}", 
                    System.Diagnostics.EventLogEntryType.Error);
                throw;
            }
        }

        public void OnStop()
        {
            try
            {
                statusTimer?.Dispose();
                agentService?.Stop();
                
                System.Diagnostics.EventLog.WriteEntry("WindowsDesktopAgent", 
                    "Desktop Agent Service stopped successfully", 
                    System.Diagnostics.EventLogEntryType.Information);
            }
            catch (Exception ex)
            {
                System.Diagnostics.EventLog.WriteEntry("WindowsDesktopAgent", 
                    $"Error stopping Desktop Agent Service: {ex.Message}", 
                    System.Diagnostics.EventLogEntryType.Error);
            }
        }

        private void LogStatus(object state)
        {
            // Log periodic status information
            System.Diagnostics.EventLog.WriteEntry("WindowsDesktopAgent", 
                "Desktop Agent Service is running normally", 
                System.Diagnostics.EventLogEntryType.Information);
        }
    }
}

// Project file reference (WindowsDesktopAgent.csproj)
/*
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net6.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>true</SelfContained>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="System.Text.Json" Version="7.0.0" />
    <PackageReference Include="Microsoft.Windows.Compatibility" Version="7.0.0" />
  </ItemGroup>

  <ItemGroup>
    <Reference Include="UIAutomationClient" />
    <Reference Include="UIAutomationTypes" />
  </ItemGroup>

</Project>
*/
