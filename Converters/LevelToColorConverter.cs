using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace OmniMixer.Converters;

/// <summary>
/// 레벨 미터 값(0.0 ~ 1.0)을 그라데이션 색상으로 변환
/// Apple Logic Pro 스타일: 부드러운 그린 → 옐로우 → 오렌지 → 레드
/// </summary>
public sealed class LevelToColorConverter : IValueConverter
{
    // Apple 스타일 색상 팔레트
    private static readonly Color Green = Color.FromRgb(52, 199, 89);
    private static readonly Color Yellow = Color.FromRgb(255, 204, 0);
    private static readonly Color Orange = Color.FromRgb(255, 149, 0);
    private static readonly Color Red = Color.FromRgb(255, 59, 48);
    private static readonly Color DarkGray = Color.FromRgb(44, 44, 46);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not float level)
            return new SolidColorBrush(DarkGray);

        level = Math.Clamp(level, 0.0f, 1.0f);

        Color color;
        if (level < 0.6f)
        {
            // 그린 유지 (0% ~ 60%)
            color = Green;
        }
        else if (level < 0.75f)
        {
            // 그린 → 옐로우 (60% ~ 75%)
            float t = (level - 0.6f) / 0.15f;
            color = InterpolateColor(Green, Yellow, t);
        }
        else if (level < 0.9f)
        {
            // 옐로우 → 오렌지 (75% ~ 90%)
            float t = (level - 0.75f) / 0.15f;
            color = InterpolateColor(Yellow, Orange, t);
        }
        else
        {
            // 오렌지 → 레드 (90% ~ 100%)
            float t = (level - 0.9f) / 0.1f;
            color = InterpolateColor(Orange, Red, t);
        }

        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }

    private static Color InterpolateColor(Color from, Color to, float t)
    {
        t = Math.Clamp(t, 0.0f, 1.0f);
        return Color.FromRgb(
            (byte)(from.R + (to.R - from.R) * t),
            (byte)(from.G + (to.G - from.G) * t),
            (byte)(from.B + (to.B - from.B) * t)
        );
    }
}
