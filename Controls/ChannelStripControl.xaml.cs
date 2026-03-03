using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OmniMixer.ViewModels;

namespace OmniMixer.Controls;

/// <summary>
/// 프로페셔널 오디오 믹서 스타일의 채널 스트립 컨트롤
/// Apple Logic Pro / Pro Tools 스타일의 세련된 디자인
/// </summary>
public partial class ChannelStripControl : UserControl
{
    public ChannelStripControl()
    {
        InitializeComponent();

        // 더블클릭 이벤트 핸들러 등록
        Loaded += (s, e) =>
        {
            if (PanSlider != null)
            {
                PanSlider.MouseDoubleClick += OnPanSliderDoubleClick;
            }

            if (VolumeSlider != null)
            {
                VolumeSlider.MouseDoubleClick += OnVolumeSliderDoubleClick;
            }
        };

        Unloaded += (s, e) =>
        {
            if (PanSlider != null)
            {
                PanSlider.MouseDoubleClick -= OnPanSliderDoubleClick;
            }

            if (VolumeSlider != null)
            {
                VolumeSlider.MouseDoubleClick -= OnVolumeSliderDoubleClick;
            }
        };
    }

    /// <summary>
    /// PAN 슬라이더 더블클릭: 중앙(0)으로 리셋
    /// </summary>
    private void OnPanSliderDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ChannelViewModel viewModel)
        {
            viewModel.Pan = 0.0f;
            e.Handled = true;
        }
    }

    /// <summary>
    /// 볼륨 슬라이더 더블클릭: Unity Gain (0dB)으로 리셋
    /// </summary>
    private void OnVolumeSliderDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ChannelViewModel viewModel)
        {
            viewModel.VolumeDb = 0.0f;
            e.Handled = true;
        }
    }
}
