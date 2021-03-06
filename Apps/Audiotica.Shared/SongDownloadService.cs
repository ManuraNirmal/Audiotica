﻿#region

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Networking.BackgroundTransfer;
using Windows.Storage;
using Windows.UI.Core;
using Audiotica.Core.Utils;
using Audiotica.Core.WinRt;
using Audiotica.Core.WinRt.Common;
using Audiotica.Core.WinRt.Utilities;
using Audiotica.Data.Collection;
using Audiotica.Data.Collection.Model;
using PCLStorage;
using TagLib;
using CreationCollisionOption = Windows.Storage.CreationCollisionOption;

#endregion

namespace Audiotica
{
    public class SongDownloadService : ISongDownloadService
    {
        #region Constructor

        public SongDownloadService(ICollectionService service, ISqlService sqlService, CoreDispatcher dispatcher)
        {
            this.service = service;
            this.sqlService = sqlService;
            this.dispatcher = dispatcher;
            ActiveDownloads = new ObservableCollection<Song>();
        }

        #endregion

        #region Private Fields

        private readonly CoreDispatcher dispatcher;

        private readonly ICollectionService service;

        private readonly ISqlService sqlService;

        #endregion

        #region Helper methods

        private async Task DiscoverActiveDownloadsAsync()
        {
            // list containing all the operations (except grouped ones)
            IReadOnlyList<DownloadOperation> downloads;
            try
            {
                downloads = await BackgroundDownloader.GetCurrentDownloadsAsync();
            }
            catch
            {
                // failed silently
                return;
            }

            // no downloads? exit!
            if (downloads.Count == 0)
            {
                return;
            }

            foreach (var download in downloads)
            {
                // With the uri get the song
                var songEntry = service.Songs.FirstOrDefault(p => p.DownloadId == download.Guid.ToString());

                if (songEntry != null)
                {
                    HandleDownload(songEntry, download, false);
                }
            }
        }

        /// <summary>
        ///     Use as callback of every downloads progress
        /// </summary>
        /// <param name="download">
        ///     The download that has just progressed
        /// </param>
        private void DownloadProgress(DownloadOperation download)
        {
            // Thread safety comes first!
            dispatcher.RunAsync(
                CoreDispatcherPriority.Normal,
                () =>
                {
                    // Get the associated song BackgroundDownload
                    var songDownload =
                        ActiveDownloads.FirstOrDefault(
                            p => ((DownloadOperation) p.Download.DownloadOperation).Guid == download.Guid);

                    if (songDownload == null)
                    {
                        return;
                    }

                    songDownload.Download.Status = download.Progress.Status.ToString();

                    if (download.Progress.TotalBytesToReceive > 0)
                    {
                        songDownload.Download.BytesToReceive = download.Progress.TotalBytesToReceive;
                        songDownload.Download.BytesReceived = download.Progress.BytesReceived;
                    }
                    else
                    {
                        songDownload.Download.Status = "Waiting";
                    }
                });
        }

        /// <summary>
        ///     Call internally to report a finished BackgroundDownload
        /// </summary>
        /// <param name="song">
        ///     The song that just finished downloading.
        /// </param>
        /// <returns>
        ///     Task.
        /// </returns>
        private async Task DownloadFinishedForAsync(Song song)
        {
            var downloadOperation = (DownloadOperation) song.Download.DownloadOperation;

            await UpdateId3TagsAsync(song, downloadOperation.ResultFile);

            var filename = song.Name.CleanForFileName("Invalid Song Name");
            if (song.ArtistName != song.Album.PrimaryArtist.Name)
            {
                filename = song.ArtistName.CleanForFileName("Invalid Artist Name") + "-" + filename;
            }

            var path = string.Format(
                AppConstant.SongPath,
                song.Album.PrimaryArtist.Name.CleanForFileName("Invalid Artist Name"),
                song.Album.Name.CleanForFileName("Invalid Album Name"),
                filename);

            var newDestination = await WinRtStorageHelper.CreateFileAsync(path, KnownFolders.MusicLibrary);

            downloadOperation.ResultFile.MoveAndReplaceAsync(newDestination);

            song.AudioUrl = newDestination.Path;
            song.SongState = SongState.Downloaded;
            song.DownloadId = null;
            await sqlService.UpdateItemAsync(song);
        }

