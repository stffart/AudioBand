using System;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Timers;
using AudioBand.AudioSource;
using MpcCore;
using Image = System.Drawing.Image;
using Timer = System.Timers.Timer;

namespace MPDAudioSource
{
    /// <summary>
    /// Audio source for Music Player Daemon
    /// </summary>
    public class MPDAudioSource : IAudioSource
    {
        private Timer _checkMPDTimer = new Timer(1000);
        private MpcCore.MpcCoreConnection _connection;
        private MpcCore.MpcCoreClient _mpdClient;
        private int _currentItemId;
        private string _currentTrackName;
        private bool _currentIsPlaying = false;
        private int _currentProgress;
        private int _currentVolumePercent;
        private bool _currentShuffle;
        private string _currentRepeat;
        private string _port;
        private string _host;
        private int _pollingInterval = 1000;
        private bool _isActive;

        /// <summary>
        /// Initializes a new instance of the <see cref="MPDAudioSource"/> class.
        /// </summary>
        public MPDAudioSource()
        {
            _checkMPDTimer.AutoReset = false;
            _checkMPDTimer.Elapsed += CheckMPDTimerOnElapsed;

        }

        /// <inheritdoc />
        public event EventHandler<TrackInfoChangedEventArgs> TrackInfoChanged;

        /// <inheritdoc />
        public event EventHandler<TimeSpan> TrackProgressChanged;

        /// <inheritdoc />
        public event EventHandler<SettingChangedEventArgs> SettingChanged;

        /// <inheritdoc />
        public event EventHandler<int> VolumeChanged;

        /// <inheritdoc />
        public event EventHandler<bool> ShuffleChanged;

        /// <inheritdoc />
        public event EventHandler<RepeatMode> RepeatModeChanged;

        /// <inheritdoc />
        public event EventHandler<bool> IsPlayingChanged;

        /// <inheritdoc />
        public string Name => "MPD";

        /// <inheritdoc />
        public string Description => "Music Player Daemon client";

        /// <inheritdoc />
        public string WindowClassName => null;

        /// <inheritdoc />
        public IAudioSourceLogger Logger { get; set; }

        /// <summary>
        /// Gets or sets the mpd host.
        /// </summary>
        [AudioSourceSetting("MPD Host")]
        public string Host
        {
            get => _host;
            set
            {
                if (value == _host)
                {
                    return;
                }

                _host = value;
                Connect();
            }
        }

        /// <summary>
        /// Gets or sets mpd port
        /// </summary>
        [AudioSourceSetting("MPD Port")]
        public string Port
        {
            get => _port;
            set
            {
                if (value == _port)
                {
                    return;
                }


                _port = value;
                Connect();
            }
        }




        /// <inheritdoc />
        public async Task ActivateAsync()
        {
            _isActive = true;

            Connect();
            await UpdatePlayer();

            _checkMPDTimer.Start();
        }

