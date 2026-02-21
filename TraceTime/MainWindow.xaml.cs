using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TraceTime.Models;
using TraceTime.Services;
using Forms = System.Windows.Forms;

namespace TraceTime
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<ActivityRecord> RecentActivities { get; set; } = new ObservableCollection<ActivityRecord>();

        private DispatcherTimer? _timer;
        private Forms.NotifyIcon? _notifyIcon;

        private HashSet<string> _expandedApps = new HashSet<string>();

        private int _silenceCounter = 0;

        public MainWindow()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey? key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\TraceTime"))
                {
                    string lang = key?.GetValue("Language")?.ToString() ?? "en-US";
                    var culture = new System.Globalization.CultureInfo(lang);
                    System.Threading.Thread.CurrentThread.CurrentUICulture = culture;
                    System.Threading.Thread.CurrentThread.CurrentCulture = culture;
                }

                InitializeComponent();

                string currentLang = System.Threading.Thread.CurrentThread.CurrentUICulture.Name;
                foreach (ComboBoxItem item in LangChangeCombo.Items)
                {
                    if (item.Tag?.ToString() == currentLang)
                    {
                        LangChangeCombo.SelectedItem = item;
                        break;
                    }
                }

                DatabaseService.Initialize();
                ActivityList.ItemsSource = RecentActivities;

                SetupTrayIcon();
                SetupTimer();

                SetAutostart(true);

                AutostartCheckbox.IsChecked = IsAutostartEnabled();

                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\TraceTime"))
                {
                    if (key != null)
                    {
                        int privacy = (int)(key.GetValue("PrivacyMode") ?? 0);
                        PrivacyModeCheckbox.IsChecked = (privacy == 1);
                    }
                }

                string[] args = Environment.GetCommandLineArgs();
                if (args.Contains("-silent"))
                {
                    this.WindowState = WindowState.Minimized;
                    this.Hide();
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Fatal error while starting the application:\n\n{ex.Message}\n\nThis may be a problem with the icon in the Resources folder or with access to the registry.",
                    "TraceTime - Start Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);

                System.Windows.Application.Current.Shutdown();
            }
        }

        private void SetupTimer()
        {
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += OnTimerTick;
            _timer.Start();
        }

        private void OnTimerTick(object? sender, EventArgs e)
        {
            try
            {
                foreach (var activity in RecentActivities)
                {
                    activity.CurrentStatus = "";
                }

                var info = MonitoringService.GetActiveWindowInfo();
                bool isAudioPlaying = MonitoringService.IsAudioPlaying();

                string audioApp = "";
                string audioTitle = "";
                if (isAudioPlaying)
                {
                    audioApp = MonitoringService.GetAppProcessWithActiveAudio();
                    audioTitle = !string.IsNullOrEmpty(audioApp) ? string.Format(Strings.AudioStatusFormatLng, audioApp) : "";
                }

                bool isPrimaryOccupied = false;

                if (!info.HasValue)
                {
                    if (isAudioPlaying && !string.IsNullOrEmpty(audioApp))
                    {
                        ProcessActivity(audioApp, audioTitle, isBackground: true, isPrimary: true);

                        CurrentAppText.Text = audioApp.ToUpper() + Strings.InBckgLng;
                        CurrentTitleText.Text = audioTitle;
                    }
                    else
                    {
                        CurrentAppText.Text = Strings.NoActivityLng;
                        CurrentTitleText.Text = "";
                    }
                    UpdateLiveCounter();
                    return;
                }

                string currentApp = info.Value.App;
                string currentTitle = MonitoringService.FormatTwitterTitle(MonitoringService.CleanTitle(info.Value.Title));
                bool isExplorer = currentApp.ToLower().Contains("explorer");

                double idleSec = MonitoringService.GetIdleSeconds();
                if (idleSec > 300 && !isAudioPlaying)
                {
                    _silenceCounter++;
                    if (_silenceCounter > 15)
                    {
                        CurrentAppText.Text = Strings.IdleLng;
                        UpdateLiveCounter();
                        return;
                    }
                }
                else { _silenceCounter = 0; }

                if (!isExplorer)
                {
                    ProcessActivity(currentApp, currentTitle, isBackground: false, isPrimary: true);
                    isPrimaryOccupied = true;
                }

                if (isAudioPlaying && !string.IsNullOrEmpty(audioApp))
                {
                    bool isSameAsFocus = audioApp.ToLower() == currentApp.ToLower();

                    if (!isSameAsFocus)
                    {
                        bool audioShouldBePrimary = !isPrimaryOccupied;

                        ProcessActivity(audioApp, audioTitle, isBackground: true, isPrimary: audioShouldBePrimary);

                        if (audioShouldBePrimary) isPrimaryOccupied = true;
                    }
                }

                if (isExplorer && isAudioPlaying && !string.IsNullOrEmpty(audioApp))
                {
                    CurrentAppText.Text = audioApp.ToUpper();
                    CurrentTitleText.Text = audioTitle;
                }
                else
                {
                    CurrentAppText.Text = currentApp.ToUpper();
                    CurrentTitleText.Text = currentTitle;
                }

                UpdateLiveCounter();

                if (RangeCombo?.SelectedItem is ComboBoxItem selectedItem)
                {
                    string range = selectedItem.Content?.ToString() ?? "";
                    if (range == Strings.TodayLng || range == Strings.Last24hLng) UpdateStatsView();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(string.Format(Strings.TimerErrorLng, ex.Message));
            }
        }

        private void ProcessActivity(string app, string title, bool isBackground = false, bool isPrimary = true)
        {
            string baseTitle = isBackground ? title : MonitoringService.CleanTitle(title);

            DatabaseService.LogActivity(app, baseTitle, isPrimary);

            var existing = RecentActivities.FirstOrDefault(a =>
                a.AppName.ToLower() == app.ToLower() &&
                a.WindowTitle == baseTitle &&
                a.IsPrimary == isPrimary);

            if (existing != null)
            {
                existing.Duration++;

                existing.CurrentStatus = isBackground ? Strings.BackgroundStatusLng : Strings.FocusStatusLng;

                TimeSpan t = TimeSpan.FromSeconds(existing.Duration);
                existing.Tag = string.Format("{0:D2}h {1:D2}m {2:D2}s", (int)t.TotalHours, t.Minutes, t.Seconds);

                int oldIndex = RecentActivities.IndexOf(existing);
                if (oldIndex > 0) RecentActivities.Move(oldIndex, 0);
            }
            else
            {
                var newRecord = new ActivityRecord
                {
                    AppName = app,
                    WindowTitle = baseTitle,
                    Duration = 1,
                    IsPrimary = isPrimary,
                    CurrentStatus = isBackground ? Strings.BackgroundStatusLng : Strings.FocusStatusLng
                };

                DatabaseService.InsertNewActivity(newRecord);
                RecentActivities.Insert(0, newRecord);
            }
        }

        private void SetupTrayIcon()
        {
            _notifyIcon = new System.Windows.Forms.NotifyIcon();

            try
            {
                Stream iconStream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Resources/tt_icon.ico")).Stream;
                _notifyIcon.Icon = new System.Drawing.Icon(iconStream);
            }
            catch
            {
                _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
            }

            _notifyIcon.Text = Strings.TTActivityMonitoringLng;
            _notifyIcon.Visible = true;

            var contextMenu = new System.Windows.Forms.ContextMenuStrip();

            contextMenu.Items.Add(Strings.OpenTraceTimeLng, null, (s, e) => {
                this.Show();
                this.WindowState = WindowState.Normal;
                this.Activate();
            });

            contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            contextMenu.Items.Add(Strings.EndAppLng, null, (s, e) => {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                System.Windows.Application.Current.Shutdown();
            });

            _notifyIcon.ContextMenuStrip = contextMenu;

            _notifyIcon.DoubleClick += (s, e) => {
                this.Show();
                this.WindowState = WindowState.Normal;
                this.Activate();
            };
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            this.Hide();

            _notifyIcon?.ShowBalloonTip(500, Strings.TraceTimeLng, Strings.StillMonitoringTime, Forms.ToolTipIcon.Info);
        }

        protected override void OnStateChanged(EventArgs e)
        {
            if (this.WindowState == WindowState.Minimized)
            {
                this.Hide();
                if (_notifyIcon != null)
                {
                    _notifyIcon.ShowBalloonTip(1000, Strings.TraceTimeLng, Strings.AppRunsBckg, Forms.ToolTipIcon.Info);
                }
            }
            base.OnStateChanged(e);
        }

        private void RangeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateStatsView();
        }
        private void RefreshStats_Click(object sender, RoutedEventArgs e)
        {
            UpdateStatsView();
        }

        private void UpdateStatsView()
        {
            if (!this.IsLoaded || RangeCombo == null || ViewTypeCombo == null || StatsList == null || StatsBars == null) return;

            string range = (RangeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? Strings.TodayLng;
            var rawStats = DatabaseService.GetStats(range);

            if (rawStats == null) return;

            bool showDetailed = DetailedTimeCheckbox?.IsChecked ?? false;
            List<ActivityRecord> processedStats;

            if (showDetailed)
            {
                processedStats = rawStats
                    .GroupBy(s => s.AppName)
                    .Select(g => new ActivityRecord
                    {
                        AppName = g.Key,
                        Duration = g.Sum(x => x.Duration),
                        WindowTitle = Strings.TotalActivity,
                        IsPrimary = true,
                        Details = g.SelectMany(x => x.Details ?? new List<ActivityRecord>()).ToList()
                    })
                    .OrderByDescending(x => x.Duration)
                    .ToList();
            }
            else
            {
                processedStats = rawStats
                    .Where(s => s.IsPrimary)
                    .OrderByDescending(s => s.Duration)
                    .ToList();
            }

            long filteredTotalSeconds = processedStats.Sum(s => (long)s.Duration);
            string formattedFiltered = FormatSeconds(filteredTotalSeconds);

            if (TotalFilteredText != null) TotalFilteredText.Text = formattedFiltered;
            if (FilterLabelText != null) FilterLabelText.Text = range.ToUpper();

            StatsList.ItemsSource = null;
            StatsBars.ItemsSource = null;

            UpdateHeatMap();

            if (processedStats.Count == 0) return;

            double maxDuration = processedStats.Max(s => s.Duration);
            if (maxDuration <= 0) maxDuration = 1;

            foreach (var s in processedStats)
            {
                TimeSpan t = TimeSpan.FromSeconds(s.Duration);
                s.Tag = string.Format("{0:D2}h {1:D2}m {2:D2}s", (int)t.TotalHours, t.Minutes, t.Seconds);

                s.IsExpanded = _expandedApps != null && _expandedApps.Contains(s.AppName);
                s.BarWidth = (s.Duration / maxDuration) * 400;

                if (s.Details != null)
                {
                    foreach (var detail in s.Details)
                    {
                        TimeSpan dt = TimeSpan.FromSeconds(detail.Duration);
                        detail.Tag = string.Format("{0:D2}h {1:D2}m {2:D2}s", (int)dt.TotalHours, dt.Minutes, dt.Seconds);
                    }
                }
            }

            string viewType = (ViewTypeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? Strings.PostsLng;
            bool isTable = viewType == Strings.TableLng;

            StatsList.Visibility = isTable ? Visibility.Visible : Visibility.Collapsed;
            StatsBars.Visibility = isTable ? Visibility.Collapsed : Visibility.Visible;

            if (isTable) StatsList.ItemsSource = processedStats;
            else StatsBars.ItemsSource = processedStats;
        }

        private string FormatSeconds(long seconds)
        {
            TimeSpan t = TimeSpan.FromSeconds(seconds);
            return string.Format("{0:D2}h {1:D2}m {2:D2}s", (int)t.TotalHours, t.Minutes, t.Seconds);
        }

        private void Bar_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as System.Windows.Controls.Button;

            if (button?.DataContext is ActivityRecord record)
            {
                if (record != null && record.Details.Count > 0)
                {
                    if (_expandedApps.Contains(record.AppName))
                        _expandedApps.Remove(record.AppName);
                    else
                        _expandedApps.Add(record.AppName);

                    UpdateStatsView();
                }
            }
        }

        private void UpdateLiveCounter()
        {
            var todayStats = DatabaseService.GetStats(Strings.TodayLng);
            if (todayStats == null) return;

            long seconds = todayStats
                .Where(s => s.IsPrimary)
                .Sum(s => (long)s.Duration);

            string formatted = FormatSeconds(seconds);

            if (TotalTodayText != null) TotalTodayText.Text = formatted;
            if (TotalTodayStatsText != null) TotalTodayStatsText.Text = formatted;
        }

        private void DetailedTime_Changed(object sender, RoutedEventArgs e)
        {
            UpdateLiveCounter();
        }

        private void UpdateHeatMap()
        {
            var dailyData = DatabaseService.GetDailyTotals() ?? new Dictionary<string, int>();
            var days = new List<HeatMapDay>();

            for (int i = 29; i >= 0; i--)
            {
                var date = DateTime.Now.AddHours(-4).Date.AddDays(-i);
                string dateKey = date.ToString("yyyy-MM-dd");

                double hours = 0;
                if (dailyData.ContainsKey(dateKey))
                {
                    hours = dailyData[dateKey] / 3600.0;
                }

                days.Add(new HeatMapDay
                {
                    Color = GetColorForHours(hours),
                    ToolTipText = $"{dateKey}: {hours:F1}h"
                });
            }

            if (HeatMapControl != null)
            {
                HeatMapControl.ItemsSource = days;
            }
        }

        private string GetColorForHours(double hours)
        {
            if (hours <= 0) return "#212121";
            if (hours < 1) return "#0E4429";
            if (hours < 3) return "#006D32";
            if (hours < 6) return "#26A641";
            return "#39D353";
        }
        private void LangChangeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;

            if (LangChangeCombo.SelectedItem is ComboBoxItem selected)
            {
                string cultureCode = selected.Tag?.ToString() ?? "en-US";

                using (Microsoft.Win32.RegistryKey? key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"SOFTWARE\TraceTime"))
                {
                    if (key != null)
                    {
                        key.SetValue("Language", cultureCode);
                    }
                }

                var result = System.Windows.MessageBox.Show(
                    Strings.LanguageChangeRestartNote,
                    Strings.TraceTimeLng,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    string? exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                    if (exePath != null)
                    {
                        System.Diagnostics.Process.Start(exePath);
                        System.Windows.Application.Current.Shutdown();
                    }
                }
            }
        }

        private void PrivacyMode_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;

            bool isPrivate = PrivacyModeCheckbox.IsChecked ?? false;
            using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"SOFTWARE\TraceTime"))
            {
                key.SetValue("PrivacyMode", isPrivate ? 1 : 0);
            }
        }

        private void EnsureAutostart()
        {
            try
            {
                string appName = "TraceTime";
                string? exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;

                if (exePath != null)
                {
                    using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                    {
                        if (key != null)
                        {
                            key.SetValue(appName, $"\"{exePath}\"");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Autostart error: " + ex.Message);
            }
        }

        private void SetAutostart(bool enable)
        {
            try
            {
                string path = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(path, true)!)
                {
                    if (key == null) return;

                    if (enable)
                    {
                        string executablePath = Environment.ProcessPath ??
                                                System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ??
                                                string.Empty;

                        key.SetValue("TraceTime", $"\"{executablePath}\" -silent");
                    }
                    else
                    {
                        key.DeleteValue("TraceTime", false);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Autostart error: " + ex.Message);
            }
        }

        private void Autostart_Changed(object sender, RoutedEventArgs e)
        {
            EnsureAutostart();
        }

        private bool IsAutostartEnabled()
        {
            try
            {
                string path = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(path))
                {
                    return key?.GetValue("TraceTime") != null;
                }
            }
            catch { return false; }
        }
    }
}