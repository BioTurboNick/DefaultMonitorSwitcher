using System.Globalization;
using System.Windows.Data;
using DefaultMonitorSwitcher.Core;

namespace DefaultMonitorSwitcher.UI;

public sealed class TrayIconStateToImageSourceConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) => null;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class SwitcherStateToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is SwitcherState s ? s.ToString() : string.Empty;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
