using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
namespace TraceTime.Services
{
    public partial class MonitoringService
    {
        [StructLayout(LayoutKind.Sequential)]
        struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private delegate bool EnumWindowsProc(IntPtr hWnd, int lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, int lParam);

        [DllImport("user32.dll")]
        static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
        public static (string App, string Title)? GetActiveWindowInfo()
        {
            IntPtr handle = GetForegroundWindow();
            if (handle == IntPtr.Zero) return null;

            StringBuilder buff = new StringBuilder(256);
            if (GetWindowText(handle, buff, 256) > 0)
            {
                uint pid;
                GetWindowThreadProcessId(handle, out pid);

                try
                {
                    var proc = Process.GetProcessById((int)pid);
                    string appName = proc.ProcessName;
                    if (appName == "ApplicationFrameHost")
                    {
                        return (appName, buff.ToString());
                    }

                    return (appName, buff.ToString());
                }
                catch (ArgumentException) { return null; }
                catch (Exception) { return null; }
            }
            return null;
        }
        public static string GetAppProcessWithActiveAudio()
        {
            try
            {
                using (var enumerator = new MMDeviceEnumerator())
                {
                    var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    var sessionManager = device.AudioSessionManager;
                    var sessions = sessionManager.Sessions;

                    for (int i = 0; i < sessions.Count; i++)
                    {
                        var session = sessions[i];
                        if (session.State == AudioSessionState.AudioSessionStateActive)
                        {
                            int processId = (int)session.GetProcessID;
                            if (processId > 0)
                            {
                                try
                                {
                                    using (var process = Process.GetProcessById(processId))
                                    {
                                        return process.ProcessName;
                                    }
                                }
                                catch { continue; }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error while detecting audio: {ex.Message}");
            }
            return string.Empty;
        }
        public static string GetBackgroundWindowTitle(string audioProcessName, string activeTitle)
        {
            string foundTitle = "";
            var processes = Process.GetProcessesByName(audioProcessName);
            string[] streamPlatforms = { "youtube", "twitch", "netflix", "prime video", "hbo", "disney", "spotify", "kick" };

            EnumWindows((hWnd, lParam) => {
                GetWindowThreadProcessId(hWnd, out uint windowPid);

                if (processes.Any(p => p.Id == windowPid))
                {
                    StringBuilder sb = new StringBuilder(256);
                    GetWindowText(hWnd, sb, sb.Capacity);
                    string title = sb.ToString();

                    if (!string.IsNullOrEmpty(title) && title != activeTitle)
                    {
                        string tLower = title.ToLower();
                        foreach (var platform in streamPlatforms)
                        {
                            if (tLower.Contains(platform))
                            {
                                foundTitle = title;
                                if (platform == "kick") return false;
                            }
                        }

                        if (string.IsNullOrEmpty(foundTitle)) foundTitle = title;
                    }
                }
                return true;
            }, 0);

            return string.IsNullOrEmpty(foundTitle) ? audioProcessName : foundTitle;
        }
        public static bool IsAudioPlaying()
        {
            try
            {
                using (var enumerator = new MMDeviceEnumerator())
                using (var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
                {
                    return device.AudioMeterInformation.MasterPeakValue > 0.005;
                }
            }
            catch { return false; }
        }
        public static double GetIdleSeconds()
        {
            LASTINPUTINFO lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
            GetLastInputInfo(ref lii);
            return (Environment.TickCount - lii.dwTime) / 1000.0;
        }
        public static string FormatTwitterTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return title;

            string[] separators = new[] { " / X", " | X", " X: ", " - X" };
            string rawName = title;

            foreach (var sep in separators)
            {
                if (title.Contains(sep))
                {
                    rawName = title.Split(new[] { sep }, StringSplitOptions.None)[0];
                    break;
                }
            }

            if (rawName.StartsWith("(") && rawName.Contains(")"))
            {
                int closingBracket = rawName.IndexOf(")");
                if (closingBracket > 0 && closingBracket < rawName.Length - 1)
                {
                    rawName = rawName.Substring(closingBracket + 1).Trim();
                }
            }

            string[] noise = new[] { " w serwisie", " on X", " on Twitter" };
            foreach (var word in noise)
            {
                rawName = rawName.Split(new[] { word }, StringSplitOptions.None)[0];
            }

            if (title.Contains(" X") || title.Contains("Twitter"))
            {
                return rawName.Trim() + " (@X)";
            }

            return title;
        }
        public static string CleanTitle(string title)
        {
            if (string.IsNullOrEmpty(title)) return "";
            return new string(title.Where(c => c >= 32 && c < 65533).ToArray());
        }
    }
}