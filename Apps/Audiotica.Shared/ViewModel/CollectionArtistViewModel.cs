﻿#region

using System.Linq;
using Windows.UI.Xaml.Controls;
using Audiotica.Data.Collection;
using Audiotica.Data.Collection.Model;
using Audiotica.Data.Service.Interfaces;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using GalaSoft.MvvmLight.Messaging;
using IF.Lastfm.Core.Objects;

#endregion

namespace Audiotica.ViewModel
{
    public class CollectionArtistViewModel : ViewModelBase
    {
        private readonly AudioPlayerHelper _audioPlayer;
        private readonly ICollectionService _service;
        private readonly IScrobblerService _lastService;
        private readonly RelayCommand<ItemClickEventArgs> _songClickCommand;
        private Artist _artist;
        private LastArtist _lastArtist;

        public CollectionArtistViewModel(ICollectionService service, IScrobblerService lastService, AudioPlayerHelper audioPlayer)
        {
            _service = service;
            _lastService = lastService;
            _audioPlayer = audioPlayer;
            _songClickCommand = new RelayCommand<ItemClickEventArgs>(SongClickExecute);
            MessengerInstance.Register<GenericMessage<long>>(this, "artist-coll-detail-id", ReceivedId);

            if (IsInDesignMode)
                SetArtist(0);
        }

        public Artist Artist
        {
            get { return _artist; }
            set { Set(ref _artist, value); }
        }

        public LastArtist LastArtist
        {
            get { return _lastArtist; }
            set { Set(ref _lastArtist, value); }
        }

        public RelayCommand<ItemClickEventArgs> SongClickRelayCommand
        {
            get { return _songClickCommand; }
        }

        private void ReceivedId(GenericMessage<long> obj)
        {
            LastArtist = null;
            SetArtist(obj.Content);
        }

        private async void SongClickExecute(ItemClickEventArgs e)
        {
            var song = e.ClickedItem as Song;

            await _service.ClearQueueAsync();

            foreach (var queueSong in _artist.Songs)
            {
                await _service.AddToQueueAsync(queueSong);
            }

#if WINDOWS_PHONE_APP
            _audioPlayer.PlaySong(_service.PlaybackQueue[_artist.Songs.IndexOf(song)]);
#endif
        }

        private async void SetArtist(long id)
        {
            Artist = _service.Artists.FirstOrDefault(p => p.Id == id);
            try
            {
                LastArtist = await _lastService.GetDetailArtist(Artist.Name);
            }
            catch { }
        }
    }
}