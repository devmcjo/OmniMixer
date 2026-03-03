using System.Windows;
using OmniMixer.Converters;

namespace OmniMixer;

/// <summary>
/// OmniMixer 메인 윈도우
/// Apple 스타일의 프로페셔널 오디오 믹서 인터페이스
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        // Converters를 Application 리소스에 등록
        RegisterConverters();

        InitializeComponent();
    }

    /// <summary>
    /// XAML 전역에서 사용할 수 있도록 Converters를 Application 리소스에 등록
    /// </summary>
    private void RegisterConverters()
    {
        if (Application.Current == null) return;

        var resources = Application.Current.Resources;

        // 기본 Boolean 변환
        resources["InverseBooleanConverter"] = new InverseBooleanConverter();

        // Start/Stop 버튼 색상
        resources["BooleanToStartStopBrushConverter"] = new BooleanToStartStopBrushConverter();

        // dB ↔ 슬라이더 위치 (오디오 페이더 테이퍼)
        resources["DbToSliderPositionConverter"] = new DbToSliderPositionConverter();

        // 레벨 → 색상 (미터 그라데이션)
        resources["LevelToColorConverter"] = new LevelToColorConverter();

        // dB 텍스트 표시
        resources["LevelToDbTextConverter"] = new LevelToDbTextConverter();

        // 레벨 → 높이 (로그 스케일)
        resources["LevelToHeightConverter"] = new LevelToHeightConverter();
    }
}
