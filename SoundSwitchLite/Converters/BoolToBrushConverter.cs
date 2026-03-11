using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SoundSwitchLite.Converters;

[ValueConversion(typeof(bool), typeof(Brush))]
public class BoolToBrushConverter : IValueConverter
{
    public Brush TrueBrush { get; set; } = new SolidColorBrush(Color.FromRgb(0x6C, 0x63, 0xFF));
    public Brush FalseBrush { get; set; } = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x46));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? TrueBrush : FalseBrush;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
