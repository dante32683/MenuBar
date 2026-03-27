using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
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
        private ImageSource _cachedAlbumCover;

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

            if (_currentSession != null)
            {
                _currentSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged += OnPlaybackInfoChanged;
                _currentSession.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
            }
        }

        private void OnMediaPropertiesChanged(
            GlobalSystemMediaTransportControlsSession sender,
            MediaPropertiesChangedEventArgs args)
        {
            _dispatcher.TryEnqueue(() => _ = RefreshAsync(full: true));
        }

        private void OnPlaybackInfoChanged(
            GlobalSystemMediaTransportControlsSession sender,
            PlaybackInfoChangedEventArgs args)
        {
            _dispatcher.TryEnqueue(() => _ = RefreshAsync(full: false));
        }

        private void OnTimelinePropertiesChanged(
            GlobalSystemMediaTransportControlsSession sender,
            TimelinePropertiesChangedEventArgs args)
        {
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
                }

                CurrentState = state;
            }
            catch { }

            StateChanged?.Invoke(CurrentState);
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
            AttachSession(null);
            if (_manager != null)
            {
                _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
                _manager.SessionsChanged -= OnSessionsChanged;
            }
        }
    }
}
