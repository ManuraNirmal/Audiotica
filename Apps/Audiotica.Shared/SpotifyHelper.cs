﻿#region

using System.Linq;
using System.Threading.Tasks;

using Audiotica.Core.Exceptions;
using Audiotica.Data.Collection;
using Audiotica.Data.Collection.Model;
using Audiotica.Data.Spotify.Models;

#endregion

namespace Audiotica
{
    public static class SpotifyHelper
    {
        public static async Task<SaveResults> SaveTrackAsync(SimpleTrack track, FullAlbum album)
        {
            try
            {
                if (track == null || album == null)
                {
                    return new SaveResults() { Error = SavingError.Unknown };
                }

                var preparedSong = track.ToSong();

                var exists = App.Locator.CollectionService.SongAlreadyExists(
                    preparedSong.ProviderId,
                    track.Name,
                    album.Name,
                    album.Artist != null ? album.Artist.Name : track.Artist.Name);

                if (exists != null)
                {
                    return new SaveResults { Error = SavingError.AlreadyExists, Entry = exists };
                }

                var fullTrack = track as FullTrack ?? await App.Locator.Spotify.GetTrack(track.Id);

                var artist = fullTrack != null ? fullTrack.Artist : track.Artist;

                preparedSong.ArtistName = fullTrack != null
                                              ? string.Join(", ", fullTrack.Artists.Select(p => p.Name))
                                              : artist.Name;
                preparedSong.Album = album.ToAlbum();
                preparedSong.Artist = album.Artist.ToArtist();
                preparedSong.Album.PrimaryArtist = preparedSong.Artist;
                await App.Locator.CollectionService.AddSongAsync(preparedSong).ConfigureAwait(false);
                CollectionHelper.MatchSong(preparedSong);
                return new SaveResults() { Error = SavingError.None, Entry = preparedSong};
            }
            catch (NetworkException)
            {
                return new SaveResults() { Error = SavingError.Network };
            }
            catch
            {
                return new SaveResults() { Error = SavingError.Unknown };
            }
        }
    }
}