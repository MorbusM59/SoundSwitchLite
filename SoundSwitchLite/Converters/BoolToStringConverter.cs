using System.Globalization;
using System.Windows.Data;

namespace SoundSwitchLite.Converters;

[ValueConversion(typeof(bool), typeof(string))]
public class BoolToStringConverter : IValueConverter
{
    public string TrueString { get; set; } = string.Empty;
    public string FalseString { get; set; } = string.Empty;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? TrueString : FalseString;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
