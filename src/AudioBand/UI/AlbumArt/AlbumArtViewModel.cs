﻿using AudioBand.AudioSource;
using AudioBand.Extensions;
using AudioBand.Messages;
using AudioBand.Models;
using AudioBand.Settings;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AudioBand.UI
{
    /// <summary>
    /// View model for the album art.
    /// </summary>
    public class AlbumArtViewModel : LayoutViewModelBase<AlbumArt>
    {
        private readonly IAppSettings _appsettings;
        private readonly IAudioSession _audioSession;
        private ImageSource _albumArt;

        /// <summary>
        /// Initializes a new instance of the <see cref="AlbumArtViewModel"/> class.
        /// </summary>
        /// <param name="appsettings">The app settings.</param>
        /// <param name="dialogService">The dialog service.</param>
        /// <param name="audioSession">The audio session.</param>
        /// <param name="messageBus">The message bus.</param>
        public AlbumArtViewModel(IAppSettings appsettings, IDialogService dialogService, IAudioSession audioSession, IMessageBus messageBus)
            : base(messageBus, appsettings.CurrentProfile.AlbumArt)
        {
            DialogService = dialogService;
            _appsettings = appsettings;
            _audioSession = audioSession;

            appsettings.ProfileChanged += AppsettingsOnProfileChanged;
            audioSession.PropertyChanged += AudioSessionOnPropertyChanged;
        }

        /// <summary>
        /// Gets or sets the placeholder path.
        /// </summary>
        [TrackState]
        public string PlaceholderPath
        {
            get => Model.PlaceholderPath;
            set => SetProperty(Model, nameof(Model.PlaceholderPath), value);
        }

        /// <summary>
        /// Gets the current album art image.
        /// </summary>
        public ImageSource AlbumArt
        {
            get => _albumArt;
            private set => SetProperty(ref _albumArt, value);
        }

        /// <summary>
        /// Gets the dialog service.
        /// </summary>
        public IDialogService DialogService { get; }

        /// <inheritdoc />
        protected override void OnEndEdit()
        {
            base.OnEndEdit();
            MapSelf(Model, _appsettings.CurrentProfile.AlbumArt);
        }

        private void AppsettingsOnProfileChanged(object sender, EventArgs e)
        {
            Debug.Assert(IsEditing == false, "Should not be editing");
            MapSelf(_appsettings.CurrentProfile.AlbumArt, Model);
            RaisePropertyChangedAll();
        }

        private void AudioSessionOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(IAudioSession.AlbumArt))
            {
                return;
            }

            AlbumArtUpdated(_audioSession.AlbumArt);
        }

        private void AlbumArtUpdated(Image albumArt)
        {
            if (albumArt == null)
            {
                try
                {
                    AlbumArt = new BitmapImage(new Uri(PlaceholderPath));
                    AlbumArt.Freeze();
                }
                catch
                {
                    AlbumArt = null;
                }

                return;
            }

            try
            {
                AlbumArt = albumArt.ToImageSource();
            }
            catch (InvalidOperationException)
            {
                Logger.Error("Error while trying to update AlbumArt in Win10 (see GitHub issue #9)");
            }
        }
    }
}
