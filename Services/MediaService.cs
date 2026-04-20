using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Media.Control;

namespace MenuBar.Services
{
    public sealed class MediaService : IDisposable
    {
        public class MediaState
        {
            public string Title { get; set; } = string.Empty;
            public string Artist { get; set; } = string.Empty;
            public string SourceApp { get; set; } = string.Empty;
            public ImageSource SourceAppIcon { get; set; }
            public bool Playing { get; set; }
            public ImageSource AlbumCover { get; set; }
            public bool? IsShuffleActive { get; set; }
            public Windows.Media.MediaPlaybackAutoRepeatMode? RepeatMode { get; set; }
            public TimeSpan Position { get; set; }
            public TimeSpan EndTime { get; set; }
            public DateTimeOffset LastUpdatedTime { get; set; }

            public bool HasContent =>
                !string.IsNullOrWhiteSpace(Title) || !string.IsNullOrWhiteSpace(Artist);

            public static MediaState Empty => new();
        }

        private GlobalSystemMediaTransportControlsSessionManager _manager;
        private GlobalSystemMediaTransportControlsSession _currentSession;
        private readonly DispatcherQueue _dispatcher;
        private readonly DispatcherQueueTimer _progressTimer;

        private string _lastTrackId = string.Empty;
        private string _lastPlayingAppId = string.Empty;
        private string _lastSourceAppId = string.Empty;
        private ImageSource _cachedAlbumCover;
        private ImageSource _cachedSourceAppIcon;

        public event Action<MediaState> StateChanged;
        public MediaState CurrentState { get; private set; } = MediaState.Empty;
        public bool SuppressUpdates { get; set; }

        public MediaService(DispatcherQueue dispatcher)
        {
            _dispatcher = dispatcher;
            _progressTimer = _dispatcher.CreateTimer();
            _progressTimer.Interval = TimeSpan.FromMilliseconds(500);
            _progressTimer.Tick += (_, _) => _ = RefreshAsync(full: false);
        }

        public void SetHighFrequencyUpdate(bool enabled)
        {
            _progressTimer.Interval = enabled ? TimeSpan.FromMilliseconds(100) : TimeSpan.FromMilliseconds(500);
        }

        public async Task InitializeAsync()
        {
            try
            {
                _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                _manager.CurrentSessionChanged += OnCurrentSessionChanged;
                _manager.SessionsChanged += OnSessionsChanged;
                
                UpdateSessionSelection();
            }
            catch
            {
                _dispatcher.TryEnqueue(() =>
                {
                    CurrentState = MediaState.Empty;
                    StateChanged?.Invoke(CurrentState);
                });
            }
        }

