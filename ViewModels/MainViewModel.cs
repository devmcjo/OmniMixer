using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NAudio.CoreAudioApi;
using OmniMixer.Audio;
using OmniMixer.Models;

namespace OmniMixer.ViewModels;

/// <summary>
/// OmniMixer의 최상위 ViewModel.
/// AudioEngine의 유일한 소유자이며, 8개 ChannelViewModel을 관리한다.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    // ─────────────────────────────────────────────────────────
    //  낮부 컴포넌트 (AudioEngine은 이 클래스만 소유)
    // ─────────────────────────────────────────────────────────

    private readonly AudioEngine _audioEngine;
    private readonly Dispatcher _dispatcher;

    // ─────────────────────────────────────────────────────────
    //  Observable Properties
    // ─────────────────────────────────────────────────────────

    /// <summary>현재 오디오 엔진이 실행 중인지 여부</summary>
    [ObservableProperty]
    private bool _isRunning = false;

    /// <summary>선택된 입력 장치 (캡처 장치)</summary>
    [ObservableProperty]
    private AudioDeviceItem? _selectedInputDevice;

    /// <summary>Start/Stop 전환 중인지 여부 (중복 실행 방지)</summary>
    [ObservableProperty]
    private bool _isTransitioning = false;

    /// <summary>입력 장치 목록</summary>
    public ObservableCollection<AudioDeviceItem> InputDevices { get; } = new();

    /// <summary>출력 장치 목록</summary>
    public ObservableCollection<AudioDeviceItem> OutputDevices { get; } = new();

    /// <summary>8개 채널의 ViewModel</summary>
    public ChannelViewModel[] Channels { get; }

    /// <summary>상태/오류 메시지</summary>
    [ObservableProperty]
    private string _statusMessage = "준비";

    /// <summary>Start/Stop 버튼 텍스트</summary>
    public string StartStopButtonText => IsRunning ? "■ STOP" : "▶ START";

    // ─────────────────────────────────────────────────────────
    //  생성자
    // ─────────────────────────────────────────────────────────

    public MainViewModel()
    {
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _audioEngine = new AudioEngine();
        _audioEngine.EngineError += OnEngineError;

        // 장치 목록 먼저 로드 (OutputDevices 채우기)
        LoadDevices();

        // 8개 채널 ViewModel 초기화 (OutputDevices가 채워진 후)
        Channels = new ChannelViewModel[AudioEngine.MaxChannels];
        for (int i = 0; i < AudioEngine.MaxChannels; i++)
        {
            Channels[i] = new ChannelViewModel(i, _dispatcher)
            {
                OutputDevices = OutputDevices
            };
        }

        // PropertyChanged 이벤트 구독 (StartStopButtonText 갱신용)
        PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(IsRunning))
            {
                OnPropertyChanged(nameof(StartStopButtonText));
            }
        };
    }

    // ─────────────────────────────────────────────────────────
    //  Partial Methods (CommunityToolkit.Mvvm)
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// P1 Fix: SelectedInputDevice 변경 시 StartStopCommand의 CanExecute 자동 갱신
    /// </summary>
    partial void OnSelectedInputDeviceChanged(AudioDeviceItem? value)
    {
        StartStopCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// P3 Fix: IsTransitioning 변경 시에도 CanExecute 갱신 (버튼 비활성화)
    /// </summary>
    partial void OnIsTransitioningChanged(bool value)
    {
        StartStopCommand.NotifyCanExecuteChanged();
    }

    // ─────────────────────────────────────────────────────────
    //  장치 목록 관리
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 시스템의 오디오 장치 목록을 로드한다.
    /// </summary>
    private void LoadDevices()
    {
        try
        {
            // 입력 장치 로드
            InputDevices.Clear();
            var captureDevices = AudioEngine.GetCaptureDevices();
            foreach (var device in captureDevices)
            {
                InputDevices.Add(AudioDeviceItem.FromMMDevice(device));
            }

            // VB-Cable 자동 선택 (있는 경우)
            var cableDevice = InputDevices.FirstOrDefault(d =>
                d.FriendlyName.Contains("CABLE", StringComparison.OrdinalIgnoreCase) ||
                d.FriendlyName.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase));
            if (cableDevice != null)
            {
                SelectedInputDevice = cableDevice;
            }
            else if (InputDevices.Count > 0)
            {
                SelectedInputDevice = InputDevices[0];
            }

            // 출력 장치 로드
            OutputDevices.Clear();
            var outputDevices = AudioEngine.GetOutputDevices();
            foreach (var device in outputDevices)
            {
                OutputDevices.Add(AudioDeviceItem.FromMMDevice(device));
            }

            StatusMessage = $"장치 로드 완료: 입력 {InputDevices.Count}개, 출력 {OutputDevices.Count}개";
        }
        catch (Exception ex)
        {
            StatusMessage = $"장치 로드 실패: {ex.Message}";
        }
    }

    /// <summary>
    /// 장치 목록 새로고침 커맨드
    /// </summary>
    [RelayCommand]
    private void RefreshDevices()
    {
        LoadDevices();
    }

    // ─────────────────────────────────────────────────────────
    //  Start / Stop 커맨드
    // ─────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanStartStop))]
    private void StartStop()
    {
        if (IsRunning)
        {
            Stop();
        }
        else
        {
            Start();
        }
    }

    private bool CanStartStop()
    {
        // P1 Fix: 입력 장치 선택 + 전환 중 아님
        return SelectedInputDevice != null && !IsTransitioning;
    }

    private void Start()
    {
        if (SelectedInputDevice == null || IsTransitioning)
        {
            StatusMessage = "입력 장치를 선택하세요.";
            return;
        }

        // P3 Fix: 전환 상태 설정
        IsTransitioning = true;

        try
        {
            // 선택된 입력 장치의 MMDevice 찾기
            var captureDevices = AudioEngine.GetCaptureDevices();
            var selectedCaptureDevice = captureDevices.FirstOrDefault(d => d.ID == SelectedInputDevice.Id);

            if (selectedCaptureDevice == null)
            {
                StatusMessage = "선택된 입력 장치를 찾을 수 없습니다.";
                IsTransitioning = false;
                return;
            }

            // 채널 설정 수집
            var channelSettings = new List<ChannelSettings>();
            for (int i = 0; i < AudioEngine.MaxChannels; i++)
            {
                var channelVm = Channels[i];
                channelSettings.Add(new ChannelSettings
                {
                    ChannelIndex = i,
                    DeviceId = channelVm.SelectedDeviceId,
                    VolumeDb = channelVm.VolumeDb,
                    Pan = channelVm.Pan,
                    IsMuted = channelVm.IsMuted
                });

                // 채널 활성화 상태 설정
                channelVm.IsActive = !string.IsNullOrEmpty(channelVm.SelectedDeviceId);
            }

            // AudioEngine 시작
            _audioEngine.Start(selectedCaptureDevice, channelSettings);

            // DSP 프로바이더 연결
            for (int i = 0; i < AudioEngine.MaxChannels; i++)
            {
                var dspProvider = _audioEngine.Channels[i].DspProvider;
                if (dspProvider != null)
                {
                    Channels[i].AttachDspProvider(dspProvider);
                }
            }

            IsRunning = true;
            StatusMessage = "오디오 엔진 실행 중";
        }
        catch (Exception ex)
        {
            StatusMessage = $"시작 실패: {ex.Message}";
            Stop();
        }
        finally
        {
            // P3 Fix: 전환 상태 해제
            IsTransitioning = false;
        }
    }

    private void Stop()
    {
        // P3 Fix: 이미 전환 중이면 중복 실행 방지
        if (IsTransitioning && !IsRunning) return;

        IsTransitioning = true;

        try
        {
            // DSP 프로바이더 연결 해제
            foreach (var channelVm in Channels)
            {
                channelVm.DetachDspProvider();
                channelVm.IsActive = false;
            }

            _audioEngine.Stop();
            IsRunning = false;
            StatusMessage = "정지됨";
        }
        catch (Exception ex)
        {
            StatusMessage = $"정지 중 오류: {ex.Message}";
        }
        finally
        {
            // P3 Fix: 전환 상태 해제
            IsTransitioning = false;
        }
    }

    // ─────────────────────────────────────────────────────────
    //  오류 처리
    // ─────────────────────────────────────────────────────────

    private void OnEngineError(object? sender, string errorMessage)
    {
        _dispatcher.BeginInvoke(() =>
        {
            StatusMessage = errorMessage;
        }, DispatcherPriority.Normal);
    }

    // ─────────────────────────────────────────────────────────
    //  IDisposable
    // ─────────────────────────────────────────────────────────

    public void Dispose()
    {
        Stop();
        _audioEngine.EngineError -= OnEngineError;
        _audioEngine.Dispose();

        foreach (var channel in Channels)
        {
            channel.Dispose();
        }
    }
}