        /// <inheritdoc />
        public Task DeactivateAsync()
        {
            _isActive = false;

            _checkMPDTimer.Stop();
            _currentItemId = -1;
            _currentTrackName = null;
            Logger.Debug("MPD has been deactivated.");

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task PlayTrackAsync()
        {
            // Play track
            try
            {
                bool result = (await _mpdClient.SendAsync(new MpcCore.Commands.Player.Play())).Result;
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }

            await Task.Delay(110).ContinueWith(async t => await UpdatePlayer());
        }

        /// <inheritdoc />
        public async Task PauseTrackAsync()
        {
            try
            {
                bool result = (await _mpdClient.SendAsync(new MpcCore.Commands.Player.Pause())).Result;
            }
            catch (System.Exception e)
            {
                Logger.Error(e);
            }

            await Task.Delay(110).ContinueWith(async t => await UpdatePlayer());
        }

        /// <inheritdoc />
        public async Task PreviousTrackAsync()
        {
            try
            {
                bool result = (await _mpdClient.SendAsync(new MpcCore.Commands.Player.Previous())).Result;
            }
            catch (System.Exception e)
            {
                Logger.Error(e);
            }
        }

        /// <inheritdoc />
        public async Task NextTrackAsync()
        {
            try
            {
                bool result = (await _mpdClient.SendAsync(new MpcCore.Commands.Player.Next())).Result;
            }
            catch (System.Exception e)
            {
                Logger.Error(e);
            }
        }

        /// <inheritdoc />
        public async Task SetVolumeAsync(int newVolume)
        {
            if (_mpdClient == null)
            {
                await Connect();
                return;
            }
            if (_currentVolumePercent != newVolume && newVolume >= 0 && _currentIsPlaying)
            {
                bool result = (await _mpdClient.SendAsync(new MpcCore.Commands.Options.ChangeVolume(newVolume - _currentVolumePercent))).Result;
                if (result)
                {
                    _currentVolumePercent = newVolume;
                }
            }

            await Task.Delay(110).ContinueWith(async t => await UpdatePlayer());
        }

        /// <inheritdoc />
        public async Task SetPlaybackProgressAsync(TimeSpan newProgress)
        {
            if (_mpdClient == null)
            {
                await Connect();
                return;
            }

            bool result = (await _mpdClient.SendAsync(new MpcCore.Commands.Player.Seek(newProgress.TotalSeconds))).Result;

            await Task.Delay(110).ContinueWith(async t => await UpdatePlayer());
        }

        /// <inheritdoc />
        public async Task SetShuffleAsync(bool shuffleOn)
        {
            if (_mpdClient == null)
            {
                await Connect();
                return;
            }

            bool result = (await _mpdClient.SendAsync(new MpcCore.Commands.Options.SetRandom(shuffleOn))).Result;

            await Task.Delay(110).ContinueWith(async t => await UpdatePlayer());
        }

        /// <inheritdoc />
        public async Task SetRepeatModeAsync(RepeatMode newRepeatMode)
        {
            if (_mpdClient == null)
            {
                await Connect();
                return;
            }

            bool result1;
            bool result2;
            switch (newRepeatMode)
            {
                case RepeatMode.Off:
                    result1 = (await _mpdClient.SendAsync(new MpcCore.Commands.Options.SetRepeat(false))).Result;
                    result2 = (await _mpdClient.SendAsync(new MpcCore.Commands.Options.SetSingle(false))).Result;
                    break;
                case RepeatMode.RepeatContext:
                    result1 = (await _mpdClient.SendAsync(new MpcCore.Commands.Options.SetRepeat(true))).Result;
                    result2 = (await _mpdClient.SendAsync(new MpcCore.Commands.Options.SetSingle(false))).Result;
                    break;
                case RepeatMode.RepeatTrack:
                    result1 = (await _mpdClient.SendAsync(new MpcCore.Commands.Options.SetRepeat(true))).Result;
                    result2 = (await _mpdClient.SendAsync(new MpcCore.Commands.Options.SetSingle(true))).Result;
                    break;
                default:
                    Logger.Warn($"No case for {newRepeatMode}");
                    break;
            }

            await Task.Delay(110).ContinueWith(async t => await UpdatePlayer());
        }

        private async Task<bool> Connect()
        {
            if (!_isActive)
            {
                return true;
            }

            if (string.IsNullOrEmpty(Host) || string.IsNullOrEmpty(Port))
            {
                Logger.Error($"Cannot connect to MPD because either Host or Port is empty.");
                return false;
            }

            try
            {
                _connection = new MpcCoreConnection(Host, Port);
                _mpdClient = new MpcCoreClient(_connection);
                return await _mpdClient.ConnectAsync();
            }
            catch (Exception e)
            {
                Logger.Error($"Error while trying to connect ~ {e.Message}");
                return false;
            }
        }

        private async Task NotifyTrackUpdate(MpcCore.Contracts.Mpd.IItem track)
        {
            // Local files have no id so we use name
            if (track.Id == _currentItemId && track.Title == _currentTrackName)
            {
                return;
            }

            MpcCore.Contracts.Mpd.IBinaryChunk responseReadPicture = (await _mpdClient.SendAsync(new MpcCore.Commands.Database.ReadPicture(track.Path))).Result;
            if (responseReadPicture == null)
            {
                responseReadPicture = (await _mpdClient.SendAsync(new MpcCore.Commands.Database.GetAlbumArt(track.Path))).Result;
            }
            Image albumart = null;
            if (responseReadPicture != null)
            {
                albumart = new ImageConverter().ConvertFrom(responseReadPicture.Binary) as Image;
            }

            _currentItemId = track.Id;
            _currentTrackName = track.Title;

            string artists = track.Artist;
            System.TimeSpan trackLength = TimeSpan.FromSeconds(0);
            if (track.Duration == 0)
            {
                System.Collections.Generic.List<String> value;
                if(track.MetaData.TryGetValue("time",out value))
                {
                    trackLength = TimeSpan.FromSeconds(Int32.Parse(value[0]));
                }
            } else
            {
                trackLength = TimeSpan.FromSeconds(track.Duration);
            }

            var trackUpdateInfo = new TrackInfoChangedEventArgs
            {
                Artist = artists,
                TrackName = track.Title,
                Album = track.Album,
                TrackLength = trackLength,
                AlbumArt = albumart,
            };

            TrackInfoChanged?.Invoke(this, trackUpdateInfo);
        }


        private void NotifyPlayState(bool isPlaying)
        {
            if (_currentIsPlaying == isPlaying)
            {
                return;
            }

            _currentIsPlaying = isPlaying;
            IsPlayingChanged?.Invoke(this, _currentIsPlaying);
        }

        private void NotifyTrackProgress(int elapsed)
        {
            _currentProgress = elapsed;
            TrackProgressChanged?.Invoke(this, TimeSpan.FromSeconds(_currentProgress));
        }

        private void NotifyVolume(int volume)
        {
            _currentVolumePercent = volume;
            VolumeChanged?.Invoke(this, _currentVolumePercent);
        }

        private void NotifyShuffle(bool shuffle)
        {
            _currentShuffle = shuffle;
            ShuffleChanged?.Invoke(this, _currentShuffle);
        }

        private RepeatMode ToRepeatMode(string state)
        {
            switch (state)
            {
                case "off":
                    return RepeatMode.Off;
                case "context":
                    return RepeatMode.RepeatContext;
                case "track":
                    return RepeatMode.RepeatTrack;
                default:
                    Logger.Warn($"No case for {state}");
                    return RepeatMode.Off;
            }
        }

        private void NotifyRepeat(bool repeat, bool single)
        {
            if(repeat)
            {
                if(single)
                    _currentRepeat = "track";
                else
                    _currentRepeat = "context";
            } else
                _currentRepeat = "off";
            RepeatModeChanged?.Invoke(this, ToRepeatMode(_currentRepeat));
        }

        private async void CheckMPDTimerOnElapsed(object sender, ElapsedEventArgs elapsedEventArgs)
        {
            // Spotify api does not provide a way to get realtime player status updates, so we have to resort to polling.
            await UpdatePlayer();
        }

        private async Task UpdatePlayer()
        {
            if (!_isActive)
            {
                _checkMPDTimer.Interval = 2500;
                return;
            }
            _checkMPDTimer.Stop();
            if (!_connection.IsConnected)
            {
                bool result = await Connect();
                if (!result)
                {
                    _checkMPDTimer.Interval = 2500;
                    _checkMPDTimer.Start();
                }
            }

            try
            {
                MpcCore.Contracts.Mpd.IStatus status = (await _mpdClient.SendAsync(new MpcCore.Commands.Status.GetStatus())).Result;
                if (status != null)
                {
                    NotifyPlayState(status.IsPlaying);
                    NotifyTrackProgress((int)status.Elapsed);
                    NotifyVolume(status.Volume);
                    NotifyShuffle(status.Random);
                    NotifyRepeat(status.Repeat, status.Single);
                }

                MpcCore.Contracts.Mpd.IItem currentTrack = (await _mpdClient.SendAsync(new MpcCore.Commands.Status.GetCurrentSong())).Result;                  
                if (currentTrack == null)
                {
                    // Playback can be null if there are no devices playing
                    await Task.Delay(TimeSpan.FromSeconds(1));
                    return;
                }
                else
                {
                    await NotifyTrackUpdate(currentTrack);
                }
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
            finally
            {
                // check again in case
                if (_isActive)
                {
                    try
                    {
                        _checkMPDTimer.Interval = _checkMPDTimer.Interval < 100
                                                ? 100 : _checkMPDTimer.Interval;
                        _checkMPDTimer.Start();
                    }
                    catch
                    {
                        // normally this should never fire but its here just in case
                        _checkMPDTimer = new Timer(_pollingInterval);
                    }
                }
            }
        }

        private void OnSettingChanged(string settingName)
        {
            SettingChanged?.Invoke(this, new SettingChangedEventArgs(settingName));
        }

        private async Task OnPlayerCommandFailed(Func<Task<bool>> command, [CallerMemberName] string caller = null)
        {
            var hasError = await command();
            if (hasError)
            {
                Logger.Warn($"Something went wrong with player command [{caller}].");
            }
        }

    }
}