        private GlobalSystemMediaTransportControlsSession FindBestSession()
        {
            try
            {
                var sessions = _manager.GetSessions();
                if (sessions == null || sessions.Count == 0) return null;

                // 1. Any session actually playing?
                // We prioritize sessions with a non-zero timeline (actual media) over others (like meetings)
                var playingSessions = sessions
                    .Where(s => s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    .OrderByDescending(s => s.GetTimelineProperties()?.EndTime.Ticks > 0)
                    .ToList();

                if (playingSessions.Any()) return playingSessions.First();

                // 2. If nothing is playing, prefer the last app that was playing (Stickiness)
                if (!string.IsNullOrEmpty(_lastPlayingAppId))
                {
                    var sticky = sessions.FirstOrDefault(s => s.SourceAppUserModelId == _lastPlayingAppId);
                    if (sticky != null) return sticky;
                }

                // 3. Fallback to what the OS thinks is current
                return _manager.GetCurrentSession();
            }
            catch
            {
                return _manager.GetCurrentSession();
            }
        }

        private void UpdateSessionSelection()
        {
            _dispatcher.TryEnqueue(() =>
            {
                var best = FindBestSession();
                if (best?.SourceAppUserModelId != _currentSession?.SourceAppUserModelId)
                {
                    AttachSession(best);
                    _ = RefreshAsync(full: true);
                }
                else if (best == null && _currentSession != null)
                {
                    AttachSession(null);
                    _ = RefreshAsync(full: true);
                }
            });
        }

        private void OnCurrentSessionChanged(
            GlobalSystemMediaTransportControlsSessionManager sender,
            CurrentSessionChangedEventArgs args)
        {
            UpdateSessionSelection();
        }

        private void OnSessionsChanged(
            GlobalSystemMediaTransportControlsSessionManager sender,
            SessionsChangedEventArgs args)
        {
            UpdateSessionSelection();
        }

        private void AttachSession(GlobalSystemMediaTransportControlsSession session)
        {
            if (_currentSession != null)
            {
                _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                _currentSession.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
            }

            _currentSession = session;
            _lastTrackId = string.Empty;
            _cachedAlbumCover = null;
            _lastSourceAppId = string.Empty;
            _cachedSourceAppIcon = null;

            if (_currentSession != null)
            {
                _currentSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged += OnPlaybackInfoChanged;
                _currentSession.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
            }
        }

        private DateTime _lastEventTime = DateTime.MinValue;
        private const int EventThrottleMs = 250;

        private void OnMediaPropertiesChanged(
            GlobalSystemMediaTransportControlsSession sender,
            MediaPropertiesChangedEventArgs args)
        {
            if ((DateTime.Now - _lastEventTime).TotalMilliseconds < EventThrottleMs) return;
            _lastEventTime = DateTime.Now;
            _dispatcher.TryEnqueue(() => _ = RefreshAsync(full: true));
        }

        private void OnPlaybackInfoChanged(
            GlobalSystemMediaTransportControlsSession sender,
            PlaybackInfoChangedEventArgs args)
        {
            if ((DateTime.Now - _lastEventTime).TotalMilliseconds < EventThrottleMs) return;
            _lastEventTime = DateTime.Now;
            _dispatcher.TryEnqueue(() => _ = RefreshAsync(full: false));
        }

        private void OnTimelinePropertiesChanged(
            GlobalSystemMediaTransportControlsSession sender,
            TimelinePropertiesChangedEventArgs args)
        {
            if ((DateTime.Now - _lastEventTime).TotalMilliseconds < EventThrottleMs) return;
            _lastEventTime = DateTime.Now;
            _dispatcher.TryEnqueue(() => _ = RefreshAsync(full: false));
        }

        public async Task RefreshAsync(bool full)
        {
            if (_currentSession == null)
            {
                CurrentState = MediaState.Empty;
                StateChanged?.Invoke(CurrentState);
                if (_progressTimer.IsRunning) _progressTimer.Stop();
                return;
            }

            if (SuppressUpdates) return;

            try
            {
                var playback = _currentSession.GetPlaybackInfo();
                var timeline = _currentSession.GetTimelineProperties();
                
                // Manage timer based on playback status
                bool isPlaying = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                if (isPlaying)
                {
                    _lastPlayingAppId = _currentSession.SourceAppUserModelId;
                }

                if (isPlaying && !_progressTimer.IsRunning) _progressTimer.Start();
                else if (!isPlaying && _progressTimer.IsRunning) _progressTimer.Stop();

                var state = new MediaState
                {
                    SourceApp = FormatSourceApp(_currentSession.SourceAppUserModelId),
                    SourceAppIcon = CurrentState.SourceAppIcon,
                    Playing = isPlaying,
                    IsShuffleActive = playback?.IsShuffleActive,
                    RepeatMode = playback?.AutoRepeatMode,
                    Position = timeline?.Position ?? TimeSpan.Zero,
                    EndTime = timeline?.EndTime ?? TimeSpan.Zero,
                    LastUpdatedTime = timeline?.LastUpdatedTime ?? DateTimeOffset.Now
                };

                if (full || string.IsNullOrEmpty(_lastTrackId))
                {
                    var props = await _currentSession.TryGetMediaPropertiesAsync();
                    state.Title = props?.Title ?? string.Empty;
                    state.Artist = props?.Artist ?? string.Empty;
                    state.SourceAppIcon = ResolveSourceAppIcon(_currentSession.SourceAppUserModelId);

                    string trackId = $"{state.SourceApp}:{state.Title}:{state.Artist}";
                    if (trackId != _lastTrackId)
                    {
                        _lastTrackId = trackId;
                        if (props?.Thumbnail != null)
                        {
                            try
                            {
                                var stream = await props.Thumbnail.OpenReadAsync();
                                if (stream != null)
                                {
                                    var bitmap = new BitmapImage();
                                    await bitmap.SetSourceAsync(stream);
                                    _cachedAlbumCover = bitmap;
                                }
                            }
                            catch { _cachedAlbumCover = null; }
                        }
                        else { _cachedAlbumCover = null; }
                    }
                    
                    state.AlbumCover = _cachedAlbumCover;
                }
                else
                {
                    state.Title = CurrentState.Title;
                    state.Artist = CurrentState.Artist;
                    state.AlbumCover = _cachedAlbumCover;
                    state.SourceAppIcon = CurrentState.SourceAppIcon;
                }

                CurrentState = state;
            }
            catch { }

            try { StateChanged?.Invoke(CurrentState); } catch { }
        }

        private static string FormatSourceApp(string aumid)
        {
            if (string.IsNullOrWhiteSpace(aumid)) return string.Empty;

            string name = aumid;
            if (name.Contains("!", StringComparison.Ordinal))
                name = name.Split('!')[0];
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                name = name[..^4];
            if (name.Length > 0 && char.IsLower(name[0]))
                name = char.ToUpper(name[0]) + name[1..];

            return name switch
            {
                "Spotify" => "Spotify",
                "Chrome" => "Google Chrome",
                "Msedge" => "Microsoft Edge",
                "Music.UI" => "Media Player",
                _ => name
            };
        }

        private ImageSource ResolveSourceAppIcon(string aumid)
        {
            if (string.IsNullOrWhiteSpace(aumid))
            {
                _lastSourceAppId = string.Empty;
                _cachedSourceAppIcon = null;
                return null;
            }

            if (string.Equals(_lastSourceAppId, aumid, StringComparison.OrdinalIgnoreCase))
            {
                return _cachedSourceAppIcon;
            }

            _lastSourceAppId = aumid;
            _cachedSourceAppIcon = TryGetSourceAppIcon(aumid);
            return _cachedSourceAppIcon;
        }

        private static ImageSource TryGetSourceAppIcon(string aumid)
        {
            string processName = GetProcessNameFromAumid(aumid);
            if (string.IsNullOrWhiteSpace(processName))
            {
                return null;
            }

            try
            {
                foreach (var process in Process.GetProcessesByName(processName))
                {
                    try
                    {
                        IntPtr hwnd = process.MainWindowHandle;
                        if (hwnd == IntPtr.Zero)
                        {
                            continue;
                        }

                        ImageSource icon = GetWindowIconBitmap(hwnd);
                        if (icon != null)
                        {
                            return icon;
                        }
                    }
                    catch
                    {
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static string GetProcessNameFromAumid(string aumid)
        {
            if (string.IsNullOrWhiteSpace(aumid))
            {
                return string.Empty;
            }

            string token = aumid;
            if (token.Contains('!'))
            {
                token = token.Split('!')[0];
            }

            int exeIndex = token.LastIndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeIndex >= 0)
            {
                token = token[..(exeIndex + 4)];
            }

            string fileName = Path.GetFileNameWithoutExtension(token);
            return fileName;
        }

        private static ImageSource GetWindowIconBitmap(IntPtr hwnd)
        {
            IntPtr hIcon = NativeMethods.SendMessage(hwnd, NativeMethods.WM_GETICON, NativeMethods.ICON_SMALL2, 0);
            if (hIcon == IntPtr.Zero)
                hIcon = NativeMethods.SendMessage(hwnd, NativeMethods.WM_GETICON, NativeMethods.ICON_SMALL, 0);
            if (hIcon == IntPtr.Zero)
                hIcon = NativeMethods.SendMessage(hwnd, NativeMethods.WM_GETICON, NativeMethods.ICON_BIG, 0);
            if (hIcon == IntPtr.Zero)
                hIcon = NativeMethods.GetClassLongPtr(hwnd, NativeMethods.GCLP_HICONSM);
            if (hIcon == IntPtr.Zero)
                hIcon = NativeMethods.GetClassLongPtr(hwnd, NativeMethods.GCLP_HICON);
            if (hIcon == IntPtr.Zero)
                return null;

            return HIconToWriteableBitmap(hIcon);
        }

        private static WriteableBitmap HIconToWriteableBitmap(IntPtr hIcon)
        {
            int size = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSMICON);
            if (size <= 0) size = 16;

            var bmi = new NativeMethods.BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                biWidth = size,
                biHeight = -size,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0
            };

            IntPtr hdc = NativeMethods.CreateCompatibleDC(IntPtr.Zero);
            if (hdc == IntPtr.Zero) return null;

            IntPtr hBitmap = NativeMethods.CreateDIBSection(
                hdc, ref bmi, NativeMethods.DIB_RGB_COLORS,
                out IntPtr ppvBits, IntPtr.Zero, 0);
            if (hBitmap == IntPtr.Zero)
            {
                NativeMethods.DeleteDC(hdc);
                return null;
            }

            IntPtr oldBitmap = NativeMethods.SelectObject(hdc, hBitmap);
            NativeMethods.DrawIconEx(hdc, 0, 0, hIcon, size, size, 0, IntPtr.Zero, NativeMethods.DI_NORMAL);
            NativeMethods.SelectObject(hdc, oldBitmap);

            byte[] pixels = new byte[size * size * 4];
            Marshal.Copy(ppvBits, pixels, 0, pixels.Length);

            NativeMethods.DeleteObject(hBitmap);
            NativeMethods.DeleteDC(hdc);

            bool hasAlpha = false;
            for (int i = 3; i < pixels.Length; i += 4)
            {
                if (pixels[i] != 0) { hasAlpha = true; break; }
            }

            if (!hasAlpha)
            {
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    if (pixels[i] != 0 || pixels[i + 1] != 0 || pixels[i + 2] != 0)
                        pixels[i + 3] = 255;
                }
            }

            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte a = pixels[i + 3];
                if (a == 0 || a == 255) continue;
                pixels[i] = (byte)(pixels[i] * a / 255);
                pixels[i + 1] = (byte)(pixels[i + 1] * a / 255);
                pixels[i + 2] = (byte)(pixels[i + 2] * a / 255);
            }

            var wb = new WriteableBitmap(size, size);
            using (var stream = wb.PixelBuffer.AsStream())
                stream.Write(pixels, 0, pixels.Length);
            wb.Invalidate();
            return wb;
        }

