using Google.Apis.Auth.OAuth2;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                //await RemoveFromAlbumsAsync(albumId, mediaIds);
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
                        //"https://www.googleapis.com/auth/photoslibrary.readonly",
                        "https://www.googleapis.com/auth/photoslibrary"
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
            var list_wait = new List<Task>();
            var album_list = new List<MediaItem>();
            MediaItemsResponse albumRes = null;
            do
            {
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
                    //album_list.AddRange(albumRes.mediaItems);
                    _logger.LogInformation($"=== Begin fetch next page {albumRes.mediaItems.Count}===");

                    foreach (MediaItem alb in albumRes.mediaItems)
                    {
                        LogLevel lv = alb.mediaMetadata.photo != null && (alb.mediaMetadata.width * alb.mediaMetadata.height < 2_000_000) ? LogLevel.Warning : LogLevel.Information;
                        if (lv == LogLevel.Warning)
                        {
                            list_wait.Add(process_warningAsync(alb));
                        }
                        else
                        {
                            await Task.WhenAll(list_wait);
                            list_wait.Clear();
                            _logger.Log(lv, $"{alb.filename} - {alb.mediaMetadata.width}x{alb.mediaMetadata.height} - {alb.id}");
                        }
                    }
                    await Task.WhenAll(list_wait);
                }
                else
                {
                    albumRes = null;
                    _logger.LogWarning($"Fail API: {res.StatusCode} - {res_text}");
                }
            }
            while (albumRes?.nextPageToken != null);

            return album_list;

            async Task process_warningAsync(MediaItem item)
            {
                var orgMedia = await GetPhotoAsync(item.id);

                if (orgMedia.mediaMetadata.width * orgMedia.mediaMetadata.height != item.mediaMetadata.width * item.mediaMetadata.height)
                {
                    _logger.Log(LogLevel.Error, $"{item.filename} - {item.mediaMetadata.width}x{item.mediaMetadata.height} - {orgMedia.mediaMetadata.width}x{orgMedia.mediaMetadata.height} - {item.id}");
                    album_list.Add(orgMedia);
                }
                else
                    _logger.LogWarning($"{item.filename} - {item.mediaMetadata.width}x{item.mediaMetadata.height} - {item.id}");

            }
        }
        async Task<MediaItem> GetPhotoAsync(string mediaItemId)
        {
            MediaItem alb = null;

            //_logger.LogInformation($"=== Begin GetPhotoAsync ===");

            var res = await httpClient.GetAsync($"https://photoslibrary.googleapis.com/v1/mediaItems/{mediaItemId}");
            string res_text = await res.Content.ReadAsStringAsync();
            if (res.IsSuccessStatusCode)
            {
                alb = JsonConvert.DeserializeObject<MediaItem>(res_text);
                LogLevel lv = alb.mediaMetadata.width < 2000 || alb.mediaMetadata.height < 2000 ? LogLevel.Warning : LogLevel.Information;
                //_logger.Log(lv, $"{alb.filename} - {alb.mediaMetadata.width} x {alb.mediaMetadata.height} - {alb.id}");
            }
            else
            {
                _logger.LogWarning($"Fail API: {res.StatusCode} - {res_text}");
            }
            return alb;
        }
        async Task RemoveFromAlbumsAsync(string albumId, string[] mediaIDs)
        {
            var list_process = mediaIDs.AsEnumerable();
            do
            {
                _logger.LogInformation($"=== Begin Remove ===");
                var list_remove = list_process.Take(50);
                list_process = list_process.Skip(50);
                string json = JsonConvert.SerializeObject(new
                {
                    mediaItemIds = list_remove
                });
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var res = await httpClient.PostAsync($"https://photoslibrary.googleapis.com/v1/albums/{albumId}:batchRemoveMediaItems", content);
                string res_text = await res.Content.ReadAsStringAsync();
                if (res.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Remove batch success");
                }
                else
                {
                    _logger.LogWarning($"Fail Remove batch: {res_text}");
                }
            }
            while (list_process.Any());
        }

        string[] mediaIds = {
"ADJDZHaHiMqa_YIpADgNyLHUVu4sX4oZfnXnwMdO4mLsAjJQR2e5QPsQlITLycOe970KJgAFhCBnbNOpHuhxMjmk5GaO5Sz3vg",
"ADJDZHYHW11cKuJnFOysSRIwIMkvIGHr-ZUJStIBHatkYFRqHPYfPHX-I1hjdYXjGtJNCSfxSJVoMRAVXb0EYr1CaUurp08i3Q",
"ADJDZHYkOdRzp8T2PAI0X0ub-l0Wx3iK8vm3ll_5BJVIGSmKCDofF_mSz0nJP-9AfusHKHnAlG5dj80dddGoBKuES1ToY_CZhg",
"ADJDZHZxEj6WIZqRnoKD4hkDRgXm0LqIWAw6KruwV_On-Mkx6UxlS21D4Wr7aVM_sFil2LlHAgSx__zW4OyAhrDXsNsKbcmakw",
"ADJDZHaE8p5EjUJFtV_Oo-dk-i59T_d981nf40qiyG3Bokmb5SBiQj8EonV5XHIphrJhKPzc9qs88TCzNh4PnqgQDc5-KJdx_g",
"ADJDZHaAfiKgc0O-2F8Bja-hOP6cGvVA9zMg7lIHVpz0LQyDFBvDhpTx7HXfs73iaU5-U6AShfGN4oEeQAP7QGxaFXR_hBkd8Q",
"ADJDZHYqkGM2heU2_AT9RcELMAkbXo4aje8vXcllD4rF9bpmI0UAzzemh9TZfdo89G8vT2VTJJq8K2j573HClyqtT7gWtrMTJA",
"ADJDZHYSfZw5SOAKAxAxAhYIR8bf2VEcLcGfDBHgncI7SJTV-Q_ut2qu6ItChbzMvyNqFHrUMYxV0PVfgz-H4YZxWvSQeUs0jA",
"ADJDZHbMHCqm79NI8K1Gm1qRC7FgFBy_0c7FHTHKSLFJx1B_rUobXXwdPDgr4axFiqTkfnLr3KH-Py20nzAekXLFeEJIYcDBPQ",
"ADJDZHYaKsNJNDuEizGfrSA6UXlFoiIJg-GEcu2EuVty9wfoNDaGgT4qdDQHXcUqfyPDMtchn_WdJWJtM06RggTJI0n6i8EaOA",
"ADJDZHamGUhUuvU0uNDeqnfUJ9s8-J1hBJgueof-Cw_KFOpGmpDPw4hq32hvOLKTVLObdS5HDb39H-0PEWnx-fQGcD5Gcc5CAA",
"ADJDZHaSSnv7qmWk9A5XUogBdyx0S-STf-YUC-_XnrIJqQcK0DTaX2RR1zpwsufuJ1HsNrUNjWpunxX9-XJrElCmPQAMSj1kgA",
"ADJDZHb__qM4dY3bL5tlIj-5k3BUzc1iCeYR1Vx-2yYIWdXDIAjpkX6pkoFVISTcJEyHssIEqzNJU76aVlaVX52Aka6FULQvBg",
"ADJDZHbxSZP2sU8kLEYBiy538SqM6FK0wEWNwcoY0o9DSuoVTfSWW0bKnQ-RJC--rWrzeswlcSNJQTB2vC6d1dD3CAVsWV28uQ",
"ADJDZHY6epLtxoPNb7EEkemLbzGiJnGW3spne3A_BhmvUe7sA-uWpKO8uW027uTLzVGis_h-pmNyPehig9QpYjhXIU-MVYR87A",
"ADJDZHZcHaAOoj_anSN2gYRmwAK4BH2HyZ6u3Vcm6JKW4Y12zVPPkS_oKQrFL_PxH0op76DVEzMuH8bbcsco7buwI0TmA4sfXw",
"ADJDZHZ22MU18lCLf8E0AuT3SneZ7L8eQpaWWZhWUmnSUGiGQRCHrIJi0ynBn6xPbmS3lGRc7Th4lO6KYSWfqLc3CCSyvzBrYw",
"ADJDZHZnktjDQh-9bfKewavdKdS2IWGHYkTxW0xLA17ig2ljYKH8GIkk7XuOhLlQOw-Nz1K3ULYL77BJNi8z1oCqrnnBiWeGmg",
"ADJDZHaA-7tdFkln3x__xtiEOoPwxA4nc-W1gNmQN2e7OzGrCA9kOPMMm37qucgrbhOiBRJ4dgaBYfBSFwZknriMEZpKDdIhvQ",
"ADJDZHZGuj6cvesLy2oukMNfXlyH-1yoxN0ZIrKyG3VYIqFaP_Uvm7QCiJHQ5dmgQeWQsIiQjKSf1zIkrwcGVe-CFMG2huSLPQ",
"ADJDZHa591JOLresHyJ7FyzDp6sos4GuhkUzEr-pXs4pFxXtegFoDuos5UzMy2tNBJEUbbU6FaZ3z2IYaLCEAcaRbBsQUedK9g",
"ADJDZHZkSA9VS5lvEzd70UoGcwo1GH5xQVk2ytIaWz6xqcQ0VbXBT80apeyxa7puvxGChYZHj5VUgc4VGi7e8AxqsxFgEOROUA",
"ADJDZHbtd0a4J9PTvEJSoysQOnPD9rr5Y28n6Bs4LwN8TQI6q22gSULfFxtnsOqweXD20Dri5XTmu4OqXDntO4KBgtxlgNJVTg",
"ADJDZHYFAUbvx0QFaZlm0x7JElh3zqg3hAMPECMkWa1KO0VyHPukv7F3eXdVKo0ZFyfOSRwYebIDXUyrearxfKlvZv-dZYQt_g",
"ADJDZHbvbt78WlE-DPoGYgT6GZY4k6DrpaEdu-q0-peGziOMtm__0GkF_Ti9XRDhzFAOMZWDzh8ZLbxtCGYyLJEiGRK2TiAcYw",
"ADJDZHbZITlSTtlXxPlXlUbWDSppHXlYNOzb4HkBB8PQy21odt4vKWb6oD2nxGytARgGwk3CjM9Y8eaG-Y_9-p3eV_4D0HuAzQ",
"ADJDZHbQX3aDz9jKRzzLDKTYRZmKNuhaZG3YcD_LJR1YgMO3uQ8RvG4VdpWxGgABxxoI7blbiBunKjZEWtxKjuEDZwkI3g3yxg",
"ADJDZHabWrGAYsxPsqjOw8BZeEcTyHXD7WMh-HKTCDwWtm5oBxTB_-IVu8-vjJol1T9ryf0u1dVboOl4Gq_uu1X8TMmYdEe1Ug",
"ADJDZHa-9Eeq75AM3D1KkLxXYVzlA8o7yLb3kc9sacuWE5z_8M1RcyBtr6UCOvHLreX08sCDoSlJHalce3yoj5ElhgfLb42BBw",
"ADJDZHaTxDppldQhJj4c9mY6IXSPdi2idTq0hjfvO3vbB4saQvwbQYgXO9YtSi3YzvnatrXmjmBOeSP1Xh1ot58pfvxtyHCegQ",
"ADJDZHbzkZMaY8F_A4B5ZfrHDD4iDqW0kLdMuAE5j7bh6PTahHTf2lTJZBNEzL3Ti6zBtFzf_N31YE8XRBzysw5fOwdYbYyt2Q",
"ADJDZHbbXWXvSs7-GDiTkZdiVB4gjZk0UOwwHw6uTaq9ShuQrdNfOAHajAae5-9VRE6xaOKA_sCSg56UUj_Gyymcj9y_DdTwzg",
"ADJDZHadVjwv3_Nf-raekefTtBtDMjihH1DuNRTvJo2HL4a9L3IpOteGhDylrQQUNt7l29NAnAvy6VK16k8dSV8BsejkT9DI1Q",
"ADJDZHYWG1W8s5_iNHJrlX-oMScW-Q8Tfgssmw1egOL9Vvorbl8T1GrkC0x_nuqbzpwQ6A7MI03yDiQjQ1ijxjIaymNPi--mGw",
"ADJDZHZa9ejSbPbtiyzfc3asWKnxyzY7TjqAtPuE1KQi0oKpPf1r6izdzNXQKujxVcxT4Faahr3dGaDvdcbL6teD8JfrhhoQZQ",
"ADJDZHYzOa46wpFUBuJLbrzEvJhOpiayftFB2JVb6Rk4tFNv_ky0eoMYz7OiW2a8NoJezisgjrVcjCPsR-PGTcSO4w-QH2ojWw",
"ADJDZHb5MpKQIr27FeWtGW_I4ISA6mQT108lhtL7NCPKksfS_a_jRA9Flyh_3FxzQdMhhI-tXkBSLAId0p4WLImCCbu7sM34qA",
"ADJDZHZgPqpa0mbu2jnZex8Mdu4GDvkGWjAH4dvGmv88MpM7iZXGhc2_Vh8USf2UAxwrldKIHPf_fGMsqyCzBCB4jrbNTofZcw",
"ADJDZHZVOwhj4KlAtzRIqicmykWMT33vZmqOY2JxITONErQnZPm2thqHExGO3ZWrPaZHv4-RFXS0vP_fwaG1C9-TPCXQhQNItw",
"ADJDZHbeirpfvDwLq2L1YVadPIUk_rQx6UdHzxJwG51W9sr4tIFsyU8Ytrfs5aGyTnCgShXck6Zyg_myJ_QdHcBDu_umzjhnvA",
"ADJDZHb9fLEJC-hzR82DU3EI-qBgUg6zKpnxlzQ8BWov4TG-atNMkxq-6ZW2_blOodmL9CK4o0Rp_6uh9Vvlaq5aD4iJT8AC7g",
"ADJDZHatMk0BuzK3yG44WrUzX165Sr7fX8Q01922jCzzcOAr5JDjQ-miWPumTLf2f2Xvdxt4A3UbZ95od9750_L_fk9-gwoVMg",
"ADJDZHZnKHA_sNBuHVvUjmUB1EFS18ZdX7Ar4_aiwTddpoGCzq4xH6YAlMkv5ANq6l0N5O9y76BqtzvKvDDgrmm8Q9XbhIOFVw",
"ADJDZHYcYY9hO6z-ZS-wyPP6pLFcYV1MSrsi5pQczhXffTsomj5N5zwL91RiXpdOd7tCK0mIZ7B9pFb-i43C9iyRU4Ra9XpU8A",
"ADJDZHaIaMBeqfaoO8J0l0fFWSwImCvBw8JIRhKcGHEDBgGxDbDQhjc_aZvtflwisZ7856OEoBhEzuWuAM462prP0i57ghVjxQ",
"ADJDZHZC1ut1kPDsdgnfgeYAem1ikldgx-b9m7_tbfS-TKdosQkzmDAn9i9JBQ-abDFpruRkychs4duaKks4hDqyDDVnxg5WsA",
"ADJDZHYhST0efE-3sYPo9LJGWoiDMdPSsQfPIPvfADbyzvSJRj6xScu1rdwLRycB9wNIWkZ_i6f4Ev2pz4lzvJRMZ50Wqxl_kA",
"ADJDZHYBlOsrL9h5-qfQpxNJnw5hR0iFBsInMVivxAfaF16MNKw9poKVXcrmegmNnStu66YJnIYSwZys4BjekBAykVpL8cq_cA",
"ADJDZHYl2ZTk4P2W_PQPA4xwUxpWZxgg1wMX0PZG5OkGxlwK3BnjBR2eWa4vY5f8oh9ATXHLMqBCw5u-5JODt90b5T5flnrbCQ",
"ADJDZHZrfjWudn7bh9ICtVt7dLO7lV3oHEdMWctCyS61ELixOvRFoWv5XFgRU8K20pB2p9IOAdbPJl-0z1D8xmeuG_m9iacfxA",
"ADJDZHYn-_4Uhxftq2MpUmBL_oZayLkgxWJ5UkcZE_j0p6QBMHE63G08u4hVFC2VJw_Y_vr_xY_P0J_G4fXpRP_zpZ0fy6rU7A",
"ADJDZHZKKswXyczx0QHCsWnaR0s1i3XIc0iqLUOBqKZA2Z03uUDtXpABZh6tgIy-vOgQUAyiX9D9i-dcAUtbQRvRatSEaDOufQ",
"ADJDZHaEjEwXAf39Bf0XiWmGxBrc8oWvI_Hia0RAqqSePSU8LLm3ZnBOY4s7magFr5AeMqxt6eVwpGXh0E41YXYQHTkaifLVaA",
"ADJDZHbwojOK3HnjShqVPi3mVjkQNN-5y5hZDdhyXf09lMUgws67WFCq61ZDKVIizw4gSyATPVlISgIcCcRXqNfpoz0LYwjbLg",
"ADJDZHadKHYkrVIjQQhsMkF2Ptv6jw8CuStPa0KpoAr_WpdQ0aZePdNN1v0K-sT778ZJLOlD1aARaDlXUrKZQCqFUUzLyxzYmg",
"ADJDZHYgM1d9VLqsif06yZUbCwIe8zgFw-XHMU0kjv5wFzPioc7-moIIA9PPsBGDoQz-rJGiqltN8ZzTtD6ohGSoEr_49bXH_Q",
"ADJDZHZGHmJVn7MKBT0tsLXX5h5ldu2Kp7bmYsEBO3FWc3yjgHRNdvbdgqCAvhDwfBTd8oU89LeZWsa0ek3gXmO1XbnVLPS7Cg",
"ADJDZHa4wYbDdbfLh8UVwtPJmUlz1jnDG6wEsGY6ppwBRxgHcJbXY1wFnOH3vH8UD-TtWQ0Y-KyA22s4h27rQdYbk9dcJYzGFA",
"ADJDZHb_rx_AIA4GxJ84bcWg-MIs0-8QSN5dmoyTuWegICaWqf41nMIkXIiw_PdEzjggyToicN2Tjjgr9hXyX_zqWWJZVPMDYw",
"ADJDZHZeU6w7cLOZydApWLcm4E2JNuJJC52Oth5LuWXB0pFF26i73YB7V2TjQdm4z0P7D6IY1LO5nOVCUPiohVe77xD6DeeBTw",
"ADJDZHYjRcpEvvG0ePbBIewiPcy09olDn_jhs3a60oM-OEQv9i0cTW8sxFGRm_P4JNCASKMbqIwzSpgU2uWCm8TxUYXHLyGLyg",
"ADJDZHaxwpYASKFQpS6t9PjEuC5eVD5y9n_4kn1cIf_q6c4xucgVv3uEEbMjHoxl26uOGrzdR1MqPZkTVZt8I7lpNqCyxqYamg",
"ADJDZHZVPKzDeUJPLoAHXyx70_AAT7zaAbXshbPDpSZ-UVdR0eMpgctu2ePt8HI42YAN2leHSoaUGyMRcUUbVi7bbz63v12N8A",
"ADJDZHaDlviUlsx81HzDZbv_w3Fu37AFJTjT0N46ehA_4K6EY9_8SFTGIPNDbMhJ5eKQ2KAqO9ZT3IpDB60W0L_w5U8JrdXDgA",
"ADJDZHaCRhnwGsDuMpvgmimiHuNMSOGu8HUaaP25cRvdvEh-DQrdoQ6kFc6f8SSLzp-xC1z_5540VLi538qjQYnAtkDo18seKw",
"ADJDZHZLrxKU3Fso-kQzV5ROJHdwtXH4d5Fe8YijXgxQsL-0I5xsqz3O_9AQ3RmQ8nEvK7CPiyIvMzTXRZsSyrYaHfLwo31qng",
"ADJDZHai1lMKUtPR5dgkZwTH5Yccln0oMNOCOOVmEsGXeQDOLAlCxg3qp3F4e8b9uxOmfyu7HKwQ14nJ14n7R_BupdMGvoTbFQ",
"ADJDZHaOUrDgwmgQPTgVhAcRsD2zphSucI6b4L-vros8iZutIr3hpp6hecaqKw9pRwBe0kl02xAzCci0qMcKyw4jhhF3A_kgcA",
"ADJDZHatyQkTIQbS64CnljDwl-a9iqmwmLYERvUD0Lxl2m68sGle4R-lYXiKc6ZslPlXF1eJtcPj7tpOW0IpxIrTKHWb0R838A",
"ADJDZHa1h3oq_c3kjRKBh6dvEuJfW5wRtgoQu1rjB3TqEuTNySrG3D6e7hmH3waJTzAsayEWdxIWq3_YTZCc2Bl-sFy4LJPP9w",
"ADJDZHbhhtk9dYVKu46unWeoLaiFvfhQAY1oEx4ouBs9_-jMFJqfxktiT-Jzgo2roVoxYCQgKNVx4jFJzMDU4dOVQH66UNtqFg",
"ADJDZHaUN3V9PMmt_fw_f91whohvDpsBDipMaNqpIO10__tAMH1_bGgs6hV26_WSqBCmkgRrtqEdCBJ0mJxBk5jlSWowZB0VaA",
"ADJDZHYOlEorrgM3kReUiO_qiWD3iLWW3IYuu4kfSX-X3hhP5dwdGEvgJGYfPJ9YXtKp1CKi-8pktdzNOXyiYGZDHK3LvUFfng",
"ADJDZHZZTub7nmCQXpVkr7Nys8XI0h48S9D3F_gYycqpLle2laEo7d--FFwHGBH56WE2SECvbjewDLN4RHhyXekypdVUliuuVg",
"ADJDZHZ6vZvvziY6WyX0yq44qnWaO6yz2rfFXTaQWD0mxcfy817nuy685isMu7UWXwLVMFPSPDA0fBjPydZvhkmqRHI9--eqeg",
"ADJDZHZJLSVz8aSmD1CMN-YQzzh7P3Ei6jZosYpS01TLS1ha6_JCim5VPWiHZEckPiQqV4gmTYPwuqMs5sD6YdBT8Osuq1bELg",
"ADJDZHZIPxU-LAKkuVtOU5N8HCZ7O9GqeZ-9VYLQxaCt_27THSAA61XCXOrl8_MTE_Qi1koPIrxTBPkh8txrCP4oppxRwh555w",
"ADJDZHbonMCSd0TW3188eelNHrxcmSpdWUIfK_CXD76MfLxiAONlPSS0l_M2PhhIVLQAOcoql4_P9tTAJvYgCgAgLzv6xD0DpQ",
"ADJDZHZe15Pte2gA3sulVSc3I1yUkTDlMAHiUZKbHAch_LRm0LUHpi_B9ugB8tKP4tH7YcxHqj6aFPqLvsQVmGwT3LebKF4u5w",
"ADJDZHbvjFTJF_-6SR7YsiJ1YMBYNIyvy7igw38aEddu9wHt9hWvoRx3A3cnwBhUgg0cf0hoNnrtGrseMe_sD3XcSC2C6aObmA",
"ADJDZHaGbOEZwsGM2dHt3ysY0qyUELwoH8tdAXPsMOhOCywFQ7BZn_hasGeu1r3sTD5rR5yO_8bLviY_93p4MgpkU9nEHYVMSA",
"ADJDZHbhm5MpyL87Y--2euAGJqfkP6d2iwPc_B3wWQKSCpLiMP0jQhyUUni8R2lZMjyuwxa4RcMOhYJ5zEu47pMpwaoX-p7zhQ",
"ADJDZHbUWaUArWxXt1DF8QqugqUIqtQR1860zA1VK5H2MNQ8LMdQzh4ZocEBP34TtUS91aQCCPl_K7Q3MdKg_F8iVn-eKHxHqg",
"ADJDZHZxaj_Y_G1T1f7tW6VM4pHcEKdxEoPZbt4NK8T3tRrITsAVO4dk3vnkvQGw3SA4YnpS3LbO8Qjb1i77e9tx-748z2D2_w",
"ADJDZHa1kR7z9m-RAMb7yPD3nmGdMEIp0U6-SVfcow8EakMcnqB5I8hK_yUMPSZzNcK7VD1QOVlqzeZWFjBZQKiM9DfnI7QWag",
"ADJDZHZipFNvO3kfNCXv83M9qBxO5yNJ_Y7Dy7-jny8NCfNFDS6sBRvMWFd4RTU0ovihvYCiIi5FWcx2zqQg8nx_FZGpA_GL4w",
"ADJDZHZ4dOzN61T9sn2BHC5Uw8GCCms6yumpxmaP_cILfjmSjp36L5l0bSTjZbiPEyYtBXDFkJAKaD5J2FxE3WbUa5OkWlz63w",
"ADJDZHY0LLAOGLH553n9hwTrnhvLjTzrg9R85diYmA8jDVPfdpaVesexWl4FQUzn3eCs28n6iMCh2JEUhvVPudwd_Cw-9V5MKQ",
"ADJDZHYiUILOOnuVa8chWqM8qpQGcr89vePr3uHqgxrqAbzFRO5x1tjyX60UQ8d8sk-SzZJeBiKaDvS_UA_S1LsmNzL1J-uutg",
"ADJDZHZ61FijJpW6_TgfSeQF7qKUFcBJg6OTIOq4kqLlGNB39EIt0CPiPjPRbTsEVQ2vfOxY0y0XKHItGPNm3wzNxmCG_0iXzA",
"ADJDZHbTiOH4ebQvSTG9URbZCo3tFCRN_zkxby0KPUHWJwNcY1D3no_yThb5zGMqmQ0K27qUNAzOCY8-3GEXZoIWNf5bHyt_Ug",
"ADJDZHZR0fw84sc2PpnODr7Rgukq2Q6nmqfBO7XTep3RmRXPoQD64KGFJ95HRQy3ZuVEqMybQTTH45f4CdRgh_BuO1kUnJrYKw",
"ADJDZHYCx0D9UZF9Nu9nWNiqA6HVJkhUFMryv8iRxx6MzPmApHjwebwD_nimvzBjGSirx27j5mTnsiQYyDsn0jDjs_KgEYdQ_g",
"ADJDZHbXniDto1l7gv5eOgBJHj8Nq6_qLotLttaSR0fULdv2RXF9nRwWUewT_NZZ6tw2Hn5qfFTcrrVgRLVrsNzqVu-w4Ar-rw",
"ADJDZHaNfHxutmtp1RzjS5x_03Jw521s652QGQsHMMZlRp_TfGpTuOJJjgoBZUCXPAOmNAOYq_d7nuCt4i_1ybez7peAiY12vg",
"ADJDZHayuCbCiLXWC01tXjG0ZW1xO0KKl8EczpMlNPXs3fqb9ku7xFgDOc7wtzb2i9zkFxBzxR3IgJBQ93L1DHIFOwYpbYPWJQ",
"ADJDZHavkOou6dVoqZWmCnUrt5JO8sIBYP0pPIs-QI-3vpkWOluoCqWWwcGcAyHm8efbAJPMn8BpWXJO6WTuQyIjrpEK_FkWRQ",
"ADJDZHY2kVr2QKf0bkmSkoAcQxq8B6qVOQTQI-sI6i_RNc8EJscjQYBQtn2RRXCfSpG09dHLHPVkRkINAN3bjAwgo-W_nEZaUQ",
"ADJDZHacX0ytVawubtD6LetEHbKbz8qtWDVvZK12X_Pf9ZtjSgqyDsYbEn4cLPNABHfItGWpf8mkI1wOej6xsQpM9rqK8Ib_Rg",
"ADJDZHZtBGWQAR5cdyVqaCQKEmkiXsd8PFwk1ug6pWRuJ4M5sxeQXFAqUlvwxqZrrLstRqNMRsHUV3WZN4EPXhBqdT7ydIkrLw",
"ADJDZHagBFfS6RPGApH2Xmt3KogTWWwIf1hYD3a_s7r7FImX5jbsk7x8qsv0-eVuox60cxm6m3E57bAdNCw2c6LS_jnEuqeRUA",
"ADJDZHZD61eWLjH-d_SNjwM6TkmMH-DdaUkFeZOkqHmM4IFREvnYAdDW4RBAhDR6bsJDFaKdL-nbIC_j1bbymaZIwg28OxGvsQ",
"ADJDZHY-VfvY1sRAS1PvLmRTolZPtylTpDihvEmA58qSueq65MYtzjc4XB3YFBNi3IcqWSaXP0nV7H2zskRlUcPiZzawakOqyQ",
"ADJDZHZ53CF0FIBrKtlCA2-5qAFt16jZf-YEmuuHXU7_ICQ043gd3noOB1ezVFeze0YpPhbTW5e8LvfVe-xHkoKYHGLzIIDCyw",
"ADJDZHYYPp9qaeQxlDdOGV1yOvwvRzWrSckdrFmDRdl0tX1DE-FKYCQaFx2mqnBqkmA0PBfI4LF8HiJ63WYYbyyOonTxc9wj6w",
"ADJDZHbo0mjmUl3qLt6U3FL8NgpcpS98KjY-y_to7EyW--y2TH99QLPqQCYP6rGTilPuUwc8BeHWPf9ECoNGDhBYfXGD_C-TjQ",
"ADJDZHb2o1RMtyjMu4feUhtTUiv7wadBce9jhhASR0XNpk34MlFt_lJsMt_x85spaD3T7WlQ5qIArwDC5h4qSjlpkIpMRVM7VQ",
"ADJDZHbD2VCMW0IsArZUiybaseqDY_hpkUzlD9qqhVePvrHUNqlCgGYYUoNC3rJUuBFNIcPFPi1v2WE396tLWPz76AaLYJdhpw",
"ADJDZHYngys6HVWOGjJ_9xnPNriRQOVPjLcjTJ2sn-5kmrMJ6w8uZgGrByc4NhJM-pCvJf8V5zMYMN8tJWPVLCUFvgJU3dQ8FA",
"ADJDZHYKyxyshp_ftCE0XyJsAMf81gbxRWEy9Gc1sKySxmAWgjC4D7CteEsF3oxPdZEAZQGogP0kDMMOXs04DyLhjn9Pfvz9zw",
"ADJDZHZqnzjIkDDcdclsBgfBYxsPKRdRehenU-x9KyWvqY9B89PleVY4Edbz3dpBi4v_eFRHhE-RKViaCxT5hnBFm8HUmNHHSA",
"ADJDZHbehv07VgKWCXp3uSP45a8QPBth3M4fHka5kdTjn0gzUKt-IpmOkZB_C2wqz7KnwjQhP0bS9x0rYXE66Y4t8f6iTRmvtQ",
"ADJDZHbrUQAvqMVdRdfM_oE2GW4zyh7Ysj8abvKPtSSd3MxqktYE2MtDFGEFZ3d03fFedpjVQM2BykAfxUNtaVbTOmKK8JsmVQ",
"ADJDZHZLz6aQasM7ef1D7iz9ydCfSQvxg5zkKTBXJSZrPrOzyZP-wd9-EP5BiAiljSbYUffig9dkc-gGjLlOwDjrPTdYSFkchA",
"ADJDZHZcVodOr6kyWxNaxAqsyrtUV5EpUa1MYqcs5QH-FpUVuyFAo-BNmbtdiE-Rsz1PlMZpc1euG54axh-mEXVq1iNxP3afTw",
"ADJDZHbR2QWw5HjiLkrYp9DvF9F27kHCO6sRwTQzXXitncHJg8EowykPdchfOtNtFdLsMICUgpfXUpZi7SkX2wuuaUxdIndU1Q",
"ADJDZHY3MuoTtVLX5xUE7kbyngvMBkNmfjyCoYMFTP89xqyT-7xj6oOnOAy8sGuOU-VHuIxSNusx1aMmrMV-XxpOGg99vvNBSw",
"ADJDZHYKP8MdEIzne3ZgEu8MX8-jsqt-2rCtKHrxOXbn6sIIHtaf9V00uH2wgpZZZKmQXqWXvSurLjr76hc1xWs-EDFI1F5Zpw",
"ADJDZHZ1bGEY0atcg71LW99_HJel3TOIroG4T7OBJoBQgjE1ujJ72CGN6qzcuFneIPeUY1lB0MTKuyFBDJ3XhQNF2Cz64LKi4A",
"ADJDZHa3ch92o_79SfAeDbLApygiCLrY6ostQ51Kb71Rxkqb-YSGVG0G_CAx2U05dYFACB63q5f7dlAyECQbKZQjIaiIcgA3rA",
"ADJDZHY3DUFLFLlDzWvcQPfPI8siJrmnYPGsOuHkHQN2bv0H3gW3Y2Yd0pP5WHi5MisTmxqKrWrgO1hp3J7wI0EKL2uPAu3zUg",
"ADJDZHZEt12e-Mhgn4QCBPyZ17YtGmxDvx9DXtD1AKmUdQ2c5MT-it8X3TzlmfP5v6FotGV2Ydmmdx34llKyFsuRVDuBxtns3w",
"ADJDZHZt_Y5OO71Woj-l6-fNDwwiJUAdeitEdva6fAHhgWuPUwChh52UHv59l5z-8355DaNvP_0gZeNpPN3R6supSTBFXwHByw",
"ADJDZHa7V0jOIfwackrpsKZAhT00XBRzg-ssU1i_mFrkaSVM39xC7tXawt85ro3t3mvhttyvRy7t0BB3JYty2DSQiy3EPeY9dg",
"ADJDZHYwC-i4EPKrViKxw8_dO9XjbZgstGwXuEQXikebR0n9SLPKNtDsKaDBeVGS5JjIzNqPbairfth2gF4rWcK89FZ7MiuZ3A",
"ADJDZHbkFVXms6gXqyVC6FGac4br6jjhSvg8QN8FJQPq5fALpQq726UGaNTKBE90YUr-tet7RWvu3jElK64QpnvD6Zquk_NkvA",
"ADJDZHYDxb-P2UTk-sFM9LHsAVmUPe9dspNbvpV0kVWazsysCecCJoCtjjb4NTzBLhfg50dmaEgD0BdDYeCRZWINLnvt67mkgA",
"ADJDZHamCIeig3yFw7tGHMs9EERPRLYPvjnzKxWBgsDq0egKqLnt52ya0MiWQZCtg1E7lDr4FCZjC0xfWceI1sMpFA0CRuIqmw",
"ADJDZHYhqfe4iqhgsUmCc39c1PKVoLWd2_yOGmh98PhZKpWlNsUUMRUV2nnX8AKmCnBQ8zgV8zVlmLtPprA1z8VrHEdmymhrZA",
"ADJDZHa9YxxR_AZmiAM_rAJJqe6b5JiJqIJlK-z-Y2M60-RwQC-xXJc2a9R55Th8-cmJ5P1nuw7-QWX02qg4EZBUnzhNTZxthw",
"ADJDZHZj4I7kHL_sIBs8WcMQN_CDPTrfy3rLk1nKaMn0iz3hm3D7uH3OeiBLrmV9ayetPcXbyHQ96HcZUl_8LcQCyXyqPwgxmQ",
"ADJDZHapSAK5NZFkYs96wKZD-02YC43eFRod4qNwMHe8EOHXkmpe3nc2WF-JS3xaJ76gEtiSSftgZ2WpfkXBTQcF1KXNahURvw",
"ADJDZHZKs5eVS1ohzadpGyqWxy7n9pJYx5cVUXL8oO9i4r_UDp7n7vtKhZQFQFlaeu1um2wbygACMGvQ4bQqtAfNhiO6XS06Vw",
"ADJDZHag_pOmv-AKWOPTk2pvDomUETEkSDzfPCM3FGjJY1GELJDh9ucgmv65vyLTu6HaIwD9xZq6lrZqnDsX8mZnHSh2oxudsQ",
"ADJDZHZOIlewrGP5KKzx4Fo_DU_vqyRyEyq-wY-61edFoNxto_nB2ndm8C0bgkiMvKgXAvXnhPySgID-0N_USDEffnd_5MMG9Q",
"ADJDZHbPRsQgNY-AodW1LeY4g4wcG1rR3dYQGLx8kfCQueWYMt5VjfNURQjTNkFJYwLQAkFPzNSa9-Um159ZsIDY4l2WuPSr1w",
"ADJDZHZ3_TkqFmEvsb5Pleedun3D4rRV5LpF0ZaJDYS0L6cAOjz3rbJllb1WharNAM9PuMY6fwvwffXXcLLBGFi7IR1nSHt95A",
"ADJDZHa7iJrT30uPuobf2bSiUftAhqvvDDX74ZDkllaZ06MEvtI6Q9r9-70oYeW0k0cdyRqT_dliU1sEd-dNW51TUMGhJWGwQQ",
"ADJDZHazqRpfNqteuRE6UmR_r10q7xBmV1xXSDbQdvH9Q1M2wtwDgOttkbcxSzF-37NSdYN66rs0Q0rlsAC5sKaPktyTB1tE3w",
"ADJDZHbYV0oq8-S0W64EDHtQU-HIySj6gxmVlSxKrm_d0UQXhJh7VM3xk91Ayl2dIQc32Px50M1MPZgFF164nCb9R2Ti1uUenw",
"ADJDZHaTXSClZWSTECvrEFLBAc5icDkJb-AdGi1yERtQr-Ml4B9vmRFjMdhIX3CPBzld_3HdAZZjFrChf7jHp9j4bjCvlNBpeg",
"ADJDZHbhU3QzJJbfoNIe-ygkEmADaz84WbJAEXHnpgDFZL6pppo3XCIkIiY1DcY_sLgEaAmCZvWK8Jy52VgJNjBXD-S_MCJOEQ",
"ADJDZHbvlUpzvWFyZLWi06t6T4kTXWm_L8PHpqDZKIqPv5R_pTdkWs4x8tjkddZRQhrWvGCUjirePS93FjFci8j2ctHBhe6-VQ",
"ADJDZHZ5UVpRgW5EALhzXtZf2Rct4QZUIz8YfS5YsfWsEQsfg9lrXKVI5YDLBKahS-McuwvOAZT8nOcoMHWNrI07tI6PS4Sn4A",
"ADJDZHb3GZwRD9iO0fbHY4kjDItOwkgSFbk72pRfXVizXi-P_Slfiiznf0O6p2SABFDoGpUGnGgvgd4WoPucMxen7ZhLMVmuWw",
"ADJDZHY7duTfPXfek6FchBS6na0c7u27qLW78uF8wMR-D35n9TTIXvqwi4-dqzB2xZd9Ssa0v_70iEP4zns78rf6XpS-8YY4hA",
"ADJDZHbw9WGs_nunvGkOYUaeQCqsrQBtO3Xu0_uLe6F2eUpUWLOuPRBXAvvkokjwz-vSEfXfpRsw7PjcFNp5pk8Osyx30Bkn4g",
"ADJDZHb59WXy6_ZscWPbwHDWQED3mU79DrdjotEflKX_c3Lv9xXiZmcJhdYFsK5ZasQYz294FXhEliS4I8OyYSR_9dv0X243RA",
"ADJDZHblBFGoSEFY8imMjQ_jEo0ugm33CZgO5u4qGvnVsHMEWC_8TWGgRGTpv_0COtly6qdxGbUrgWdbQcrhKy5Mw9U_COC_Wg",
"ADJDZHbabfRiubUbTNf0HtZWoni7uayrSsmkJYkAVUS6HQbZCOPab0ouTT17CpY4nhOZnoW_vxSJ2Q6vv65k49f4c-qbgLZ1ag",
"ADJDZHY3kmLyIrJNmkc14PRZWoyPMIsYq1xg5-owKmDT9YyfZSZMAPYekRZ7SyR9qqXy9057XUOrlWn9bQMvDWAMHwCu5ij8CA",
"ADJDZHY69A9eiUa5dk6RdvEITxEQnPeiQDYoTUP2FRUXLgJyE0xjML5EteTrzr0ZF3G6l6qqpClYc-UEvaDQHztaJtewiyXw-w",
"ADJDZHaQQzfiaBYvhWuxAa9WQP_7t38IuQeXUpi4Hcx7AIRlbLjXGaP_EGq35TbP8j618wpkG5d2a7ZYEUxiRGIFntTRndjS-A",
"ADJDZHY6-fG1hWzuSokJ683ELib7_Ab20LqyGJgxLWM30xr-9sf2mwyYc_S930RcTzIsRc5cxwWcToo2xbey6N8eSzlNbMnj3g",
"ADJDZHZ4_3a5Rb7-0C11whq4DrtBcL6JCK6XMIgqj9yCQv6GopkCsc7yesrmVVdydBayO6PazTyGlqrHMOPUaOfS2XGeNbLYQQ",
"ADJDZHalv_i007AWMoHaUxzEbiatZAjk420WZ9Qq-qsb5RA8Vj2-xQFh5K1Z0lDJ5WX5xuCBYRC1wai5cbLF8R583SENe6ioEQ",
"ADJDZHYBBRt5xuTfZ9bPSThdiGp7OLjST5uquP32fSKQZOCHRVOTBn846uh-x4QinsA16bUUgFc22u5Qeh1S9DwhXrejCHS2Og",
"ADJDZHYx6HPbaPq3ejOSbLZoXC_OqppshfhtYkcZL-EkoJf0ZD9LNl5HGdJCTvPdX1uVhqGdLuRX8fcGEAVqQm_4IfpVwLZfAg",
"ADJDZHZ170xuEq8B8jvpMqxLrfBg83CxCUmwe4vYytj7gBw7BgUMuzD07KhQJ93pZ_wz2J3Yu8lyhEuE_n4bU_eFmVKwsHdNYQ",
"ADJDZHZf8bIY3JRdV1zABngZWVw1LEsYBa1z5jecAUqXn_sHpQtwvdD0edEh60FA-pwgkBtgaNyPruxpwqdh3YO66A8ICAFR7g",
"ADJDZHZH5iJT29dH7EzN4Fdr4t3QQoIWQQNgvPfml61kr05tyGI9wYAog0gCQWV2JqPuE2E8dFHuJ3ADj3T9L2nOssZQPHg_Jg",
"ADJDZHbJ3wFay1VhAIfls92BTP48YNf57z9TnUw231Y2Hucwx5Uu0fCngGaeHVJfyOvWPogzYpG5t1_rl8gBl7OyutNyMGF4SA",
"ADJDZHaQJ4qCdNcdcKhL2HlDTrMjS8z24Jd54oGQVe_rRmCeJx6D1mM-vT1JGBkHQ8vq3MDSlS-vfFYYqpOAOgQs-f-Q48hYSg",
"ADJDZHbrFTG9t-SNNuvTy00e0oeYx4w1u53cHRk1cn_WL9yQlKvf1JTEljKmjCyPBbEY547KE7rOlfcleNv5vosYtFJsvVSqmA",
"ADJDZHb_zC6cI_cmfqdVasfLzTdfg_fYq5S6kA212C-e1zzoTCQYXN8B_YrNhBVbecJQ2GTi9nTa1b5UKxoae0s3TlxJLF805Q",
"ADJDZHbAo-Y98Dah5m2P799zK_jj3sYMMqWWPrqF80H91nmo1GWiteYPm_0DW06vdLzqKcVD3eB_KWadkgDvuvb7umLrDUGzPA",
"ADJDZHYlpKpi-StelI0vco25G6mOpZlpXqCxv40XalhoXX3GDCLaBCSc1WzxbaaSCG0px4SZbYoEdGtl8vMagAi6uM2gSVmlzw",
"ADJDZHYTSxHa5X_6ubyOqIbXFpieDcXjrXvc4Ye5S4FBWUhjLrw9FIYiI06kbnkxtsf85dMzgEYrqohvAYV1RMf2WPoRJOJqEQ",
"ADJDZHaS2rxq_xsAMgqPhfiwmf-DwCZ-Ln465ricAB-dUsWsHEbvieKkpfLx44ymnQyb_IbMvPM6iRHL5xSe4DCj610KD1scpw",
"ADJDZHaMmZ5QKK-fZqn6QIyl1eVcH3zsW2Ykd1ds3oXna32b8470Jb6Ij2TjzTjNPZoQWMrfdQSgVCxV13CFYvLPS_YlhJx39g",
"ADJDZHaXR5xkORfV8bA22O9J9JFk004tr_drSYAyr_cH7Qykne-H0KPLo9iIpZwL2O8N0DUELCCstTyQrjmJYiJgxmRYjST4Mg",
"ADJDZHbO3D763Pnibf_CFGYcxyJaGgHvSehWbEh6JdwTaaLd95ZIsFW46rX-e_JQv_pPj44OKTTRI9gFCTxdFpMFFBKMeoQwEQ",
"ADJDZHaXCXupSLhR7S6e4fDhLFMYQOcgt569b9JOxx_aKa7N2K-AndKo9UimZHUV_ZLaDenCvVQj-SmBJpqYMPT6ThiAJtylaQ",
"ADJDZHYHFNXl22IZDwueby0oMZ5X3AOcDnjJGyoo0fAb9VGZs-qvYAgpcZXuVhXj5ev06hGyjlNKH9FWZFag2IIM2fcCqD1POg",
"ADJDZHaY3gx2_3lsUPhB7xxOuE38MQTtwaZkaApn7uNrrjP9pTNde3q0ivcNT7wB6AVuDnvNQZkVJBr9NLTGKLR5N88wnlKuyg",
"ADJDZHYelfku8hTX8R4EVloL2jwARIOWpc2CLK2s7zuBUqzjsZDDY_nshBzdo_vRl_KTvB0MB59usCc0XEVmnwrMPLX0Res7gw",
"ADJDZHY3isX5QWLk4qukrWuzwWENPw6e2KU_XG-_mrotJvIYTOOfmwhOuFhk4EZrpJoxjsNpVexBLqUDwwKJhAsH0hc_7mHqxw",
"ADJDZHaDiejHLsFezOFmORdMg983LfsXOYt9SQcc30Pvq_V3PmiLvdzE0ViHYYjgd34v30CVnKYv_WrUzQ6ZxGQ7pQyd6ERB_g",
"ADJDZHYz4iYPO65kQYt2s3g4YUV0Ab3jm-2_IXvfGXh078_wP7JyDnDkpp5FB2mWUOGHUPrFiAM731IhdSx9l1G28JcrGUnP5w",
"ADJDZHa3kPIBHc0XBWc10pjGMNvF6_BO-1lUqQSBNgaQWkXStneUxvTsMUgSgAH30l_7PR02kskOGG91Kc7QqBYMQNDqdxpAPQ",
"ADJDZHbR_W283Zz-Xs93_8Wdbr1hT8wcxiwudRg52SQZAiy73eQ7LJSicWslqwJc0Q0xgiAT0mm8HvYzAuVfBetqrDmGc8fQlQ",
"ADJDZHbHbj-f9CKh9qUZ8iw3NPs3znwPW9SMYqvO5_d4CiVle4Hbi77XsoFm5_PgbKFJxRDOvOqzmC7mfPr5sZgeYLi49KOSeA",
"ADJDZHaVObxYItsoGDITNEF2A7KeSnEubwzOj-hpwSY1phd_tZS7HLhZiMMT9sNMKRc4owaqb6z2NoYMG5bhWbqPbGRsm0ezCA",
"ADJDZHYUavbM_JcWrqINQYDUHWUXovegbvaKntgXjSohUcxQTXHkb9YgofDjA8Q2qfx5HOLnYYgOr0PlWoXNTZfMWt_eGiXMEQ",
"ADJDZHYyDyZjygP6At9Zz1tGRByKyDiIUvuU6WB6Hmb45XFZ6fbyUm_YpzP6LW08I7PgByHU71WJbro6Bslq5ca1d1K13zT1zw",
"ADJDZHZG7e4i6XLVJLgx6KnMGQLg624b-F4srrUop6bZ0_GJqoqVtfKxD_G-gs_njYT8I3ZK_Rg7H33Rl-BykN75vXKcjScazw",
"ADJDZHb9dMYI_gQI_NCln_ECGU_nBR1AtZLPuxnE0hp9i1iYDA-JqUcVYETvuIKG8jpp-R1PFsY79nSQJ1AS2Kzmnfhjv_ppiA",
"ADJDZHYVU2SzV4OtyndddU5HHWh99k94oUeMMmEy9zkseVEmjkKqkcMbh5uByimT1eKf6-6yDHy8xaNRyyKQJWzwD-6ZRpxogQ",
"ADJDZHbRk5A1_YmuRjY1YQrClVKJDPamUnhG4qLkAA_bwM4VR_Ll3O4TbXUORtd4dGkeXY2twYk0KIfhs2lAGWO5rLCHGRDYEQ",
"ADJDZHa0VLyB9I7U5mS1z4AMawoEWldaAKbdNcstB-Ec7ipvDoZ2ETUJSY9rNUbU8FWHlT3MfPv-dUyPghUG9thYaC4rFOS3QQ",
"ADJDZHb6mIjuNWBygLPpnjdWKzMZywg2YHFeO6y3t0mSW3pPwXDAtbv-B5s4gWwzhfoUAvvmgHTuiwhjUoj77o9J1OYnXyO6Cg",
"ADJDZHZbPLgqNzOLiVG6QSSCMTMMAr75pF-JpYtezJ4BzRobp199rRI0nUVC3lMKE8buWovSLowwktL0UQHeFpKf6FVdkVaIFw",
"ADJDZHZ72auCxIiY-bpoMgAB_gX4vXnRapzpGWb9FqnuW2nR7SGZAeg9gOQhbZwmLzKcFrDOPVNhAPgcdFvO5-W2gRMa9zSldQ",
"ADJDZHYL0raWpiq-GIpdFjFwV6FFfCMFU9Q1PQevGjp-gmfH15wwMuc0b7EN_sw8w0HKDY2FQJm-8exrpV0AuwSMEd_SK_p4gg",
"ADJDZHZ8h6EIsdT1EORrcWr8I61q7Ayj22-5ZCB4aRcQLwV54WZpASiFFk4ETaNTfkBqZpQtv6pvX_qVxg-9m8o5HCBQIeaqaA",
"ADJDZHYkzHKI9rWg5rdeXTIwSx_2K_2LtZfVbhpmL-dfDteCnt7a9SAkTAeuOzw22Sur5YRS3BuES--a7JeykycrjPdIKykGqQ",
"ADJDZHZ1WMQBXtE3l4fO0-VODe3qNzy3-t89E394N0Sab0n56cSJkFSl7ifBSfeFX2VxRAsxKxcHmc-hqfOMJN1HHbZ8cDt2XA",
"ADJDZHZfMzEvZVqop6UQ56v90Dt-scwR9bUd_PtoXxkNmEBHRcSMVjzyiUlkh23UzsYw3zP6aYJmqA_shlkIjGrVhpWajCIAyg",
"ADJDZHZLpwd_8vm9Inj327af4Kyd38qxF6LnaB0Lyq7UyJ6HxFBhpE9AwFQJx--9enR-WMCu0lNpbdK41jAbimJK4FMtCCC7Mg",
"ADJDZHaofl9DpscLZvH4uO-g0NXvRqVUX7V214QDSCZaGLgEW-WH1yBVRMPDMX4sV54nDsB4Dy3EteOpzptxrRRK0QSsrZ532Q",
"ADJDZHY5EKCdaj5H-z7cuJXONSCyfhYA9pphBRfttdKcWa9_aYVqQJqdUxIugA_0tiVO2ZEhHOdCPGYr2sRfMTZlaZgACeOVLA",
"ADJDZHYoD3zIpgFl6ybxSrmfffJBmyOebO9YrvdfLXpLH2ebz_6RCKPnCv2ZHElrshUpMNLWozPYRj89E4Hr27oNyQDUBL7xOQ",
"ADJDZHbSv_8cyskOBKkizILXLceoQpI351ci1QXKhyqh_EU8ev4nGx6Aqw6jqpQnREOvQpSfLUWqehuBjlJqNZa9LoruxdSKNA",
"ADJDZHbr7MSp0aVommA3g9Pd0ywSH18T52mgfHwUKL-FdeABSYVHJa6O6axjN_mBH_KCDlBoWH_LiuvzcpTm4zAh51skz5KjTg",
"ADJDZHZ9EVtofdz1hmkMuZGA86Pl0pZYX2057r3MsQYGZPQQDKv6G43sicmxIIDTYd4sr68wStcRqd6u5t-7Brkm4Vk1fdLXvg",
"ADJDZHZLJw3V7_zlxcG-8MQGNB6ec1KdnjHVoNVH-YrVy9_h8o8mreQOA_EkfsvaiH8dyEKh0rIkggYVpL0lrE-YzVksNfwmzQ",
"ADJDZHaKD_4lkfTOEiLulzdPh9L5PWE6_kzjuMfa9bSF4-dXNeIaGlUpq2ycqDRjidIUqFSdYVQqZ4Fw78BpS5QgoBSHOQVZ5w",
"ADJDZHZ3o2ho7caD8OfCECVWzN62HZLj7K0Gr2fnsUJY5uOPgHP7x1pSg1d5TQpUFX9m2g48qMn9-mwVQVE4Vy4W1GYo0rKM0w",
"ADJDZHbycdJ4lfOBzNGzZWd-zU6C00XVLBFJa9Jd5s45iduuZyiGwfVqo4bc9xlc31hEFiOlxuP--fxBO8pE0icD4nela6xSPg",
"ADJDZHZJ3ULY64EWSMX1oEVCew6rB_J3OvjTgo5L0hj_VGeunGzTKTlSpMM2ElR-dai5kGqGHkRhFKyQqh5_SyqKfqzMAUs27w",
"ADJDZHaMNy9f3qyK9WIB7lAO2wVRxR58_YOLHFny9uZhOdOaM1kNBJDlfLFUinlFj4e7q50TDDiUv0UNFdRvlMg_ipf8wOOfiQ",
"ADJDZHb-muEuS_iordHFCfQxjaj7grX7GYtcv_JvMWJkTUeoNDQz2aC__v1n5Y06iVc2MppVByK2eYjI1PmWpaWnmq0xujp0Kw",
"ADJDZHaFWR79fhVZKfSMI-3zYx-4HRsWFg2S-Ec7-K4FaC4IyIIAznl9Ogyspvs50vowavxurFDjz9g2aDGQMRjnje9O6dyx1g",
"ADJDZHYgnvp-8uvRbDtsCwbpek3gM1gX0sDnUhfnTOfnhNJ-V06gkiIhXHCGt2vh9sU2uPAd9J8PpIS21lvqIqPGtabjifnWxQ",
"ADJDZHbprGvQatb0L8KSuRRx_A5-hrnpp62Yq4AnMEs2AJhUXDezWqfA7-6kafhMA6dtaLEkycRQ-OAR6e7F0NzkZSyQOH0acA",
"ADJDZHY_8CItrIBompcXkhWNFgxQuihS0gg4rntAclsap9AUG-_06qjnXXQrSVE99-Fz7NKvawrssSolp7za1Ra7Kpqz3y1VVw",
"ADJDZHZaZ40Q40dKt6R65Bp89Fdut4b-8hNMFw8HvcKWi9mqDUsW_vClJRwp35I9SAXSNx6aqYoK4wrOXKtqlQU_I-tys5uLDg",
"ADJDZHbT3FFy6asQHlxJkGgqPNADRen46A1sX1OGzZJESMSHhAF7VKI9GI-l6VZ1PhEI4nT2P2qlOuQcbg5Kh6SKxsJQJ_0SGg",
"ADJDZHZ1hZUpiiXB3s_PUBDTrEk2MvSsOu0CcAbkOupSx5FGYe7VHQ4MShBon5VASsilGhijBI-pafNE_c_5hPhUZHXPtSgNfw",
"ADJDZHYWeHhbtNWhy1uOoDwJuiLOAmLfixogIJz8m2c8EoBSreHhwLdRUpLytL4bkjSgPSrAXuX43Vf9WIXJ7HAp6WTWc9aO9A",
"ADJDZHbmWUzymBHgSmG6UXYimWYxWATJFWQrb55j3LNzfOW--PRAfvss9H2R3qiUFUj2WEsYPJ6N2x-1uNU9hp9IA9YDm5z_Ag",
"ADJDZHZqM4kbO8EjwPaydDQaQBonH-ipDKVpCPdQ3P1S1s9svhlnYRVt9Zcd9cN1SZcxnoDb3tYEBFQvB-GiJwzgluS6JZFimw",
"ADJDZHYnMh0deBkG8MQeXGhf_hAb3Jhii5IPDqqegdbGBAR3he-W83TZBO_MYwUbXM-x4H7NaM1X6W8LsT1G-sGESaQSUZxJig",
"ADJDZHaKTz3CJ0nXQlxZ7W55kMcK0X2QmwBQQq49ji4ga1Q7vU0qPnAaiqlUwmvY3rGhop81P15pZDa0rF8s27VEk2uORc0EFw",

        };
    }
}
