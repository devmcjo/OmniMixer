using System.Globalization;
using System.Windows.Data;

namespace OmniMixer.Converters;

/// <summary>
/// dB 값을 읽기 쉬운 텍스트로 변환 (-80dB 이하는 "-∞")
/// </summary>
public sealed class LevelToDbTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not float db)
            return "0 dB";

        // -80dB 이하는 무음으로 간주
        if (db <= -79.0f)
            return "-∞";

        // 소수점 처리: -10dB 이상은 소수점 1자리, 이하는 정수
        if (db >= -10.0f)
            return $"{db:F1} dB";
        else
            return $"{db:F0} dB";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
