using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TraceTime.Services
{
    public static class IconHelper
    {
        public static ImageSource? GetIcon(string processName)
        {
            try
            {
                var processes = Process.GetProcessesByName(processName);
                if (processes.Length > 0)
                {
                    var process = processes[0];
                    string? filePath = process.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(filePath))
                    {
                        using (Icon? icon = Icon.ExtractAssociatedIcon(filePath))
                        {
                            if (icon != null)
                            {
                                return Imaging.CreateBitmapSourceFromHIcon(
                                    icon.Handle,
                                    Int32Rect.Empty,
                                    BitmapSizeOptions.FromEmptyOptions());
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            return null;
        }
    }
}