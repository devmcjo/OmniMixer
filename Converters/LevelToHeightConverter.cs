using System.Globalization;
using System.Windows.Data;

namespace OmniMixer.Converters;

/// <summary>
/// 레벨 값(0.0 ~ 1.0)을 ProgressBar 높이 비율로 변환
/// dB 스케일에 따라 시각적 표현 최적화 (로그 스케일 느낌)
/// </summary>
public sealed class LevelToHeightConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not float level)
            return 0.0;

        level = Math.Clamp(level, 0.0f, 1.0f);

        // 로그 스케일 적용: 낮은 레벨도 눈에 보이도록
        // 0.01 (-40dB) 정도도 약간 보이게
        double logLevel = 20 * Math.Log10(level + 0.0001);
        double normalized = (logLevel + 80) / 80;  // -80dB ~ 0dB를 0~1로

        return Math.Max(0, Math.Min(1, normalized));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
