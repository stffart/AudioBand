﻿using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Timers;
using AudioBand.AudioSource;
using Windows.Foundation.Metadata;
using Windows.Media;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Win10AudioSource
{
    /// <summary>
    /// The Windows 10 API AudioSource.
    /// </summary>
    public class Win10AudioSource : IAudioSource
    {
        private readonly Timer _checkTimer = new Timer(1000);
        private GlobalSystemMediaTransportControlsSessionManager _mtcManager;
        private GlobalSystemMediaTransportControlsSession _currentSession;
        private GlobalSystemMediaTransportControlsSessionMediaProperties _lastProperties;
        private bool _isPlaying = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="Win10AudioSource"/> class.
        /// </summary>
        public Win10AudioSource()
        {
            _checkTimer.Elapsed += OnTimerElapsed;
        }

        /// <inheritdoc />
        public event EventHandler<SettingChangedEventArgs> SettingChanged
        {
            add { }
            remove { }
        }

        /// <inheritdoc />
        public event EventHandler<TrackInfoChangedEventArgs> TrackInfoChanged;

        /// <inheritdoc />
        public event EventHandler<bool> IsPlayingChanged;

        /// <inheritdoc />
        public event EventHandler<TimeSpan> TrackProgressChanged;

        /// <inheritdoc />
        public event EventHandler<int> VolumeChanged
        {
            add { }
            remove { }
        }

        /// <inheritdoc />
        public event EventHandler<bool> ShuffleChanged;

        /// <inheritdoc />
        public event EventHandler<RepeatMode> RepeatModeChanged;

        /// <inheritdoc />
        public string Name => "Windows 10";

        /// <inheritdoc />
        public string Description => "Visit the documentation to see which apps are compatible.";

        /// <inheritdoc />
        public string WindowClassName => null;

        /// <inheritdoc />
        public IAudioSourceLogger Logger { get; set; }

        /// <inheritdoc />
        public async Task ActivateAsync()
        {
            _checkTimer.Start();
            if (!ApiInformation.IsApiContractPresent("Windows.Foundation.UniversalApiContract", 7))
            {
                Logger.Info("Audio source only available on windows 10 1809 and later");
                return;
            }

            _mtcManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            await UpdateSession(_mtcManager.GetCurrentSession());
        }

        /// <inheritdoc />
        public Task DeactivateAsync()
        {
            _checkTimer.Stop();
            UnsubscribeFromSession();
            _mtcManager = null;

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task PlayTrackAsync()
        {
            if (_currentSession == null)
            {
                return;
            }

            await _currentSession.TryPlayAsync();
        }

        /// <inheritdoc />
        public async Task PauseTrackAsync()
        {
            if (_currentSession == null)
            {
                return;
            }

            await _currentSession.TryPauseAsync();
        }

        /// <inheritdoc />
        public async Task PreviousTrackAsync()
        {
            if (_currentSession == null)
            {
                return;
            }

            await _currentSession.TrySkipPreviousAsync();
        }

        /// <inheritdoc />
        public async Task NextTrackAsync()
        {
            if (_currentSession == null)
            {
                return;
            }

            await _currentSession.TrySkipNextAsync();
        }

        /// <inheritdoc />
        public Task SetVolumeAsync(int newVolume)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task SetPlaybackProgressAsync(TimeSpan newProgress)
        {
            if (_currentSession == null)
            {
                return;
            }

            if (!await _currentSession.TryChangePlaybackPositionAsync((long)newProgress.Ticks))
            {
                Logger.Warn($"Failed to set playback for Win10 Audio Source.");
            }
        }

        /// <inheritdoc />
        public async Task SetShuffleAsync(bool shuffleOn)
        {
            if (_currentSession == null)
            {
                return;
            }

            await _currentSession.TryChangeShuffleActiveAsync(shuffleOn);
        }

        /// <inheritdoc />
        public async Task SetRepeatModeAsync(RepeatMode newRepeatMode)
        {
            if (_currentSession == null)
            {
                return;
            }

            await _currentSession.TryChangeAutoRepeatModeAsync(ToWindowsRepeatMode(newRepeatMode));
        }

        private void OnTimerElapsed(object sender, ElapsedEventArgs e)
        {
            var session = _mtcManager.GetCurrentSession();

            UpdateSession(session).GetAwaiter().GetResult();
        }

        private void CurrentSessionOnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
        {
            UpdateTimelineProperties();
        }

        private void CurrentSessionOnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
        {
            UpdatePlaybackProperties();
        }

        private async void CurrentSessionOnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
        {
            await UpdateMediaProperties();
        }

        private async Task UpdateSession(GlobalSystemMediaTransportControlsSession newSession)
        {
            UnsubscribeFromSession();
            _currentSession = newSession;

            await UpdateMediaProperties();
            UpdateTimelineProperties();
            UpdatePlaybackProperties();
            SubscribeToSession();
        }

        private void UnsubscribeFromSession()
        {
            if (_currentSession == null)
            {
                return;
            }

            _currentSession.MediaPropertiesChanged -= CurrentSessionOnMediaPropertiesChanged;
            _currentSession.PlaybackInfoChanged -= CurrentSessionOnPlaybackInfoChanged;
            _currentSession.TimelinePropertiesChanged -= CurrentSessionOnTimelinePropertiesChanged;
        }

        private void SubscribeToSession()
        {
            if (_currentSession == null)
            {
                return;
            }

            _currentSession.MediaPropertiesChanged += CurrentSessionOnMediaPropertiesChanged;
            _currentSession.PlaybackInfoChanged += CurrentSessionOnPlaybackInfoChanged;
            _currentSession.TimelinePropertiesChanged += CurrentSessionOnTimelinePropertiesChanged;
        }

        private async Task UpdateMediaProperties()
        {
            if (_currentSession == null)
            {
                return;
            }

            var mediaProperties = await _currentSession.TryGetMediaPropertiesAsync();

            if (mediaProperties.Title == _lastProperties?.Title && mediaProperties.Artist == _lastProperties?.Artist)
            {
                return;
            }

            _lastProperties = mediaProperties;
            var albumArt = await GetAlbumArt(mediaProperties.Thumbnail);

            TrackInfoChanged?.Invoke(this, new TrackInfoChangedEventArgs
            {
                Album = mediaProperties.AlbumTitle,
                AlbumArt = albumArt,
                Artist = mediaProperties.Artist,
                TrackName = mediaProperties.Title,
                TrackLength = _currentSession.GetTimelineProperties().EndTime,
            });
        }

        private void UpdateTimelineProperties()
        {
            if (_currentSession == null)
            {
                return;
            }

            var timelineProperties = _currentSession.GetTimelineProperties();
            TrackProgressChanged?.Invoke(this, timelineProperties.Position);
        }

        private void UpdatePlaybackProperties()
        {
            if (_currentSession == null)
            {
                ClearState();
                return;
            }

            var playbackInfo = _currentSession.GetPlaybackInfo();

            // We'll just make every other state count as paused.
            var isPlaying = playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            if (_isPlaying != isPlaying)
            {
                _isPlaying = isPlaying;
                IsPlayingChanged?.Invoke(this, _isPlaying);
            }

            ShuffleChanged?.Invoke(this, playbackInfo.IsShuffleActive.GetValueOrDefault());
            RepeatModeChanged?.Invoke(this, ToAudioBandRepeatMode(playbackInfo.AutoRepeatMode.GetValueOrDefault()));
        }

        private RepeatMode ToAudioBandRepeatMode(MediaPlaybackAutoRepeatMode repeatMode)
        {
            switch (repeatMode)
            {
                case MediaPlaybackAutoRepeatMode.List:
                    return RepeatMode.RepeatContext;
                case MediaPlaybackAutoRepeatMode.None:
                    return RepeatMode.Off;
                case MediaPlaybackAutoRepeatMode.Track:
                    return RepeatMode.RepeatTrack;
            }

            return RepeatMode.Off;
        }

        private MediaPlaybackAutoRepeatMode ToWindowsRepeatMode(RepeatMode repeatMode)
        {
            switch (repeatMode)
            {
                case RepeatMode.Off:
                    return MediaPlaybackAutoRepeatMode.None;
                case RepeatMode.RepeatContext:
                    return MediaPlaybackAutoRepeatMode.List;
                case RepeatMode.RepeatTrack:
                    return MediaPlaybackAutoRepeatMode.Track;
            }

            return MediaPlaybackAutoRepeatMode.None;
        }

        private async Task<Image> GetAlbumArt(IRandomAccessStreamReference stream)
        {
            if (stream == null)
            {
                return null;
            }

            try
            {
                var read = await stream.OpenReadAsync();
                using (var netStream = read.AsStreamForRead())
                {
                    return Image.FromStream(netStream);
                }
            }
            catch (Exception e)
            {
                Logger.Error(e);
                return null;
            }
        }

        private void ClearState()
        {
            TrackInfoChanged?.Invoke(this, new TrackInfoChangedEventArgs());
            IsPlayingChanged?.Invoke(this, false);
            TrackProgressChanged?.Invoke(this, TimeSpan.Zero);
            ShuffleChanged?.Invoke(this, false);
            RepeatModeChanged?.Invoke(this, RepeatMode.Off);
        }
    }
}
