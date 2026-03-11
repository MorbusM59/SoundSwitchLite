using System.Globalization;
using System.Windows.Data;

namespace SoundSwitchLite.Converters;

[ValueConversion(typeof(bool), typeof(double))]
public class BoolToOpacityConverter : IValueConverter
{
    public double TrueOpacity { get; set; } = 1.0;
    public double FalseOpacity { get; set; } = 0.4;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? TrueOpacity : FalseOpacity;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
