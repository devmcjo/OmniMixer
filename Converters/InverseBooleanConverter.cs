using System.Globalization;
using System.Windows.Data;

namespace OmniMixer.Converters;

/// <summary>
/// Boolean 값을 반전하는 컨버터
/// Start/Stop 상태에 따라 ComboBox 비활성화 등에 사용
/// </summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
            return !boolValue;
        return value ?? false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
            return !boolValue;
        return value ?? false;
    }
}
