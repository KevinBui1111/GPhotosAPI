using Google.Apis.Auth.OAuth2;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GPhotosAPI
{
    class PhotoService
    {
        static HttpClient httpClient = new HttpClient();

        private readonly ILogger<PhotoService> _logger;

        public PhotoService(ILogger<PhotoService> logger)
        {
            _logger = logger;
        }
        internal async Task RunAsync()
        {
            string albumId = Console.ReadLine();
            while (albumId != "q")
            {
                Console.Clear();
                await LoginAsync();
                //await GetAlbumsAsync();
                await ListInAlbumsAsync(albumId);
                //await GetPhotoAsync(albumId);

                albumId = Console.ReadLine();
            }
        }
        private async Task LoginAsync()
        {
            UserCredential credential;
            using (var stream = new FileStream(@"Config\client_secret.json", FileMode.Open, FileAccess.Read))
            {
                credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.Load(stream).Secrets,
                    new[] {
                        "https://www.googleapis.com/auth/photoslibrary.readonly"
                    },
                    Environment.UserName, CancellationToken.None, new FileDataStore("Books.ListMyLibrary"));
            }
            _logger.LogInformation("Get credential sucessfully");
            await credential.GetAccessTokenForRequestAsync();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                credential.Token.TokenType, credential.Token.AccessToken);
        }

        async Task<List<Album>> GetAlbumsAsync()
        {
            var album_list = new List<Album>();
            AlbumsResponse albumRes = null;
            do
            {
                _logger.LogInformation($"=== Begin fetch next page ===");
                var res = await httpClient.GetAsync($"https://photoslibrary.googleapis.com/v1/albums?pageSize=50&pageToken={albumRes?.nextPageToken}");
                string res_text = await res.Content.ReadAsStringAsync();
                if (res.IsSuccessStatusCode)
                {
                    albumRes = JsonConvert.DeserializeObject<AlbumsResponse>(res_text);
                    album_list.AddRange(albumRes.albums);

                    foreach (Album alb in albumRes.albums)
                    {
                        _logger.LogInformation($"Album: {alb.title} - {alb.mediaItemsCount} - {alb.id}");
                    }
                }
                else
                {
                    albumRes = null;
                    _logger.LogWarning($"Fail API: {res.StatusCode} - {res_text}");
                }
            }
            while (albumRes?.nextPageToken != null);

            return album_list;
        }

        async Task<List<MediaItem>> ListInAlbumsAsync(string albumId)
        {
            var album_list = new List<MediaItem>();
            MediaItemsResponse albumRes = null;
            do
            {
                _logger.LogInformation($"=== Begin fetch next page ===");
                string json = JsonConvert.SerializeObject(new
                {
                    albumId,
                    pageSize = 100,
                    pageToken = albumRes?.nextPageToken
                });
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var res = await httpClient.PostAsync($"https://photoslibrary.googleapis.com/v1/mediaItems:search", content);
                string res_text = await res.Content.ReadAsStringAsync();
                if (res.IsSuccessStatusCode)
                {
                    albumRes = JsonConvert.DeserializeObject<MediaItemsResponse>(res_text);
                    album_list.AddRange(albumRes.mediaItems);

                    foreach (MediaItem alb in albumRes.mediaItems)
                    {
                        LogLevel lv = alb.mediaMetadata.width < 2000 || alb.mediaMetadata.height < 2000 ? LogLevel.Warning : LogLevel.Information;
                        _logger.Log(lv, $"MediaItem: {alb.filename} - {alb.mediaMetadata.width} x {alb.mediaMetadata.height} - {alb.id}");
                    }
                }
                else
                {
                    albumRes = null;
                    _logger.LogWarning($"Fail API: {res.StatusCode} - {res_text}");
                }
            }
            while (albumRes?.nextPageToken != null);

            return album_list;
        }
        async Task<MediaItem> GetPhotoAsync(string mediaItemId)
        {
            MediaItem alb = null;

            _logger.LogInformation($"=== Begin GetPhotoAsync ===");

            var res = await httpClient.GetAsync($"https://photoslibrary.googleapis.com/v1/mediaItems/{mediaItemId}");
            string res_text = await res.Content.ReadAsStringAsync();
            if (res.IsSuccessStatusCode)
            {
                alb = JsonConvert.DeserializeObject<MediaItem>(res_text);
                LogLevel lv = alb.mediaMetadata.width < 2000 || alb.mediaMetadata.height < 2000 ? LogLevel.Warning : LogLevel.Information;
                _logger.Log(lv, $"MediaItem: {alb.filename} - {alb.mediaMetadata.width} x {alb.mediaMetadata.height} - {alb.id}");
            }
            else
            {
                _logger.LogWarning($"Fail API: {res.StatusCode} - {res_text}");
            }

            return alb;
        }

    }
}
