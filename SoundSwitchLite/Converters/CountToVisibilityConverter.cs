using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SoundSwitchLite.Converters;

/// <summary>Returns Visible when the bound integer is greater than zero, Collapsed otherwise.</summary>
[ValueConversion(typeof(int), typeof(Visibility))]
public class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