        /// <summary>
        ///     Updates the id3 tags. WARNING, there needs to be more testing with lower end devices.
        /// </summary>
        /// <param name="song">The song.</param>
        /// <param name="file">The file.</param>
        private async Task UpdateId3TagsAsync(Song song, IStorageFile file)
        {
            using (var fileStream = await file.OpenStreamForWriteAsync().ConfigureAwait(false))
            {
                File tagFile;

                try
                {
                    tagFile = File.Create(new SimpleFileAbstraction(file.Name, fileStream, fileStream));
                }
                catch
                {
                    // either the download is corrupted or is not an mp3 file
                    return;
                }

                var newTags = tagFile.GetTag(TagTypes.Id3v2, true);

                newTags.Title = song.Name;

                if (song.Artist.ProviderId != "autc.unknown")
                {
                    newTags.Performers = song.ArtistName.Split(',').Select(p => p.Trim()).ToArray();
                }

                newTags.Album = song.Album.Name;
                newTags.AlbumArtists = new[] {song.Album.PrimaryArtist.Name};

                if (!string.IsNullOrEmpty(song.Album.Genre))
                {
                    newTags.Genres = song.Album.Genre.Split(',').Select(p => p.Trim()).ToArray();
                }

                newTags.Track = (uint) song.TrackNumber;

                newTags.Comment = "Downloaded with Audiotica - http://audiotica.fm";

                try
                {
                    if (song.Album.HasArtwork)
                    {
                        var albumFilePath = string.Format(AppConstant.ArtworkPath, song.Album.Id);
                        var artworkFile = await StorageHelper.GetFileAsync(albumFilePath).ConfigureAwait(false);

                        using (var artworkStream = await artworkFile.OpenAsync(FileAccess.Read))
                        {
                            using (var memStream = new MemoryStream())
                            {
                                await artworkStream.CopyToAsync(memStream);
                                newTags.Pictures = new IPicture[]
                                {
                                    new Picture(
                                        new ByteVector(
                                            memStream.ToArray(),
                                            (int) memStream.Length))
                                };
                            }
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    // Should never happen, since we are opening the files in read mode and nothing is locking it.
                }

                await Task.Run(() => tagFile.Save());
            }
        }

        /// <summary>
        ///     Hanbdles a single BackgroundDownload for a song.
        /// </summary>
        /// <param name="song">
        ///     The song to be downloaded
        /// </param>
        /// <param name="download">
        ///     The download operation
        /// </param>
        /// <param name="start">
        ///     Either the download is started or just handled
        /// </param>
        private async void HandleDownload(Song song, DownloadOperation download, bool start)
        {
            if (song == null || download == null)
            {
                return;
            }

            song.Download = new BackgroundDownload(download);
            ActiveDownloads.Add(song);

            try
            {
                var progressCallback = new Progress<DownloadOperation>(DownloadProgress);
                if (start)
                {
                    // Start the BackgroundDownload and attach a progress handler.
                    await download.StartAsync().AsTask(song.Download.CancellationTokenSrc.Token, progressCallback);
                }
                else
                {
                    // The BackgroundDownload was already running when the application started, re-attach the progress handler.
                    await download.AttachAsync().AsTask(song.Download.CancellationTokenSrc.Token, progressCallback);
                }

                // Download Completed
                var response = download.GetResponseInformation();

                // Make sure it is success
                if (response.StatusCode < 400)
                {
                    await DownloadFinishedForAsync(song);
                }
                else
                {
                    song.SongState = SongState.None;
                    sqlService.UpdateItem(song);
                    download.ResultFile.DeleteAsync();
                }
            }
            catch
            {
                song.SongState = SongState.None;
                sqlService.UpdateItem(song);
                download.ResultFile.DeleteAsync();
            }
            finally
            {
                ActiveDownloads.Remove(song);
            }
        }

        #endregion

        #region Implementation of ISongDownloadDataService

        public ObservableCollection<Song> ActiveDownloads { get; private set; }

        public async void LoadDownloads()
        {
            await DiscoverActiveDownloadsAsync();

            Debug.WriteLine("Loaded downloads.");
        }

        public void PauseAll()
        {
            foreach (var activeDownload in ActiveDownloads)
            {
                ((DownloadOperation) activeDownload.Download.DownloadOperation).Pause();
            }
        }

        public void Cancel(BackgroundDownload backgroundDownload)
        {
            backgroundDownload.CancellationTokenSrc.Cancel();
        }

        public async Task StartDownloadAsync(Song song)
        {
            song.SongState = SongState.Downloading;

            try
            {
                var path = string.Format("songs/{0}.mp3", song.Id);

                var destinationFile =
                    await
                        WinRtStorageHelper.CreateFileAsync(path, ApplicationData.Current.LocalFolder,
                            CreationCollisionOption.ReplaceExisting).ConfigureAwait(false);

                var downloader = new BackgroundDownloader();
                var download = downloader.CreateDownload(new Uri(song.AudioUrl), destinationFile);
                download.Priority = BackgroundTransferPriority.High;
                song.DownloadId = download.Guid.ToString();

                await sqlService.UpdateItemAsync(song).ConfigureAwait(false);
                dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => HandleDownload(song, download, true));
            }
            catch (Exception e)
            {
                if (e.Message.Contains("there is not enough space on the disk"))
                {
                    dispatcher.RunAsync(
                        CoreDispatcherPriority.Normal,
                        () => CurtainPrompt.ShowError("Not enough disk space to download."));
                }

                dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => song.SongState = SongState.None);
                sqlService.UpdateItemAsync(song).ConfigureAwait(false);
            }
        }

        #endregion
    }
}