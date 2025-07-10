using Microsoft.UI.Xaml.Media;

namespace Atomic_WinUI
{
    public class FavoriteProperty
    {
        public string Key { get; set; }
        public string DisplayName { get; set; }
        public string Value { get; set; }
        public bool IsSelected { get; set; }
        public bool IsProOnly { get; set; }
        public bool IsSelectable { get; set; } = true;
        public string DisplayNameWithProSuffix =>
            !IsSelectable && IsProOnly ? $"{DisplayName} (Requires PRO)" : DisplayName;
        public Brush ForegroundBrush { get; set; } = new SolidColorBrush(Microsoft.UI.Colors.Black);
        public bool IsLast { get; set; }
    }
}
