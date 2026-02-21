using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using TraceTime.Services;

namespace TraceTime.Models
{
    public class ActivityRecord : INotifyPropertyChanged
    {
        public string AppName { get; set; } = "";
        public string WindowTitle { get; set; } = "";
        public List<ActivityRecord> Details { get; set; } = new List<ActivityRecord>();

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private int _duration;
        public int Duration
        {
            get => _duration;
            set { _duration = value; OnPropertyChanged(); }
        }
        private bool _isPrimary = true;
        public bool IsPrimary
        {
            get => _isPrimary;
            set { _isPrimary = value; OnPropertyChanged(); }
        }
        private ImageSource? _icon;
        public ImageSource? Icon
        {
            get
            {
                if (_icon == null) _icon = IconHelper.GetIcon(AppName);
                return _icon;
            }
        }
        private string _tag = "";
        public string Tag
        {
            get => _tag;
            set { _tag = value; OnPropertyChanged(); }
        }

        private string _currentStatus = "";
        public string CurrentStatus
        {
            get => _currentStatus;
            set
            {
                _currentStatus = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FullDisplayTitle));
            }
        }

        public string FullDisplayTitle => $"{WindowTitle}{CurrentStatus}";
        private bool _isExpanded = false;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                _isExpanded = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DetailsVisibility));
            }
        }

        public Visibility DetailsVisibility => IsExpanded ? Visibility.Visible : Visibility.Collapsed;
        public double BarWidth { get; set; }
    }
}