using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace OmniMixer;

/// <summary>
/// MainWindow 코드 비하인드
/// XAML에서 사용하는 컨버터들을 리소스로 등록한다.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        // XAML에서 사용하는 컨버터들을 Application 리소스에 등록
        if (Application.Current != null)
        {
            Application.Current.Resources["InverseBooleanConverter"] = new InverseBooleanConverter();
            Application.Current.Resources["BooleanToStartStopBrushConverter"] = new BooleanToStartStopBrushConverter();
        }

        InitializeComponent();
    }
}

/// <summary>
/// Boolean 반전 컨버터 (true -> false, false -> true)
/// IsRunning이 true일 때 ComboBox를 비활성화하는 등에 사용
/// </summary>
public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
            return !boolValue;
        return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
            return !boolValue;
        return value;
    }
}

/// <summary>
/// Boolean을 Start/Stop 버튼 색상으로 변환
/// </summary>
public class BooleanToStartStopBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush StartBrush = new SolidColorBrush(Color.FromRgb(76, 175, 80));   // 녹색
    private static readonly SolidColorBrush StopBrush = new SolidColorBrush(Color.FromRgb(211, 47, 47));    // 빨간색

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isRunning)
            return isRunning ? StopBrush : StartBrush;
        return StartBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// dB 값을 슬라이더 값으로 변환 (로그 스케일)
/// Step 3에서 Audio Fader Taper 구현 시 사용
/// </summary>
public class DbToSliderValueConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // 현재는 선형 변환 (Step 3에서 로그 변환 구현)
        return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value;
    }
}

/// <summary>
/// 레벨 값(0.0~1.0)을 미터 색상으로 변환
/// 초록 -> 노랑 -> 빨강 그라데이션
/// Step 3에서 구현
/// </summary>
public class LevelToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is float level)
        {
            // 0.0 ~ 0.7: 녹색
            // 0.7 ~ 0.9: 노랑
            // 0.9 ~ 1.0: 빨강
            if (level < 0.7f)
                return new SolidColorBrush(Color.FromRgb(76, 175, 80));      // 녹색
            else if (level < 0.9f)
                return new SolidColorBrush(Color.FromRgb(255, 193, 7));      // 노랑
            else
                return new SolidColorBrush(Color.FromRgb(244, 67, 54));      // 빨강
        }
        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
