using Microsoft.UI.Xaml.Media;

namespace Atomic.App.Models
{
    public class FavoriteProperty
    {
        public string Key { get; set; }
        public string DisplayName { get; set; }
        public string Value { get; set; }
        public bool IsSelected { get; set; }
        public bool IsProOnly { get; set; }
        public bool IsSelectable { get; set; } = true;
        /// <summary>
        /// The label, with a note when the property is locked.
        ///
        /// <para>The suffix comes from <c>requires_pro</c>, which is translated in all twelve -
        /// it was an English literal, so a Swedish user saw "Mohs hårdhet (Requires PRO)".</para>
        /// </summary>
        public string DisplayNameWithProSuffix =>
            !IsSelectable && IsProOnly
                ? $"{DisplayName} ({Services.ResourceStringProvider.For(Services.AppLanguage.BaseCode(Services.AppLanguage.Current)).Get(Atomic.Core.Localization.S.requires_pro)})"
                : DisplayName;
        public Brush ForegroundBrush { get; set; } = new SolidColorBrush(Microsoft.UI.Colors.Black);
        public bool IsLast { get; set; }
    }
}