        public async Task SendPlayPauseAsync()
        {
            if (_currentSession != null)
            {
                try
                {
                    await _currentSession.TryTogglePlayPauseAsync();
                }
                catch
                {
                }
            }
        }

        public async Task SendPreviousAsync()
        {
            if (_currentSession != null)
            {
                try
                {
                    await _currentSession.TrySkipPreviousAsync();
                }
                catch
                {
                }
            }
        }

        public async Task SendNextAsync()
        {
            if (_currentSession != null)
            {
                try
                {
                    await _currentSession.TrySkipNextAsync();
                }
                catch
                {
                }
            }
        }

        public async Task ToggleShuffleAsync()
        {
            if (_currentSession != null)
            {
                try
                {
                    var info = _currentSession.GetPlaybackInfo();
                    if (info?.IsShuffleActive != null)
                    {
                        await _currentSession.TryChangeShuffleActiveAsync(!info.IsShuffleActive.Value);
                    }
                }
                catch { }
            }
        }

        public async Task ToggleRepeatAsync()
        {
            if (_currentSession != null)
            {
                try
                {
                    var info = _currentSession.GetPlaybackInfo();
                    if (info?.AutoRepeatMode != null)
                    {
                        var current = info.AutoRepeatMode.Value;
                        var next = current switch
                        {
                            Windows.Media.MediaPlaybackAutoRepeatMode.None =>
                                Windows.Media.MediaPlaybackAutoRepeatMode.List,
                            Windows.Media.MediaPlaybackAutoRepeatMode.List =>
                                Windows.Media.MediaPlaybackAutoRepeatMode.Track,
                            Windows.Media.MediaPlaybackAutoRepeatMode.Track =>
                                Windows.Media.MediaPlaybackAutoRepeatMode.None,
                            _ => Windows.Media.MediaPlaybackAutoRepeatMode.None
                        };
                        await _currentSession.TryChangeAutoRepeatModeAsync(next);
                    }
                }
                catch { }
            }
        }

        public async Task SeekAsync(TimeSpan position)
        {
            if (_currentSession == null) return;
            try
            {
                var playback = _currentSession.GetPlaybackInfo();
                if (playback?.Controls?.IsPlaybackPositionEnabled == true)
                    await _currentSession.TryChangePlaybackPositionAsync(position.Ticks);
            }
            catch { }
        }

        public void Dispose()
        {
            // Stop the timer first so no more Tick callbacks fire after disposal.
            _progressTimer.Stop();
            // Clear subscribers before detaching the session so no post-close UI callbacks occur.
            StateChanged = null;
            AttachSession(null);
            if (_manager != null)
            {
                _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
                _manager.SessionsChanged -= OnSessionsChanged;
            }
        }
    }
}
