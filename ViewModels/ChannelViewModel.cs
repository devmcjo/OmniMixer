using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniMixer.Audio;
using OmniMixer.Models;

namespace OmniMixer.ViewModels;

/// <summary>
/// 단일 출력 채널의 ViewModel.
/// ChannelDspProvider와 바인딩되어 볼륨/팬/뮤트를 제어하고,
/// MeterUpdated 이벤트를 통해 레벨 미터 데이터를 UI에 반영한다.
/// </summary>
public sealed partial class ChannelViewModel : ObservableObject, IDisposable
{
    // ─────────────────────────────────────────────────────────
    //  낮부 참조
    // ─────────────────────────────────────────────────────────

    private ChannelDspProvider? _dspProvider;
    private readonly Dispatcher _dispatcher;

    /// <summary>출력 장치 목록 (MainViewModel에서 주입)</summary>
    public ObservableCollection<AudioDeviceItem>? OutputDevices { get; set; }

    // ─────────────────────────────────────────────────────────
    //  Observable Properties (CommunityToolkit.Mvvm 자동 생성)
    // ─────────────────────────────────────────────────────────

    /// <summary>채널 인덱스 (0~7)</summary>
    [ObservableProperty]
    private int _channelIndex;

    /// <summary>선택된 출력 장치 (null이면 미선택)</summary>
    [ObservableProperty]
    private AudioDeviceItem? _selectedOutputDevice;

    /// <summary>선택된 출력 장치 ID (null이면 미선택)</summary>
    [ObservableProperty]
    private string? _selectedDeviceId;

    /// <summary>볼륨 (dB): -80.0 ~ +6.0</summary>
    [ObservableProperty]
    private float _volumeDb = 0.0f;

    /// <summary>팬: -1.0(왼쪽) ~ 0.0(중앙) ~ +1.0(오른쪽)</summary>
    [ObservableProperty]
    private float _pan = 0.0f;

    // ─────────────────────────────────────────────────────────
    //  Partial Methods (CommunityToolkit.Mvvm)
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// P2 Fix: VolumeDb 변경 시 범위 제한 (-80 ~ +6 dB)
    /// </summary>
    partial void OnVolumeDbChanged(float value)
    {
        const float MinDb = -80.0f;
        const float MaxDb = 6.0f;

        if (value < MinDb)
        {
            VolumeDb = MinDb;
        }
        else if (value > MaxDb)
        {
            VolumeDb = MaxDb;
        }
    }

    /// <summary>
    /// P2 Fix: Pan 변경 시 범위 제한 (-1.0 ~ +1.0)
    /// </summary>
    partial void OnPanChanged(float value)
    {
        const float MinPan = -1.0f;
        const float MaxPan = 1.0f;

        if (value < MinPan)
        {
            Pan = MinPan;
        }
        else if (value > MaxPan)
        {
            Pan = MaxPan;
        }
    }

    /// <summary>
    /// 선택된 출력 장치가 변경되면 SelectedDeviceId도 업데이트
    /// </summary>
    partial void OnSelectedOutputDeviceChanged(AudioDeviceItem? value)
    {
        SelectedDeviceId = value?.Id;
    }

    /// <summary>음소거 상태</summary>
    [ObservableProperty]
    private bool _isMuted = false;

    /// <summary>좌측 레벨 미터 (0.0 ~ 1.0)</summary>
    [ObservableProperty]
    private float _meterLevelLeft = 0.0f;

    /// <summary>우측 레벨 미터 (0.0 ~ 1.0)</summary>
    [ObservableProperty]
    private float _meterLevelRight = 0.0f;

    /// <summary>현재 채널이 활성화(재생 중) 상태인지 여부</summary>
    [ObservableProperty]
    private bool _isActive = false;

    /// <summary>오류 메시지 (Hot-unplug 등)</summary>
    [ObservableProperty]
    private string? _errorMessage;

    // ─────────────────────────────────────────────────────────
    //  생성자
    // ─────────────────────────────────────────────────────────

    public ChannelViewModel(int channelIndex, Dispatcher dispatcher)
    {
        _channelIndex = channelIndex;
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

        // PropertyChanged 이벤트를 구독하여 DSP 프로바이더에 변경 사항 반영
        PropertyChanged += OnPropertyChanged;
    }

    /// <summary>
    /// OutputChannel에서 ChannelDspProvider가 생성된 후 호출하여 연결한다.
    /// </summary>
    public void AttachDspProvider(ChannelDspProvider dspProvider)
    {
        if (_dspProvider != null)
        {
            _dspProvider.MeterUpdated -= OnMeterUpdated;
        }

        _dspProvider = dspProvider;

        if (_dspProvider != null)
        {
            // 현재 ViewModel 값으로 DSP 프로바이더 초기화
            _dspProvider.VolumeDb = VolumeDb;
            _dspProvider.Pan = Pan;
            _dspProvider.IsMuted = IsMuted;

            // 미터 이벤트 구독
            _dspProvider.MeterUpdated += OnMeterUpdated;
        }
    }

    /// <summary>
    /// DSP 프로바이더 연결 해제 (채널 정지 시)
    /// </summary>
    public void DetachDspProvider()
    {
        if (_dspProvider != null)
        {
            _dspProvider.MeterUpdated -= OnMeterUpdated;
            _dspProvider = null;
        }
    }

    // ─────────────────────────────────────────────────────────
    //  Property Changed Handler (ViewModel → DSP)
    // ─────────────────────────────────────────────────────────

    private void OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_dspProvider == null) return;

        switch (e.PropertyName)
        {
            case nameof(VolumeDb):
                _dspProvider.VolumeDb = VolumeDb;
                break;
            case nameof(Pan):
                _dspProvider.Pan = Pan;
                break;
            case nameof(IsMuted):
                _dspProvider.IsMuted = IsMuted;
                break;
        }
    }

    // ─────────────────────────────────────────────────────────
    //  Meter Updated Handler (DSP → ViewModel → UI)
    // ─────────────────────────────────────────────────────────

    private void OnMeterUpdated(object? sender, MeteringEventArgs e)
    {
        // 오디오 스레드에서 호출되므로 반드시 Dispatcher를 통해 UI 스레드로 마샬링
        _dispatcher.BeginInvoke(() =>
        {
            // Peak 값을 미터 레벨로 사용 (RMS 대신)
            // UI에서 Peak Hold를 구현하려면 여기서 처리
            MeterLevelLeft = Math.Clamp(e.PeakLeft, 0.0f, 1.0f);
            MeterLevelRight = Math.Clamp(e.PeakRight, 0.0f, 1.0f);
        }, DispatcherPriority.Render);
    }

    // ─────────────────────────────────────────────────────────
    //  커맨드
    // ─────────────────────────────────────────────────────────

    /// <summary>음소거 토글 커맨드</summary>
    [RelayCommand]
    private void ToggleMute()
    {
        IsMuted = !IsMuted;
    }

    /// <summary>설정 초기화 (채널 초기화 시 호출)</summary>
    public void ResetSettings()
    {
        SelectedDeviceId = null;
        VolumeDb = 0.0f;
        Pan = 0.0f;
        IsMuted = false;
        MeterLevelLeft = 0.0f;
        MeterLevelRight = 0.0f;
        ErrorMessage = null;
    }

    // ─────────────────────────────────────────────────────────
    //  IDisposable
    // ─────────────────────────────────────────────────────────

    public void Dispose()
    {
        DetachDspProvider();
        PropertyChanged -= OnPropertyChanged;
    }
}
