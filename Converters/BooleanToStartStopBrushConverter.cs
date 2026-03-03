using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace OmniMixer.Converters;

/// <summary>
/// Start/Stop 버튼의 상태에 따라 색상을 반환하는 컨버터
/// Start: Apple 스타일 그린, Stop: 경고 레드
/// </summary>
public sealed class BooleanToStartStopBrushConverter : IValueConverter
{
    // Apple 스타일 색상 팔레트
    private static readonly SolidColorBrush StartBrush = CreateFrozenBrush(52, 199, 89);   // 시스템 그린
    private static readonly SolidColorBrush StopBrush = CreateFrozenBrush(255, 59, 48);   // 시스템 레드
    private static readonly SolidColorBrush DisabledBrush = CreateFrozenBrush(142, 142, 147); // 시스템 그레이

    private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isRunning)
            return isRunning ? StopBrush : StartBrush;
        return StartBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
