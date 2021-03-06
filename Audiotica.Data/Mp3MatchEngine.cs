﻿#region

using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Audiotica.Core.Utils;
using Audiotica.Core.Utils.Interfaces;
using Audiotica.Data.Model;
using Audiotica.Data.Model.AudioticaCloud;
using Audiotica.Data.Service.Interfaces;
using Audiotica.Data.Service.RunTime;

#endregion

namespace Audiotica.Data
{
    public enum Mp3Provider
    {
        Mp3Truck,
        SoundCloud,
        Netease,
        Mp3Clan,
        Meile,
        YouTube,
        Mp3Skull,
        ProstoPleer,
        Songily
    }

    public class Mp3MatchEngine
    {
        private readonly IAudioticaService _audioticaService;
        private readonly IDispatcherHelper _dispatcherHelper;
        private readonly INotificationManager _notificationManager;

        private readonly Mp3Provider[] _providers =
        {
            Mp3Provider.Netease,
            Mp3Provider.Mp3Truck,
            Mp3Provider.ProstoPleer,
            //Mp3Provider.Mp3Clan, 
            Mp3Provider.Meile,
            Mp3Provider.Mp3Skull,
            Mp3Provider.Songily,
            Mp3Provider.SoundCloud,
            Mp3Provider.YouTube
        };

        private readonly Mp3SearchService _service;

        public Mp3MatchEngine(IAppSettingsHelper settingsHelper, IAudioticaService audioticaService,
            INotificationManager notificationManager, IDispatcherHelper dispatcherHelper)
        {
            _audioticaService = audioticaService;
            _notificationManager = notificationManager;
            _dispatcherHelper = dispatcherHelper;
            _service = new Mp3SearchService(settingsHelper);
        }

        public async Task<string> FindMp3For(string title, string artist)
        {
            var sanitizedTitle = title.ToLower().Replace("feat.", "ft.") // better alternatives for matching
                .Replace("- bonus track", string.Empty)
                .Replace("bonus track", string.Empty)
                .Replace("- live", "(live)")
                .Replace("- remix", "(remix)")
                .Replace("a cappella", "acappella")
                .Replace("- acoustic version", "(acoustic)")
                .Replace("- acoustic", "(acoustic)")
                .Replace("- cover", "(cover)")
                .Replace("- stereo", string.Empty)
                .Replace("- mono", string.Empty)
                .Replace("- intro", string.Empty)
                .Replace("- no intro", string.Empty)
                .Replace("- ep version", string.Empty)
                .Replace("- deluxe edition", string.Empty)
                .Trim();

            if (sanitizedTitle.Contains("- from the") && sanitizedTitle.EndsWith("soundtrack"))
                sanitizedTitle = sanitizedTitle.Substring(0, sanitizedTitle.IndexOf("- from the") - 1);

            if (_audioticaService.IsAuthenticated && _audioticaService.CurrentUser.Subscription != SubscriptionType.None)
            {
                var matchResp = await _audioticaService.GetMatchesAsync(title, artist);

                if (matchResp.Success && matchResp.Data != null && matchResp.Data.Count > 0)
                {
                    var match = matchResp.Data.FirstOrDefault();
                    if (match != null)
                        return match.AudioUrl;
                }

                if (matchResp.StatusCode != HttpStatusCode.Unauthorized)
                {
                    await _dispatcherHelper.RunAsync(
                        () =>
                        {
                            _notificationManager.ShowError(
                                "Problem with Audiotica Cloud \"{0}\", finding mp3 locally.",
                                matchResp.Message ?? "Unknown");
                        });
                }
            }


            var currentProvider = 0;
            string url = null;

            while (currentProvider < _providers.Length)
            {
                var mp3Provider = _providers[currentProvider];
                try
                {
                    url = await GetMatch(mp3Provider, sanitizedTitle, artist).ConfigureAwait(false);
                }
                catch
                {
                    // ignored
                }

                if (url != null)
                {
                    break;
                }

                currentProvider++;
            }

            return url;
        }

        public async Task<string> GetMatch(Mp3Provider provider, string title, string artist, string album = null)
        {
            var webSongs = new List<WebSong>();

            switch (provider)
            {
                case Mp3Provider.ProstoPleer:
                    webSongs = await _service.SearchPleer(title, artist, album).ConfigureAwait(false);
                    break;
                case Mp3Provider.Netease:
                    webSongs = await _service.SearchNetease(title, artist, album).ConfigureAwait(false);
                    break;
                case Mp3Provider.Mp3Clan:
                    webSongs = await _service.SearchMp3Clan(title, artist, album).ConfigureAwait(false);
                    break;
                case Mp3Provider.Meile:
                    webSongs = await _service.SearchMeile(title, artist, album).ConfigureAwait(false);
                    break;
                case Mp3Provider.Mp3Truck:
                    webSongs = await _service.SearchMp3Truck(title, artist, album).ConfigureAwait(false);
                    break;
                case Mp3Provider.Songily:
                    webSongs = await _service.SearchSongily(title, artist, album).ConfigureAwait(false);
                    break;
                case Mp3Provider.SoundCloud:
                    webSongs = await _service.SearchSoundCloud(title, artist, album).ConfigureAwait(false);
                    break;
                case Mp3Provider.Mp3Skull:
                    webSongs = await _service.SearchMp3Skull(title, artist, album).ConfigureAwait(false);
                    break;
                case Mp3Provider.YouTube:
                    webSongs = await _service.SearchYoutube(title, artist, album).ConfigureAwait(false);
                    break;
            }

            if (webSongs != null)
            {
                var song = webSongs.FirstOrDefault(p => p.IsBestMatch);
                if (song != null) return song.AudioUrl;
                song = webSongs.FirstOrDefault(p => p.IsMatch && !p.IsLinkDeath);
                if (song != null) return song.AudioUrl;
            }
            return null;
        }
    }
}