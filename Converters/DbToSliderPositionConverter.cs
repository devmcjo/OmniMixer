using System.Globalization;
using System.Windows.Data;

namespace OmniMixer.Converters;

/// <summary>
/// 오디오 페이더 테이퍼: dB 값과 슬라이더 위치 간의 로그 스케일 변환
///
/// Apple Logic Pro / Pro Tools 스타일 페이더 특성:
/// - 중간 위치(50%)가 실제 체감 절반 볼륨(-12dB)에 해당
/// - 상단 20%: +6dB ~ -12dB (Unity 주변의 정밀 조정)
/// - 중간 60%: -12dB ~ -40dB (일반적인 사용 범위)
/// - 하단 20%: -40dB ~ -80dB (페이드 아웃)
/// </summary>
public sealed class DbToSliderPositionConverter : IValueConverter
{
    private const float MinDb = -80.0f;
    private const float MaxDb = 6.0f;
    private const float MidDb = -12.0f;  // 50% 지점
    private const float LowDb = -40.0f;  // 10% 지점

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not float db)
            return 0.5;

        db = Math.Clamp(db, MinDb, MaxDb);

        // Region 1: +6dB ~ -12dB → 100% ~ 50%
        if (db >= MidDb)
        {
            float t = (db - MidDb) / (MaxDb - MidDb);
            return 0.5 + t * 0.5;
        }
        // Region 2: -12dB ~ -40dB → 50% ~ 10%
        else if (db >= LowDb)
        {
            float t = (db - LowDb) / (MidDb - LowDb);
            return 0.1 + t * 0.4;
        }
        // Region 3: -40dB ~ -80dB → 10% ~ 0%
        else
        {
            float t = (db - MinDb) / (LowDb - MinDb);
            return t * 0.1;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double position)
            return 0.0f;

        position = Math.Clamp(position, 0.0, 1.0);

        // Region 1: 50% ~ 100% → -12dB ~ +6dB
        if (position >= 0.5)
        {
            float t = (float)((position - 0.5) / 0.5);
            return MidDb + t * (MaxDb - MidDb);
        }
        // Region 2: 10% ~ 50% → -40dB ~ -12dB
        else if (position >= 0.1)
        {
            float t = (float)((position - 0.1) / 0.4);
            return LowDb + t * (MidDb - LowDb);
        }
        // Region 3: 0% ~ 10% → -80dB ~ -40dB
        else
        {
            float t = (float)(position / 0.1);
            return MinDb + t * (LowDb - MinDb);
        }
    }
}
