using System;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
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
            public bool Playing { get; set; }
            public ImageSource AlbumCover { get; set; }

            public bool HasContent =>
                !string.IsNullOrWhiteSpace(Title) || !string.IsNullOrWhiteSpace(Artist);

            public static MediaState Empty => new();
        }

        private GlobalSystemMediaTransportControlsSessionManager _manager;
        private GlobalSystemMediaTransportControlsSession _currentSession;
        private readonly DispatcherQueue _dispatcher;

        public event Action<MediaState> StateChanged;
        public MediaState CurrentState { get; private set; } = MediaState.Empty;

        public MediaService(DispatcherQueue dispatcher)
        {
            _dispatcher = dispatcher;
        }

        public async Task InitializeAsync()
        {
            try
            {
                _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                _manager.CurrentSessionChanged += OnCurrentSessionChanged;
                AttachSession(_manager.GetCurrentSession());
                await RefreshAsync();
            }
            catch
            {
                CurrentState = MediaState.Empty;
                StateChanged?.Invoke(CurrentState);
            }
        }

        private void OnCurrentSessionChanged(
            GlobalSystemMediaTransportControlsSessionManager sender,
            CurrentSessionChangedEventArgs args)
        {
            _dispatcher.TryEnqueue(() =>
            {
                AttachSession(sender.GetCurrentSession());
                _ = RefreshAsync();
            });
        }

        private void AttachSession(GlobalSystemMediaTransportControlsSession session)
        {
            if (_currentSession != null)
            {
                _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            }

            _currentSession = session;

            if (_currentSession != null)
            {
                _currentSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged += OnPlaybackInfoChanged;
            }
        }

        private void OnMediaPropertiesChanged(
            GlobalSystemMediaTransportControlsSession sender,
            MediaPropertiesChangedEventArgs args)
        {
            _dispatcher.TryEnqueue(() => _ = RefreshAsync());
        }

        private void OnPlaybackInfoChanged(
            GlobalSystemMediaTransportControlsSession sender,
            PlaybackInfoChangedEventArgs args)
        {
            _dispatcher.TryEnqueue(() => _ = RefreshAsync());
        }

        public async Task RefreshAsync()
        {
            if (_currentSession == null)
            {
                CurrentState = MediaState.Empty;
                StateChanged?.Invoke(CurrentState);
                return;
            }

            try
            {
                var props = await _currentSession.TryGetMediaPropertiesAsync();
                var playback = _currentSession.GetPlaybackInfo();
                var state = new MediaState
                {
                    Title = props?.Title ?? string.Empty,
                    Artist = props?.Artist ?? string.Empty,
                    Playing = playback.PlaybackStatus ==
                              GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
                };

                if (props?.Thumbnail != null)
                {
                    try
                    {
                        var stream = await props.Thumbnail.OpenReadAsync();
                        if (stream != null)
                        {
                            var bitmap = new BitmapImage();
                            await bitmap.SetSourceAsync(stream);
                            state.AlbumCover = bitmap;
                        }
                    }
                    catch
                    {
                    }
                }

                CurrentState = state;
            }
            catch
            {
                CurrentState = MediaState.Empty;
            }

            StateChanged?.Invoke(CurrentState);
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

        public void Dispose()
        {
            AttachSession(null);
            if (_manager != null)
            {
                _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
            }
        }
    }
}
