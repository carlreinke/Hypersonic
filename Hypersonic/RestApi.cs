//
// Copyright (C) 2018  Carl Reinke
//
// This file is part of Hypersonic.
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without
// even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// General Public License for more details.
//
// You should have received a copy of the GNU General Public License along with this program.  If
// not, see <https://www.gnu.org/licenses/>.
//
using Hypersonic.Data;
using Hypersonic.Ffmpeg;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders.Physical;
using Microsoft.Net.Http.Headers;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;
using static Hypersonic.Ffmpeg.Helpers;
using static Hypersonic.Helpers;
using static Hypersonic.Subsonic.Helpers;
using IOFile = System.IO.File;

namespace Hypersonic
{
    internal static class RestApi
    {
        private const string _apiVersion = "1.16.0";

        private const int _apiMajorVersion = 1;

        private const int _apiMinorVersion = 16;

        private static readonly object _suffixKey = new object();

        private static readonly object _pathExtensionKey = new object();

        private static readonly object _apiContextKey = new object();

        private static readonly Encoding _encoding = new UTF8Encoding(false);

        private static readonly Lazy<XmlSerializer> _xmlSerializer = new Lazy<XmlSerializer>(() => new XmlSerializer(typeof(Subsonic.Response)));

        private static readonly Lazy<JsonSerializer> _jsonSerializer = new Lazy<JsonSerializer>(() =>
            {
                var serializer = JsonSerializer.Create();
                serializer.ContractResolver = XmlSerializationContractResolver.Instance;
                serializer.Converters.Add(new StringEnumConverter());
#if DEBUG
                serializer.Formatting = Newtonsoft.Json.Formatting.Indented;
#endif
                return serializer;
            });

        internal static bool SmuggleRestApiSuffix(this HttpContext context, string path)
        {
            // Allow the user the specify the preferred transcoded suffix in the server URL since
            // the API doesn't allow for multiple suffixes and some clients only support certain
            // codecs.
            // The user will configure the client with a server like so: http://server:4040/.mp3
            var pathString = new PathString(path);
            foreach (string suffix in new[] { "mp3", "oga", "ogg", "opus" })
            {
                PathString remaining;
                if (context.Request.Path.StartsWithSegments(new PathString("/." + suffix).Add(pathString), out remaining))
                {
                    context.Request.Path = pathString.Add(remaining);
                    context.Items[_suffixKey] = suffix;
                    return true;
                }
            }
            return false;
        }

        internal static async Task HandleRestApiRequestAsync(HttpContext context)
        {
            if (context.Request.Path.Value.Length >= 1 && context.Request.Path.Value.IndexOf('/', 1) > 0)
                goto status404;

            if (context.Request.Method != "GET")
            {
                context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                return;
            }

            // Allow request to specify any file extension.
            {
                string[] splitPath = context.Request.Path.Value.Split('.', 3);

                if (splitPath.Length > 2)
                    goto status404;

                if (splitPath.Length == 2)
                {
                    context.Request.Path = splitPath[0];
                    context.Items[_pathExtensionKey] = splitPath[1];
                }
            }

            try
            {
                context.Items[_apiContextKey] = await CreateApiContextAsync(context).ConfigureAwait(false);

                Func<HttpContext, Task> handleRequestAsync;

                switch (context.Request.Path)
                {
                    // System
                    case "/ping": handleRequestAsync = HandlePingRequestAsync; break;
                    case "/getLicense": handleRequestAsync = HandleGetLicenseRequestAsync; break;

                    // Browsing
                    case "/getMusicFolders": handleRequestAsync = HandleGetMusicFoldersRequestAsync; break;
                    case "/getIndexes": handleRequestAsync = HandleGetIndexesRequestAsync; break;
                    case "/getMusicDirectory": handleRequestAsync = HandleGetMusicDirectoryRequestAsync; break;
                    case "/getGenres": handleRequestAsync = HandleGetGenresRequestAsync; break;
                    case "/getArtists": handleRequestAsync = HandleGetArtistsRequestAsync; break;
                    case "/getArtist": handleRequestAsync = HandleGetArtistRequestAsync; break;
                    case "/getAlbum": handleRequestAsync = HandleGetAlbumRequestAsync; break;
                    case "/getSong": handleRequestAsync = HandleGetSongRequestAsync; break;
                    case "/getVideos": handleRequestAsync = HandleGetVideosRequestAsync; break;
                    case "/getVideoInfo": handleRequestAsync = HandleGetVideoInfoRequestAsync; break;
                    case "/getArtistInfo": handleRequestAsync = HandleGetArtistInfoRequestAsync; break;
                    case "/getArtistInfo2": handleRequestAsync = HandleGetArtistInfo2RequestAsync; break;
                    case "/getAlbumInfo": handleRequestAsync = HandleGetAlbumInfoRequestAsync; break;
                    case "/getAlbum2Info": handleRequestAsync = HandleGetAlbum2InfoRequestAsync; break;
                    case "/getSimilarSongs": handleRequestAsync = HandleGetSimilarSongsRequestAsync; break;
                    case "/getSimilarSongs2": handleRequestAsync = HandleGetSimilarSongs2RequestAsync; break;
                    case "/getTopSongs": handleRequestAsync = HandleGetTopSongsRequestAsync; break;

                    // Album/song lists
                    case "/getAlbumList": handleRequestAsync = HandleGetAlbumListRequestAsync; break;
                    case "/getAlbumList2": handleRequestAsync = HandleGetAlbumList2RequestAsync; break;
                    case "/getRandomSongs": handleRequestAsync = HandleGetRandomSongsRequestAsync; break;
                    case "/getSongsByGenre": handleRequestAsync = HandleGetSongsByGenreRequestAsync; break;
                    case "/getNowPlaying": handleRequestAsync = HandleGetNowPlayingRequestAsync; break;
                    case "/getStarred": handleRequestAsync = HandleGetStarredRequestAsync; break;
                    case "/getStarred2": handleRequestAsync = HandleGetStarred2RequestAsync; break;

                    // Searching
                    case "/search": handleRequestAsync = HandleSearchRequestAsync; break;
                    case "/search2": handleRequestAsync = HandleSearch2RequestAsync; break;
                    case "/search3": handleRequestAsync = HandleSearch3RequestAsync; break;

                    // Playlists
                    case "/getPlaylists": handleRequestAsync = HandleGetPlaylistsRequestAsync; break;
                    case "/getPlaylist": handleRequestAsync = HandleGetPlaylistRequestAsync; break;
                    case "/createPlaylist": handleRequestAsync = HandleCreatePlaylistRequestAsync; break;
                    case "/updatePlaylist": handleRequestAsync = HandleUpdatePlaylistRequestAsync; break;
                    case "/deletePlaylist": handleRequestAsync = HandleDeletePlaylistRequestAsync; break;

                    // Media retrieval
                    case "/stream": handleRequestAsync = HandleStreamRequestAsync; break;
                    case "/download": handleRequestAsync = HandleDownloadRequestAsync; break;
                    case "/hls": handleRequestAsync = HandleHlsRequestAsync; break;
                    case "/getCaptions": handleRequestAsync = HandleGetCaptionsRequestAsync; break;
                    case "/getCoverArt": handleRequestAsync = HandleGetCoverArtRequestAsync; break;
                    case "/getLyrics": handleRequestAsync = HandleGetLyricsRequestAsync; break;
                    case "/getAvatar": handleRequestAsync = HandleGetAvatarRequestAsync; break;

                    // Media annotation
                    case "/star": handleRequestAsync = HandleStarRequestAsync; break;
                    case "/unstar": handleRequestAsync = HandleUnstarRequestAsync; break;
                    case "/setRating": handleRequestAsync = HandleSetRatingRequestAsync; break;
                    case "/scrobble": handleRequestAsync = HandleScrobbleRequestAsync; break;

                    // Sharing
                    case "/getShares": throw RestApiErrorException.UserNotAuthorizedError();
                    case "/createShare": throw RestApiErrorException.UserNotAuthorizedError();
                    case "/updateShare": throw RestApiErrorException.UserNotAuthorizedError();
                    case "/deleteShare": throw RestApiErrorException.UserNotAuthorizedError();

                    // Podcast
                    case "/getPodcasts": throw RestApiErrorException.UserNotAuthorizedError();
                    case "/getNewestPodcasts": throw RestApiErrorException.UserNotAuthorizedError();
                    case "/refreshPodcasts": throw RestApiErrorException.UserNotAuthorizedError();
                    case "/createPodcastChannel": throw RestApiErrorException.UserNotAuthorizedError();
                    case "/deletePodcastChannel": throw RestApiErrorException.UserNotAuthorizedError();
                    case "/deletePodcastEpisode": throw RestApiErrorException.UserNotAuthorizedError();
                    case "/downloadPodcastEpisode": throw RestApiErrorException.UserNotAuthorizedError();

                    // Jukebox
                    case "/jukeboxControl": handleRequestAsync = HandleJukeboxControlRequestAsync; break;

                    // Internet radio
                    case "/getInternetRadioStations": throw RestApiErrorException.UserNotAuthorizedError();
                    case "/createInternetRadioStation": throw RestApiErrorException.UserNotAuthorizedError();
                    case "/updateInternetRadioStation": throw RestApiErrorException.UserNotAuthorizedError();
                    case "/deleteInternetRadioStation": throw RestApiErrorException.UserNotAuthorizedError();

                    // Chat
                    case "/getChatMessages": throw RestApiErrorException.UserNotAuthorizedError();
                    case "/addChatMessage": throw RestApiErrorException.UserNotAuthorizedError();

                    // User management
                    case "/getUser": handleRequestAsync = HandleGetUserRequestAsync; break;
                    case "/getUsers": handleRequestAsync = HandleGetUsersRequestAsync; break;
                    case "/createUser": handleRequestAsync = HandleCreateUserRequestAsync; break;
                    case "/updateUser": handleRequestAsync = HandleUpdateUserRequestAsync; break;
                    case "/deleteUser": handleRequestAsync = HandleDeleteUserRequestAsync; break;
                    case "/changePassword": handleRequestAsync = HandleChangePassword; break;

                    // Bookmarks
                    case "/getBookmarks": throw RestApiErrorException.UserNotAuthorizedError();
                    case "/createBookmark": throw RestApiErrorException.UserNotAuthorizedError();
                    case "/deleteBookmark": throw RestApiErrorException.UserNotAuthorizedError();
                    case "/getPlayQueue": throw RestApiErrorException.UserNotAuthorizedError();
                    case "/savePlayQueue": throw RestApiErrorException.UserNotAuthorizedError();

                    // Media library scanning
                    case "/getScanStatus": handleRequestAsync = HandleGetScanStatusRequestAsync; break;
                    case "/startScan": handleRequestAsync = HandleStartScanRequestAsync; break;

                    default:
                        goto status404;
                }

                await handleRequestAsync(context).ConfigureAwait(false);
                return;
            }
            catch (RestApiErrorException ex)
            {
                await WriteErrorResponseAsync(context, ex.Code, ex.Message).ConfigureAwait(false);
                return;
            }

        status404:
            context.Response.StatusCode = StatusCodes.Status404NotFound;
        }

        private static async Task<ApiContext> CreateApiContextAsync(HttpContext context)
        {
            var apiContext = new ApiContext();

            string format = GetOptionalStringParameterValue(context, "f");
            switch (format)
            {
                case null:
                    break;
                case "xml":
                {
                    apiContext.Format = ResponseFormat.Xml;
                    break;
                }
                case "json":
                {
                    apiContext.Format = ResponseFormat.Json;
                    break;
                }
                case "jsonp":
                {
                    string callback = GetOptionalStringParameterValue(context, "callback");
                    if (string.IsNullOrEmpty(callback))
                        callback = "callback";

                    apiContext.Format = ResponseFormat.JsonPadding;
                    apiContext.FormatCallback = callback;
                    break;
                }
                default:
                    throw RestApiErrorException.GenericError("Unknown response format requested.");
            }

            apiContext.Client = GetRequiredStringParameterValue(context, "c");

            apiContext.Version = GetRequiredStringParameterValue(context, "v");

            if (!TryParseVersion(apiContext.Version, out int majorVersion, out int minorVersion))
            {
                throw RestApiErrorException.GenericError("Invalid value for 'v'.");
            }
            else
            {
                apiContext.MajorVersion = majorVersion;
                apiContext.MinorVersion = minorVersion;
            }

            if (apiContext.MajorVersion < _apiMajorVersion)
                throw RestApiErrorException.ClientMustUpgradeError();
            if (apiContext.MajorVersion > _apiMajorVersion)
                throw RestApiErrorException.ServerMustUpgradeError();
            if (apiContext.MinorVersion > _apiMinorVersion)
                throw RestApiErrorException.ServerMustUpgradeError();

            string username = GetRequiredStringParameterValue(context, "u");
            string password = GetOptionalStringParameterValue(context, "p");
            string passwordToken = GetOptionalStringParameterValue(context, "t");
            string passwordSalt = GetOptionalStringParameterValue(context, "s");

            var dbContext = context.RequestServices.GetRequiredService<MediaInfoContext>();

            var user = await dbContext.Users
                .Where(u => u.Name == username)
                .SingleOrDefaultAsync(context.RequestAborted).ConfigureAwait(false);

            bool passwordIsWrong;

            if (password != null)
            {
                password = HexDecodePassword(password);

                if (passwordToken != null || passwordSalt != null)
                    throw RestApiErrorException.GenericError("Specified values for both 'p' and 't' and/or 's'.");

                string userPassword = user != null ? user.Password : string.Empty;

                passwordIsWrong = !ConstantTimeComparisons.ConstantTimeEquals(password, userPassword);
            }
            else
            {
                if (passwordToken == null)
                    throw RestApiErrorException.RequiredParameterMissingError("t");

                byte[] passwordTokenBytes;
                if (!TryParseHexBytes(passwordToken, out passwordTokenBytes))
                    throw RestApiErrorException.GenericError("Invalid value for 't'.");
                if (passwordTokenBytes.Length != 16)
                    throw RestApiErrorException.GenericError("Invalid value for 't'.");

                if (passwordSalt == null)
                    throw RestApiErrorException.RequiredParameterMissingError("s");

                string userPassword = user != null ? user.Password : string.Empty;

                // This security mechanism is pretty terrible.  It is vulnerable to both
                // timing and replay attacks.

#pragma warning disable CA5351 // Do not use insecure cryptographic algorithm MD5.
                using (var md5 = System.Security.Cryptography.MD5.Create())
#pragma warning restore CA5351 // Do not use insecure cryptographic algorithm MD5.
                {
                    byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(userPassword + passwordSalt));
                    passwordIsWrong = !ConstantTimeComparisons.ConstantTimeEquals(passwordTokenBytes, hash);
                }
            }

            // Check if user exists after checking password to prevent discovery of existing users
            // by timing attack.
            if (user == null || (!user.IsGuest && passwordIsWrong))
                throw RestApiErrorException.WrongUsernameOrPassword();

            apiContext.User = user;

            apiContext.Suffix = context.Items.TryGetValue(_suffixKey, out object suffix) ? (string)suffix : "opus";

            return apiContext;
        }

        private static async Task WriteResponseAsync(HttpContext context, Subsonic.Response response)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            context.Response.SetDate(now);
            context.Response.SetExpires(now);

            var apiContext = (ApiContext)context.Items[_apiContextKey];

            switch (apiContext?.Format ?? ResponseFormat.Xml)
            {
                case ResponseFormat.Xml:
                {
                    context.Response.ContentType = "text/xml; charset=utf-8";

                    using (var writer = new XmlTextWriter(context.Response.Body, _encoding))
                    {
                        _xmlSerializer.Value.Serialize(writer, response);
                    }
                    break;
                }
                case ResponseFormat.Json:
                {
                    context.Response.ContentType = "application/json; charset=utf-8";

                    using (var writer = new StreamWriter(context.Response.Body, _encoding))
                    {
                        await writer.WriteAsync(@"{""subsonic-response"":".AsMemory(), context.RequestAborted).ConfigureAwait(false);
                        _jsonSerializer.Value.Serialize(writer, response);
                        await writer.WriteAsync("}".AsMemory(), context.RequestAborted).ConfigureAwait(false);
                    }
                    break;
                }
                case ResponseFormat.JsonPadding:
                {
                    context.Response.ContentType = "application/javascript; charset=utf-8";

                    using (var writer = new StreamWriter(context.Response.Body, _encoding))
                    {
                        await writer.WriteAsync(apiContext.FormatCallback.AsMemory(), context.RequestAborted).ConfigureAwait(false);
                        await writer.WriteAsync(@"({""subsonic-response"":".AsMemory(), context.RequestAborted).ConfigureAwait(false);
                        _jsonSerializer.Value.Serialize(writer, response);
                        await writer.WriteAsync("});".AsMemory(), context.RequestAborted).ConfigureAwait(false);
                    }
                    break;
                }
                default:
                    throw new InvalidOperationException("Unreachable!");
            }
        }

        private static Task WriteResponseAsync(HttpContext context, Subsonic.ItemChoiceType itemType, object item)
        {
            return WriteResponseAsync(context, new Subsonic.Response()
            {
                status = Subsonic.ResponseStatus.ok,
                version = _apiVersion,
                ItemElementName = itemType,
                Item = item,
            });
        }

        private static Task WriteErrorResponseAsync(HttpContext context, int code, string message)
        {
            return WriteResponseAsync(context, new Subsonic.Response()
            {
                status = Subsonic.ResponseStatus.failed,
                version = _apiVersion,
                ItemElementName = Subsonic.ItemChoiceType.error,
                Item = new Subsonic.Error()
                {
                    code = code,
                    message = message,
                },
            });
        }

        #region System

        private static Task HandlePingRequestAsync(HttpContext context)
        {
            return WriteResponseAsync(context, 0, null);
        }

        private static Task HandleGetLicenseRequestAsync(HttpContext context)
        {
            var license = new Subsonic.License()
            {
                valid = true,
            };

            return WriteResponseAsync(context, Subsonic.ItemChoiceType.license, license);
        }

        #endregion

        #region Browsing

        private static async Task HandleGetMusicFoldersRequestAsync(HttpContext context)
        {
            var apiContext = (ApiContext)context.Items[_apiContextKey];
            int apiUserId = apiContext.User.UserId;
            var dbContext = context.RequestServices.GetRequiredService<MediaInfoContext>();

            Subsonic.MusicFolders musicFolders = await RestApiQueries.GetMusicFoldersAsync(dbContext, apiUserId, context.RequestAborted).ConfigureAwait(false);

            await WriteResponseAsync(context, Subsonic.ItemChoiceType.musicFolders, musicFolders).ConfigureAwait(false);
        }

        private static async Task HandleGetIndexesRequestAsync(HttpContext context)
        {
            int? musicFolderId = GetOptionalInt32ParameterValue(context, "musicFolderId");
            long? ifModifiedSince = GetOptionalInt64ParameterValue(context, "ifModifiedSince");

            var apiContext = (ApiContext)context.Items[_apiContextKey];
            int apiUserId = apiContext.User.UserId;
            var dbContext = context.RequestServices.GetRequiredService<MediaInfoContext>();

            _ = ifModifiedSince;

            Subsonic.ArtistsID3 artists = await RestApiQueries.GetArtistsAsync(dbContext, apiUserId, musicFolderId, context.RequestAborted).ConfigureAwait(false);

            Subsonic.Indexes indexes = CreateIndexes(artists);

            await WriteResponseAsync(context, Subsonic.ItemChoiceType.indexes, indexes).ConfigureAwait(false);
        }

        private static async Task HandleGetMusicDirectoryRequestAsync(HttpContext context)
        {
            string id = GetRequiredStringParameterValue(context, "id");

            var apiContext = (ApiContext)context.Items[_apiContextKey];
            int apiUserId = apiContext.User.UserId;
            var dbContext = context.RequestServices.GetRequiredService<MediaInfoContext>();

            if (TryParseDirectoryArtistId(id, out int artistId))
            {
                Subsonic.ArtistWithAlbumsID3 artist = await RestApiQueries.GetArtistAsync(dbContext, apiUserId, artistId, context.RequestAborted).ConfigureAwait(false);

                Subsonic.Directory directory = CreateDirectory(artist);

                await WriteResponseAsync(context, Subsonic.ItemChoiceType.directory, directory).ConfigureAwait(false);
            }
            else if (TryParseDirectoryAlbumId(id, out int albumId))
            {
                Subsonic.AlbumWithSongsID3 album = await RestApiQueries.GetAlbumAsync(dbContext, apiUserId, albumId, apiContext.Suffix, context.RequestAborted).ConfigureAwait(false);

                Subsonic.Directory directory = CreateDirectory(album);

                await WriteResponseAsync(context, Subsonic.ItemChoiceType.directory, directory).ConfigureAwait(false);
            }

            throw RestApiErrorException.GenericError($"Invalid value for 'id'.");
        }

        private static async Task HandleGetGenresRequestAsync(HttpContext context)
        {
            var apiContext = (ApiContext)context.Items[_apiContextKey];
            int apiUserId = apiContext.User.UserId;
            var dbContext = context.RequestServices.GetRequiredService<MediaInfoContext>();

            Subsonic.Genres genres = await RestApiQueries.GetGenresAsync(dbContext, apiUserId, context.RequestAborted).ConfigureAwait(false);

            await WriteResponseAsync(context, Subsonic.ItemChoiceType.genres, genres).ConfigureAwait(false);
        }

        private static async Task HandleGetArtistsRequestAsync(HttpContext context)
        {
            int? musicFolderId = GetOptionalInt32ParameterValue(context, "musicFolderId");

            var apiContext = (ApiContext)context.Items[_apiContextKey];
            int apiUserId = apiContext.User.UserId;
            var dbContext = context.RequestServices.GetRequiredService<MediaInfoContext>();

            Subsonic.ArtistsID3 artists = await RestApiQueries.GetArtistsAsync(dbContext, apiUserId, musicFolderId, context.RequestAborted).ConfigureAwait(false);

            await WriteResponseAsync(context, Subsonic.ItemChoiceType.artists, artists).ConfigureAwait(false);
        }

        private static async Task HandleGetArtistRequestAsync(HttpContext context)
        {
            int id = GetRequiredArtistIdParameterValue(context, "id");

            var apiContext = (ApiContext)context.Items[_apiContextKey];
            int apiUserId = apiContext.User.UserId;
            var dbContext = context.RequestServices.GetRequiredService<MediaInfoContext>();

            Subsonic.ArtistWithAlbumsID3 artist = await RestApiQueries.GetArtistAsync(dbContext, apiUserId, id, context.RequestAborted).ConfigureAwait(false);

            await WriteResponseAsync(context, Subsonic.ItemChoiceType.artist, artist).ConfigureAwait(false);
        }

        private static async Task HandleGetAlbumRequestAsync(HttpContext context)
        {
            int id = GetRequiredAlbumIdParameterValue(context, "id");

            var apiContext = (ApiContext)context.Items[_apiContextKey];
            int apiUserId = apiContext.User.UserId;
            var dbContext = context.RequestServices.GetRequiredService<MediaInfoContext>();

            Subsonic.AlbumWithSongsID3 album = await RestApiQueries.GetAlbumAsync(dbContext, apiUserId, id, apiContext.Suffix, context.RequestAborted).ConfigureAwait(false);

            await WriteResponseAsync(context, Subsonic.ItemChoiceType.album, album).ConfigureAwait(false);
        }

        private static async Task HandleGetSongRequestAsync(HttpContext context)
        {
            int id = GetRequiredTrackIdParameterValue(context, "id");

            var apiContext = (ApiContext)context.Items[_apiContextKey];
            int apiUserId = apiContext.User.UserId;
            var dbContext = context.RequestServices.GetRequiredService<MediaInfoContext>();

            Subsonic.Child song = await RestApiQueries.GetSongAsync(dbContext, apiUserId, id, apiContext.Suffix, context.RequestAborted).ConfigureAwait(false);

            await WriteResponseAsync(context, Subsonic.ItemChoiceType.song, song).ConfigureAwait(false);
        }

        private static Task HandleGetVideosRequestAsync(HttpContext context) => throw RestApiErrorException.GenericError("Not implemented.");

        private static Task HandleGetVideoInfoRequestAsync(HttpContext context) => throw RestApiErrorException.GenericError("Not implemented.");

        private static Task HandleGetArtistInfoRequestAsync(HttpContext context)
        {
            string id = GetRequiredStringParameterValue(context, "id");
            int count = GetOptionalInt32ParameterValue(context, "count") ?? 20;
            bool includeNotPresent = GetOptionalBooleanParameterValue(context, "includeNotPresent") ?? false;

            if (!TryParseDirectoryArtistId(id, out _) &&
                !TryParseDirectoryAlbumId(id, out _) &&
                !TryParseTrackId(id, out _))
            {
                throw RestApiErrorException.GenericError($"Invalid value for 'id'.");
            }

            _ = count;
            _ = includeNotPresent;

            var artistInfo = new Subsonic.ArtistInfo
            {
                biography = null,
                musicBrainzId = null,
                lastFmUrl = null,
                smallImageUrl = null,
                mediumImageUrl = null,
                largeImageUrl = null,
                similarArtist = null,
            };

            return WriteResponseAsync(context, Subsonic.ItemChoiceType.artistInfo, artistInfo);
        }

        private static Task HandleGetArtistInfo2RequestAsync(HttpContext context)
        {
            int id = GetRequiredArtistIdParameterValue(context, "id");
            int count = GetOptionalInt32ParameterValue(context, "count") ?? 20;
            bool includeNotPresent = GetOptionalBooleanParameterValue(context, "includeNotPresent") ?? false;

            _ = id;
            _ = count;
            _ = includeNotPresent;

            var artistInfo2 = new Subsonic.ArtistInfo2
            {
                biography = null,
                musicBrainzId = null,
                lastFmUrl = null,
                smallImageUrl = null,
                mediumImageUrl = null,
                largeImageUrl = null,
                similarArtist = null,
            };

            return WriteResponseAsync(context, Subsonic.ItemChoiceType.artistInfo2, artistInfo2);
        }

        private static Task HandleGetAlbumInfoRequestAsync(HttpContext context)
        {
            string id = GetRequiredStringParameterValue(context, "id");

            if (!TryParseDirectoryAlbumId(id, out _) &&
                !TryParseTrackId(id, out _))
            {
                throw RestApiErrorException.GenericError($"Invalid value for 'id'.");
            }

            var albumInfo = new Subsonic.AlbumInfo
            {
                notes = null,
                musicBrainzId = null,
                lastFmUrl = null,
                smallImageUrl = null,
                mediumImageUrl = null,
                largeImageUrl = null,
            };

            return WriteResponseAsync(context, Subsonic.ItemChoiceType.albumInfo, albumInfo);
        }

        private static Task HandleGetAlbum2InfoRequestAsync(HttpContext context)
        {
            int id = GetRequiredAlbumIdParameterValue(context, "id");

            _ = id;

            var albumInfo = new Subsonic.AlbumInfo
            {
                notes = null,
                musicBrainzId = null,
                lastFmUrl = null,
                smallImageUrl = null,
                mediumImageUrl = null,
                largeImageUrl = null,
            };

            return WriteResponseAsync(context, Subsonic.ItemChoiceType.albumInfo, albumInfo);
        }

        private static Task HandleGetSimilarSongsRequestAsync(HttpContext context)
        {
            string id = GetRequiredStringParameterValue(context, "id");
            int count = GetOptionalInt32ParameterValue(context, "count") ?? 50;

            if (!TryParseArtistId(id, out _) &&
                !TryParseAlbumId(id, out _) &&
                !TryParseTrackId(id, out _))
            {
                throw RestApiErrorException.GenericError($"Invalid value for 'id'.");
            }

            _ = count;

            var similarSongs = new Subsonic.SimilarSongs
            {
                song = null,
            };

            return WriteResponseAsync(context, Subsonic.ItemChoiceType.similarSongs, similarSongs);
        }

        private static Task HandleGetSimilarSongs2RequestAsync(HttpContext context)
        {
            int id = GetRequiredArtistIdParameterValue(context, "id");
            int count = GetOptionalInt32ParameterValue(context, "count") ?? 50;

            _ = id;
            _ = count;

            var similarSongs2 = new Subsonic.SimilarSongs2
            {
                song = null,
            };

            return WriteResponseAsync(context, Subsonic.ItemChoiceType.similarSongs2, similarSongs2);
        }

        private static Task HandleGetTopSongsRequestAsync(HttpContext context)
        {
            string artist = GetRequiredStringParameterValue(context, "artist");
            int count = GetOptionalInt32ParameterValue(context, "count") ?? 50;

            _ = artist;
            _ = count;

            var topSongs = new Subsonic.TopSongs
            {
                song = null,
            };

            return WriteResponseAsync(context, Subsonic.ItemChoiceType.topSongs, topSongs);
        }

        #endregion

        #region Album/song list

        private static async Task HandleGetAlbumListRequestAsync(HttpContext context)
        {
            int? musicFolderId = GetOptionalInt32ParameterValue(context, "musicFolderId");
            string type = GetRequiredStringParameterValue(context, "type");
            int size = GetOptionalInt32ParameterValue(context, "size") ?? 10;
            if (size < 1 || size > 500)
                throw RestApiErrorException.GenericError("Invalid value for 'size'.");
            int offset = GetOptionalInt32ParameterValue(context, "offset") ?? 0;
            if (offset < 0)
                throw RestApiErrorException.GenericError("Invalid value for 'offset'.");

            var apiContext = (ApiContext)context.Items[_apiContextKey];
            int apiUserId = apiContext.User.UserId;
            var dbContext = context.RequestServices.GetRequiredService<MediaInfoContext>();

            Subsonic.AlbumList2 albumList2;

            switch (type)
            {
                case "random":
                {
                    albumList2 = await RestApiQueries.GetAlbumList2RandomAsync(dbContext, apiUserId, musicFolderId, size, context.RequestAborted).ConfigureAwait(false);
                    break;
                }
                case "newest":
                {
                    albumList2 = await RestApiQueries.GetAlbumList2NewestAsync(dbContext, apiUserId, musicFolderId, offset, size, context.RequestAborted).ConfigureAwait(false);
                    break;
                }
                case "highest":
                {
                    // Ratings are not implemented.
                    goto case "alphabeticalByArtist";
                }
                case "frequent":
                {
                    // Scrobbling is not implemented.
                    goto case "alphabeticalByArtist";
                }
                case "recent":
                {
                    // Scrobbling is not implemented.
                    goto case "alphabeticalByArtist";
                }
                case "alphabeticalByName":
                {
                    albumList2 = await RestApiQueries.GetAlbumList2OrderedByAlbumTitleAsync(dbContext, apiUserId, musicFolderId, offset, size, context.RequestAborted).ConfigureAwait(false);
                    break;
                }
                case "alphabeticalByArtist":
                {
                    albumList2 = await RestApiQueries.GetAlbumList2OrderedByArtistNameAsync(dbContext, apiUserId, musicFolderId, offset, size, context.RequestAborted).ConfigureAwait(false);
                    break;
                }
                case "starred":
                {
                    albumList2 = await RestApiQueries.GetAlbumList2StarredAsync(dbContext, apiUserId, musicFolderId, offset, size, context.RequestAborted).ConfigureAwait(false);
                    break;
                }
                case "byYear":
                {
                    int fromYear = GetRequiredInt32ParameterValue(context, "fromYear");
                    int toYear = GetRequiredInt32ParameterValue(context, "toYear");

                    albumList2 = await RestApiQueries.GetAlbumList2ByYearAsync(dbContext, apiUserId, musicFolderId, offset, size, fromYear, toYear, context.RequestAborted).ConfigureAwait(false);
                    break;
                }
                case "byGenre":
                {
                    string genre = GetRequiredStringParameterValue(context, "genre");

                    albumList2 = await RestApiQueries.GetAlbumList2ByGenreAsync(dbContext, apiUserId, musicFolderId, offset, size, genre, context.RequestAborted).ConfigureAwait(false);
                    break;
                }
                default:
                    throw RestApiErrorException.GenericError("Invalid value for 'type'.");
            }

            var albumList = new Subsonic.AlbumList()
            {
                album = albumList2.album.Select(CreateDirectoryChild).ToArray(),
            };

            await WriteResponseAsync(context, Subsonic.ItemChoiceType.albumList, albumList).ConfigureAwait(false);
        }

        private static async Task HandleGetAlbumList2RequestAsync(HttpContext context)
        {
            int? musicFolderId = GetOptionalInt32ParameterValue(context, "musicFolderId");
            string type = GetRequiredStringParameterValue(context, "type");
            int size = GetOptionalInt32ParameterValue(context, "size") ?? 10;
            if (size < 1 || size > 500)
                throw RestApiErrorException.GenericError("Invalid value for 'size'.");
            int offset = GetOptionalInt32ParameterValue(context, "offset") ?? 0;
            if (offset < 0)
                throw RestApiErrorException.GenericError("Invalid value for 'offset'.");

            var apiContext = (ApiContext)context.Items[_apiContextKey];
            int apiUserId = apiContext.User.UserId;
            var dbContext = context.RequestServices.GetRequiredService<MediaInfoContext>();

            Subsonic.AlbumList2 albumList2;

            switch (type)
            {
                case "random":
                {
                    albumList2 = await RestApiQueries.GetAlbumList2RandomAsync(dbContext, apiUserId, musicFolderId, size, context.RequestAborted).ConfigureAwait(false);
                    break;
                }
                case "newest":
                {
                    albumList2 = await RestApiQueries.GetAlbumList2NewestAsync(dbContext, apiUserId, musicFolderId, offset, size, context.RequestAborted).ConfigureAwait(false);
                    break;
                }
                case "highest":
                {
                    // Ratings are not implemented.
                    goto case "alphabeticalByArtist";
                }
                case "frequent":
                {
                    // Scrobbling is not implemented.
                    goto case "alphabeticalByArtist";
                }
                case "recent":
                {
                    // Scrobbling is not implemented.
                    goto case "alphabeticalByArtist";
                }
                case "alphabeticalByName":
                {
                    albumList2 = await RestApiQueries.GetAlbumList2OrderedByAlbumTitleAsync(dbContext, apiUserId, musicFolderId, offset, size, context.RequestAborted).ConfigureAwait(false);
                    break;
                }
                case "alphabeticalByArtist":
                {
                    albumList2 = await RestApiQueries.GetAlbumList2OrderedByArtistNameAsync(dbContext, apiUserId, musicFolderId, offset, size, context.RequestAborted).ConfigureAwait(false);
                    break;
                }
                case "starred":
                {
                    albumList2 = await RestApiQueries.GetAlbumList2StarredAsync(dbContext, apiUserId, musicFolderId, offset, size, context.RequestAborted).ConfigureAwait(false);
                    break;
                }
                case "byYear":
                {
                    int fromYear = GetRequiredInt32ParameterValue(context, "fromYear");
                    int toYear = GetRequiredInt32ParameterValue(context, "toYear");

                    albumList2 = await RestApiQueries.GetAlbumList2ByYearAsync(dbContext, apiUserId, musicFolderId, offset, size, fromYear, toYear, context.RequestAborted).ConfigureAwait(false);
                    break;
                }
                case "byGenre":
                {
                    string genre = GetRequiredStringParameterValue(context, "genre");

                    albumList2 = await RestApiQueries.GetAlbumList2ByGenreAsync(dbContext, apiUserId, musicFolderId, offset, size, genre, context.RequestAborted).ConfigureAwait(false);
                    break;
                }
                default:
                    throw RestApiErrorException.GenericError("Invalid value for 'type'.");
            }

            await WriteResponseAsync(context, Subsonic.ItemChoiceType.albumList2, albumList2).ConfigureAwait(false);
        }

        private static async Task HandleGetRandomSongsRequestAsync(HttpContext context)
        {
            int? musicFolderId = GetOptionalInt32ParameterValue(context, "musicFolderId");
            string genre = GetOptionalStringParameterValue(context, "genre");
            int? fromYear = GetOptionalInt32ParameterValue(context, "fromYear");
            int? toYear = GetOptionalInt32ParameterValue(context, "toYear");
            int size = GetOptionalInt32ParameterValue(context, "size") ?? 10;
            if (size < 1 || size > 500)
                throw RestApiErrorException.GenericError("Invalid value for 'size'.");

            var apiContext = (ApiContext)context.Items[_apiContextKey];
            int apiUserId = apiContext.User.UserId;
            var dbContext = context.RequestServices.GetRequiredService<MediaInfoContext>();

            Subsonic.Songs randomSongs = await RestApiQueries.GetRandomSongsAsync(dbContext, apiUserId, musicFolderId, genre, fromYear, toYear, size, apiContext.Suffix, context.RequestAborted).ConfigureAwait(false);

            await WriteResponseAsync(context, Subsonic.ItemChoiceType.randomSongs, randomSongs).ConfigureAwait(false);
        }

        private static async Task HandleGetSongsByGenreRequestAsync(HttpContext context)
        {
            int? musicFolderId = GetOptionalInt32ParameterValue(context, "musicFolderId");
            string genre = GetRequiredStringParameterValue(context, "genre");
            int count = GetOptionalInt32ParameterValue(context, "count") ?? 10;
            if (count < 1 || count > 500)
                throw RestApiErrorException.GenericError("Invalid value for 'count'.");
            int offset = GetOptionalInt32ParameterValue(context, "offset") ?? 0;
            if (offset < 0)
                throw RestApiErrorException.GenericError("Invalid value for 'offset'.");

            var apiContext = (ApiContext)context.Items[_apiContextKey];
            int apiUserId = apiContext.User.UserId;
            var dbContext = context.RequestServices.GetRequiredService<MediaInfoContext>();

            Subsonic.Songs songsByGenre = await RestApiQueries.GetSongsByGenreAsync(dbContext, apiUserId, musicFolderId, genre, offset, count, apiContext.Suffix, context.RequestAborted).ConfigureAwait(false);

            await WriteResponseAsync(context, Subsonic.ItemChoiceType.songsByGenre, songsByGenre).ConfigureAwait(false);
        }

        private static Task HandleGetNowPlayingRequestAsync(HttpContext context) => throw RestApiErrorException.GenericError("Not implemented.");

        private static async Task HandleGetStarredRequestAsync(HttpContext context)
        {
            int? musicFolderId = GetOptionalInt32ParameterValue(context, "musicFolderId");

            var apiContext = (ApiContext)context.Items[_apiContextKey];
            int apiUserId = apiContext.User.UserId;
            var dbContext = context.RequestServices.GetRequiredService<MediaInfoContext>();

            Subsonic.Starred2 starred2 = await RestApiQueries.GetStarred2Async(dbContext, apiUserId, musicFolderId, apiContext.Suffix, context.RequestAborted).ConfigureAwait(false);

            var starred = new Subsonic.Starred()
            {
                artist = starred2.artist.Select(CreateArtist).ToArray(),
                album = starred2.album.Select(CreateDirectoryChild).ToArray(),
                song = starred2.song.Select(CreateDirectoryChild).ToArray(),
            };

            await WriteResponseAsync(context, Subsonic.ItemChoiceType.starred, starred).ConfigureAwait(false);
        }

        private static async Task HandleGetStarred2RequestAsync(HttpContext context)
        {
            int? musicFolderId = GetOptionalInt32ParameterValue(context, "musicFolderId");

            var apiContext = (ApiContext)context.Items[_apiContextKey];
            int apiUserId = apiContext.User.UserId;
            var dbContext = context.RequestServices.GetRequiredService<MediaInfoContext>();

            Subsonic.Starred2 starred2 = await RestApiQueries.GetStarred2Async(dbContext, apiUserId, musicFolderId, apiContext.Suffix, context.RequestAborted).ConfigureAwait(false);

            await WriteResponseAsync(context, Subsonic.ItemChoiceType.starred2, starred2).ConfigureAwait(false);
        }

        #endregion

        #region Searching

        private static Task HandleSearchRequestAsync(HttpContext context) => throw RestApiErrorException.GenericError("Not implemented.");

        private static async Task HandleSearch2RequestAsync(HttpContext context)
        {
            int? musicFolderId = GetOptionalInt32ParameterValue(context, "musicFolderId");
            string query = GetRequiredStringParameterValue(context, "query");
            int artistCount = GetOptionalInt32ParameterValue(context, "artistCount") ?? 20;
            if (artistCount < 1 || artistCount > 500)
                throw RestApiErrorException.GenericError("Invalid value for 'artistCount'.");
            int artistOffset = GetOptionalInt32ParameterValue(context, "artistOffset") ?? 0;
            if (artistOffset < 0)
                throw RestApiErrorException.GenericError("Invalid value for 'artistOffset'.");
            int albumCount = GetOptionalInt32ParameterValue(context, "albumCount") ?? 20;
            if (albumCount < 1 || albumCount > 500)
                throw RestApiErrorException.GenericError("Invalid value for 'albumCount'.");
            int albumOffset = GetOptionalInt32ParameterValue(context, "albumOffset") ?? 0;
            if (albumOffset < 0)
                throw RestApiErrorException.GenericError("Invalid value for 'albumOffset'.");
            int songCount = GetOptionalInt32ParameterValue(context, "songCount") ?? 20;
            if (songCount < 1 || songCount > 500)
                throw RestApiErrorException.GenericError("Invalid value for 'songCount'.");
            int songOffset = GetOptionalInt32ParameterValue(context, "songOffset") ?? 0;
            if (songOffset < 0)
                throw RestApiErrorException.GenericError("Invalid value for 'songOffset'.");

            var apiContext = (ApiContext)context.Items[_apiContextKey];
            int apiUserId = apiContext.User.UserId;
            var dbContext = context.RequestServices.GetRequiredService<MediaInfoContext>();

            Subsonic.SearchResult3 searchResult3 = await RestApiQueries.GetSearch3ResultsAsync(dbContext, apiUserId, musicFolderId, query, artistOffset, artistCount, albumOffset, albumCount, songOffset, songCount, apiContext.Suffix, context.RequestAborted).ConfigureAwait(false);

            var searchResult2 = new Subsonic.SearchResult2()
            {
                artist = searchResult3.artist.Select(CreateArtist).ToArray(),
                album = searchResult3.album.Select(CreateDirectoryChild).ToArray(),
                song = searchResult3.song.Select(CreateDirectoryChild).ToArray(),
            };

            await WriteResponseAsync(context, Subsonic.ItemChoiceType.searchResult2, searchResult2).ConfigureAwait(false);
        }

        private static async Task HandleSearch3RequestAsync(HttpContext context)
        {
            int? musicFolderId = GetOptionalInt32ParameterValue(context, "musicFolderId");
            string query = GetRequiredStringParameterValue(context, "query");
            int artistCount = GetOptionalInt32ParameterValue(context, "artistCount") ?? 20;
            if (artistCount < 1 || artistCount > 500)
                throw RestApiErrorException.GenericError("Invalid value for 'artistCount'.");
            int artistOffset = GetOptionalInt32ParameterValue(context, "artistOffset") ?? 0;
            if (artistOffset < 0)
                throw RestApiErrorException.GenericError("Invalid value for 'artistOffset'.");
            int albumCount = GetOptionalInt32ParameterValue(context, "albumCount") ?? 20;
            if (albumCount < 1 || albumCount > 500)
                throw RestApiErrorException.GenericError("Invalid value for 'albumCount'.");
            int albumOffset = GetOptionalInt32ParameterValue(context, "albumOffset") ?? 0;
            if (albumOffset < 0)
                throw RestApiErrorException.GenericError("Invalid value for 'albumOffset'.");
            int songCount = GetOptionalInt32ParameterValue(context, "songCount") ?? 20;
            if (songCount < 1 || songCount > 500)
                throw RestApiErrorException.GenericError("Invalid value for 'songCount'.");
            int songOffset = GetOptionalInt32ParameterValue(context, "songOffset") ?? 0;
            if (songOffset < 0)
                throw RestApiErrorException.GenericError("Invalid value for 'songOffset'.");

            var apiContext = (ApiContext)context.Items[_apiContextKey];
            int apiUserId = apiContext.User.UserId;
            var dbContext = context.RequestServices.GetRequiredService<MediaInfoContext>();

            Subsonic.SearchResult3 searchResult3 = await RestApiQueries.GetSearch3ResultsAsync(dbContext, apiUserId, musicFolderId, query, artistOffset, artistCount, albumOffset, albumCount, songOffset, songCount, apiContext.Suffix, context.RequestAborted).ConfigureAwait(false);

            await WriteResponseAsync(context, Subsonic.ItemChoiceType.searchResult3, searchResult3).ConfigureAwait(false);
        }

        #endregion

        #region Playlists

        private static async Task HandleGetPlaylistsRequestAsync(HttpContext context)
        {
            string username = GetOptionalStringParameterValue(context, "username");

            var apiContext = (ApiContext)context.Items[_apiContextKey];
            int apiUserId = apiContext.User.UserId;
            var dbContext = context.RequestServices.GetRequiredService<MediaInfoContext>();

            if (username != null && username != apiContext.User.Name)
            {
                if (!apiContext.User.IsAdmin)
                    throw RestApiErrorException.UserNotAuthorizedError();

                throw RestApiErrorException.GenericError("Impersonation is not implemented.");
            }

            Subsonic.Playlists playlists = await RestApiQueries.GetPlaylistsAsync(dbContext, apiUserId, context.RequestAborted).ConfigureAwait(false);

            await WriteResponseAsync(context, Subsonic.ItemChoiceType.playlists, playlists).ConfigureAwait(false);
        }

        private static async Task HandleGetPlaylistRequestAsync(HttpContext context)
        {
            int id = GetRequiredPlaylistIdParameterValue(context, "id");

            var apiContext = (ApiContext)context.Items[_apiContextKey];
            int apiUserId = apiContext.User.UserId;
            var dbContext = context.RequestServices.GetRequiredService<MediaInfoContext>();

            Subsonic.PlaylistWithSongs playlist = await RestApiQueries.GetPlaylistAsync(dbContext, apiUserId, id, apiContext.Suffix, context.RequestAborted).ConfigureAwait(false);

            await WriteResponseAsync(context, Subsonic.ItemChoiceType.playlist, playlist).ConfigureAwait(false);
        }

        private static async Task HandleCreatePlaylistRequestAsync(HttpContext context)
        {
            int? playlistId = GetOptionalPlaylistIdParameterValue(context, "playlistId");
            string name = GetOptionalStringParameterValue(context, "name");
            int[] songIds = GetTrackIdParameterValues(context, "songId");

            var apiContext = (ApiContext)context.Items[_apiContextKey];
            int apiUserId = apiContext.User.UserId;
            var dbContext = context.RequestServices.GetRequiredService<MediaInfoContext>();

            if (!playlistId.HasValue)
            {
                if (name == null)
                    throw RestApiErrorException.RequiredParameterMissingError("name");

                playlistId = await RestApiQueries.CreatePlaylistAsync(dbContext, apiUserId, name, context.RequestAborted).ConfigureAwait(false);

                await RestApiQueries.SetPlaylistSongsAsync(dbContext, apiUserId, playlistId.Value, songIds, context.RequestAborted).ConfigureAwait(false);

                await dbContext.SaveChangesAsync(context.RequestAborted).ConfigureAwait(false);
            }
            else
            {
                await RestApiQueries.RecreatePlaylistAsync(dbContext, apiUserId, playlistId.Value, name, context.RequestAborted).ConfigureAwait(false);

                await RestApiQueries.SetPlaylistSongsAsync(dbContext, apiUserId, playlistId.Value, songIds, context.RequestAborted).ConfigureAwait(false);

                await dbContext.SaveChangesAsync(context.RequestAborted).ConfigureAwait(false);
            }

            Subsonic.PlaylistWithSongs playlist = await RestApiQueries.GetPlaylistAsync(dbContext, apiUserId, playlistId.Value, apiContext.Suffix, context.RequestAborted).ConfigureAwait(false);

            await WriteResponseAsync(context, Subsonic.ItemChoiceType.playlist, playlist).ConfigureAwait(false);
        }

        private static async Task HandleUpdatePlaylistRequestAsync(HttpContext context)
        {
            int playlistId = GetRequiredPlaylistIdParameterValue(context, "playlistId");
            string name = GetOptionalStringParameterValue(context, "name");
            string comment = GetOptionalStringParameterValue(context, "comment");
            bool? @public = GetOptionalBooleanParameterValue(context, "public");
            int[] songIdToAdd = GetTrackIdParameterValues(context, "songIdToAdd");
            int[] songIndexToRemove = GetInt32ParameterValues(context, "songIndexToRemove");

            var apiContext = (ApiContext)context.Items[_apiContextKey];
            int apiUserId = apiContext.User.UserId;
            var dbContext = context.RequestServices.GetRequiredService<MediaInfoContext>();

            await RestApiQueries.UpdatePlaylistAsync(dbContext, apiUserId, playlistId, name, comment, @public, context.RequestAborted).ConfigureAwait(false);

            if (songIdToAdd.Length > 0)
                await RestApiQueries.AddPlaylistSongsAsync(dbContext, apiUserId, playlistId, songIdToAdd, context.RequestAborted).ConfigureAwait(false);

            if (songIndexToRemove.Length > 0)
                await RestApiQueries.RemovePlaylistSongsAsync(dbContext, apiUserId, playlistId, songIndexToRemove, context.RequestAborted).ConfigureAwait(false);

            await dbContext.SaveChangesAsync(context.RequestAborted).ConfigureAwait(false);

            Subsonic.PlaylistWithSongs playlist = await RestApiQueries.GetPlaylistAsync(dbContext, apiUserId, playlistId, apiContext.Suffix, context.RequestAborted).ConfigureAwait(false);

            await WriteResponseAsync(context, Subsonic.ItemChoiceType.playlist, playlist).ConfigureAwait(false);
        }

        private static async Task HandleDeletePlaylistRequestAsync(HttpContext context)
        {
            int id = GetRequiredPlaylistIdParameterValue(context, "id");

            var apiContext = (ApiContext)context.Items[_apiContextKey];
            int apiUserId = apiContext.User.UserId;
            var dbContext = context.RequestServices.GetRequiredService<MediaInfoContext>();

            await RestApiQueries.DeletePlaylistAsync(dbContext, apiUserId, apiContext.User.IsAdmin, id, context.RequestAborted).ConfigureAwait(false);

            await dbContext.SaveChangesAsync(context.RequestAborted).ConfigureAwait(false);

            await WriteResponseAsync(context, 0, null).ConfigureAwait(false);
        }

        #endregion

        #region Media retrieval

        private static async Task HandleStreamRequestAsync(HttpContext context)
        {
            int id = GetRequiredTrackIdParameterValue(context, "id");
            int maxBitRate = GetOptionalInt32ParameterValue(context, "maxBitRate") ?? 0;
            string format = GetOptionalStringParameterValue(context, "format");
            int timeOffset = GetOptionalInt32ParameterValue(context, "timeOffset") ?? 0;
            if (timeOffset < 0)
                throw RestApiErrorException.GenericError("Invalid value for 'timeOffset'.");
            string size = GetOptionalStringParameterValue(context, "size");
            bool estimateContentLength = GetOptionalBooleanParameterValue(context, "estimateContentLength") ?? false;
            bool converted = GetOptionalBooleanParameterValue(context, "converted") ?? false;

            if (timeOffset != 0)
                throw RestApiErrorException.GenericError("Specified value for 'timeOffset' is not supported.");
            if (size != null)
                throw RestApiErrorException.GenericError("Specified value for 'size' is not supported.");
            if (converted)
                throw RestApiErrorException.GenericError("Specified value for 'converted' is not supported.");

            var apiContext = (ApiContext)context.Items[_apiContextKey];
            int apiUserId = apiContext.User.UserId;
            var dbContext = context.RequestServices.GetRequiredService<MediaInfoContext>();

            RestApiQueries.TrackStreamInfo track = await RestApiQueries.GetTrackStreamInfoAsync(dbContext, apiUserId, id, context.RequestAborted).ConfigureAwait(false);

            string filePath = Path.Join(track.LibraryPath, track.DirectoryPath, track.FileName);
            if (!IOFile.Exists(filePath))
                throw RestApiErrorException.DataNotFoundError();

            // Don't resend if requester cache has fresh response.
            DateTime modifiedTime = IOFile.GetLastWriteTimeUtc(filePath);
            if (modifiedTime < context.Request.GetIfModifiedSince()?.AddSeconds(1))
            {
                context.Response.StatusCode = StatusCodes.Status304NotModified;
                return;
            }

            var now = DateTime.UtcNow;
            context.Response.SetDate(now);
            context.Response.SetLastModified(modifiedTime);
            context.Response.SetExpires(now);

            format = format ?? apiContext.Suffix;

            maxBitRate *= 1000;
            maxBitRate = maxBitRate == 0 ? apiContext.User.MaxBitRate
                       : apiContext.User.MaxBitRate == 0 ? maxBitRate
                       : Math.Min(maxBitRate, apiContext.User.MaxBitRate);

            ArgumentList arguments;

            switch (format)
            {
                case "mp3":
                {
                    maxBitRate = maxBitRate == 0 ? 256_000 : Math.Min(Math.Max(32_000, maxBitRate), 320_000);
                    arguments = MakeTranscoderArguments(track, filePath, maxBitRate, "mp3", "libmp3lame", "mp3");
                    break;
                }
                case "oga":
                case "ogg":
                {
                    maxBitRate = maxBitRate == 0 ? 192_000 : Math.Min(Math.Max(45_000, maxBitRate), 500_000);
                    arguments = MakeTranscoderArguments(track, filePath, maxBitRate, "vorbis", "libvorbis", "ogg");
                    break;
                }
                case "opus":
                {
                    maxBitRate = maxBitRate == 0 ? 128_000 : Math.Min(Math.Max(6_000, maxBitRate), 450_000);
                    arguments = MakeTranscoderArguments(track, filePath, maxBitRate, "opus", "libopus", "ogg");
                    break;
                }
                case "raw":
                {
                    context.Response.ContentType = null;

                    var fileInfo = new FileInfo(filePath);

                    context.Response.ContentLength = fileInfo.Length;

                    // NOTE: This doesn't account for StreamIndex.
                    await context.Response.SendFileAsync(new PhysicalFileInfo(fileInfo), context.RequestAborted).ConfigureAwait(false);
                    return;
                }
                default:
                    throw RestApiErrorException.GenericError("Specified value for 'format' is not supported.");
            }

            context.Response.ContentType = GetContentType(format);

            using (var process = FfmpegTranscoder.Transcode(arguments))
            using (context.RequestAborted.Register(process.Abort))
            using (var stream = process.OutputStream)
            {
                process.InputStream.Close();

                await stream.CopyToAsync(context.Response.Body, context.RequestAborted).ConfigureAwait(false);
            }
        }

        private static ArgumentList MakeTranscoderArguments(RestApiQueries.TrackStreamInfo track, string filePath, int maxBitRate, string codecName, string ffmpegCodec, string ffmpegContainer)
        {
            float gain = track.AlbumGain ?? track.TrackGain ?? 0;

            var arguments = new ArgumentList()
                .Add("-i").Add(filePath)
                .Add("-map_metadata").Add("-1")
                .Add("-map").Add(b => b.Append("0:").Append(track.StreamIndex.ToStringInvariant()));

            bool useTrackBitRate = track.CodecName == codecName && track.BitRate <= maxBitRate;
            if (useTrackBitRate && Math.Abs(gain) < 1)
            {
                arguments
                    .Add("-c:a:0").Add("copy");
            }
            else
            {
                int bitRate = useTrackBitRate ? track.BitRate.Value : maxBitRate;

                arguments
                    .Add("-c:a:0").Add(ffmpegCodec);
                if (ffmpegCodec == "libmp3lame")
                    arguments
                        .Add("-q:a").Add(GetLibmp3lameQuality(bitRate).ToStringInvariant());
                else if (ffmpegCodec == "libvorbis")
                    arguments
                        .Add("-q:a").Add(GetLibvorbisQuality(bitRate).ToStringInvariant());
                else
                    arguments
                        .Add("-b:a:0").Add(bitRate.ToStringInvariant());
                arguments
                    .Add("-af:0").Add(b => b.Append("volume=").Append(gain.ToStringInvariant()).Append("dB"));
            }

            arguments
                .Add("-f").Add(ffmpegContainer)
                .Add("-");

            return arguments;
        }

        private static async Task HandleDownloadRequestAsync(HttpContext context)
        {
            int id = GetRequiredTrackIdParameterValue(context, "id");

            var apiContext = (ApiContext)context.Items[_apiContextKey];
            int apiUserId = apiContext.User.UserId;
            var dbContext = context.RequestServices.GetRequiredService<MediaInfoContext>();

            RestApiQueries.TrackStreamInfo track = await RestApiQueries.GetTrackStreamInfoAsync(dbContext, apiUserId, id, context.RequestAborted).ConfigureAwait(false);

            string filePath = Path.Join(track.LibraryPath, track.DirectoryPath, track.FileName);
            if (!IOFile.Exists(filePath))
                throw RestApiErrorException.DataNotFoundError();

            // Don't resend if requester cache has fresh response.
            DateTime modifiedTime = IOFile.GetLastWriteTimeUtc(filePath);
            if (modifiedTime < context.Request.GetIfModifiedSince()?.AddSeconds(1))
            {
                context.Response.StatusCode = StatusCodes.Status304NotModified;
                return;
            }

            var now = DateTime.UtcNow;
            context.Response.SetDate(now);
            context.Response.SetLastModified(modifiedTime);
            context.Response.SetExpires(now);

            context.Response.ContentType = null;

            var fileInfo = new FileInfo(filePath);

            context.Response.Headers[HeaderNames.ContentDisposition] = new ContentDispositionHeaderValue("attachment") { FileName = fileInfo.Name }.ToString();
            context.Response.ContentLength = fileInfo.Length;

            await context.Response.SendFileAsync(new PhysicalFileInfo(fileInfo), context.RequestAborted).ConfigureAwait(false);
        }

        private static Task HandleHlsRequestAsync(HttpContext context) => throw RestApiErrorException.GenericError("Not implemented.");

        private static Task HandleGetCaptionsRequestAsync(HttpContext context) => throw RestApiErrorException.GenericError("Not implemented.");

        private static async Task HandleGetCoverArtRequestAsync(HttpContext context)
        {
            const int maxSize = 2160 * 2;

            string idString = GetRequiredStringParameterValue(context, "id");
            long id;
            if (!TryParseHexInt64(idString, out id))
                throw RestApiErrorException.GenericError("Invalid value for 'id'.");
            int? size = GetOptionalInt32ParameterValue(context, "size");
            if (size < 1 || size > maxSize)
                throw RestApiErrorException.GenericError("Invalid value for 'size'.");

            var apiContext = (ApiContext)context.Items[_apiContextKey];
            int apiUserId = apiContext.User.UserId;
            var dbContext = context.RequestServices.GetRequiredService<MediaInfoContext>();

            RestApiQueries.CoverArtStreamInfo picture = await RestApiQueries.GetCoverArtStreamInfoAsync(dbContext, apiUserId, id, context.RequestAborted).ConfigureAwait(false);

            string filePath = Path.Join(picture.LibraryPath, picture.DirectoryPath, picture.FileName);
            if (!IOFile.Exists(filePath))
                throw RestApiErrorException.DataNotFoundError();

            // Don't resend if requester cache has fresh response.
            DateTime modifiedTime = IOFile.GetLastWriteTimeUtc(filePath);
            if (modifiedTime < context.Request.GetIfModifiedSince()?.AddSeconds(1))
            {
                context.Response.StatusCode = StatusCodes.Status304NotModified;
                return;
            }

            var now = DateTime.UtcNow;
            context.Response.SetDate(now);
            context.Response.SetLastModified(modifiedTime);
            context.Response.SetExpires(now);

            var arguments = new ArgumentList()
                .Add("-i").Add(filePath)
                .Add("-map").Add(b => b.Append("0:").Append(picture.StreamIndex.ToStringInvariant()));

            arguments
                .Add("-c:v:0").Add("mjpeg")
                .Add("-q:v:0").Add("3");

            if (size.HasValue)
                arguments.Add("-filter:v:0").Add(b => b
                    .Append("scale=w=").Append(size.Value.ToStringInvariant())
                    .Append(":h=").Append(size.Value.ToStringInvariant())
                    .Append(":force_original_aspect_ratio=decrease"));

            arguments
                .Add("-f").Add("singlejpeg")
                .Add("-");

            context.Response.ContentType = "image/jpeg";

            using (var process = FfmpegTranscoder.Transcode(arguments))
            using (context.RequestAborted.Register(process.Abort))
            using (var stream = process.OutputStream)
            {
                process.InputStream.Close();

                await stream.CopyToAsync(context.Response.Body, context.RequestAborted).ConfigureAwait(false);
            }
        }

        private static Task HandleGetLyricsRequestAsync(HttpContext context) => throw RestApiErrorException.GenericError("Not implemented.");

        private static Task HandleGetAvatarRequestAsync(HttpContext context) => throw RestApiErrorException.DataNotFoundError();

        #endregion

        #region Media annotation

        private static async Task HandleStarRequestAsync(HttpContext context)
        {
            var artistIds = new List<int>();
            var albumIds = new List<int>();
            var trackIds = new List<int>();

            var values = context.Request.Query["id"];
            for (int i = 0; i < values.Count; ++i)
            {
                int id;
                if (TryParseDirectoryArtistId(values[i], out int artistId))
                    artistIds.Add(artistId);
                else if (TryParseDirectoryAlbumId(values[i], out int albumId))
                    albumIds.Add(albumId);
                else if (TryParseTrackId(values[i], out id))
                    trackIds.Add(id);
                else
                    throw RestApiErrorException.GenericError($"Invalid value for 'id'.");
            }

            artistIds.AddRange(GetArtistIdParameterValues(context, "artistId"));
            albumIds.AddRange(GetAlbumIdParameterValues(context, "albumId"));

            var apiContext = (ApiContext)context.Items[_apiContextKey];
            int apiUserId = apiContext.User.UserId;
            var dbContext = context.RequestServices.GetRequiredService<MediaInfoContext>();

            foreach (int artistId in artistIds)
                await RestApiQueries.StarArtistAsync(dbContext, apiUserId, artistId, context.RequestAborted).ConfigureAwait(false);

            foreach (int albumId in albumIds)
                await RestApiQueries.StarAlbumAsync(dbContext, apiUserId, albumId, context.RequestAborted).ConfigureAwait(false);

            foreach (int trackId in trackIds)
                await RestApiQueries.StarTrackAsync(dbContext, apiUserId, trackId, context.RequestAborted).ConfigureAwait(false);

            await dbContext.SaveChangesAsync(context.RequestAborted).ConfigureAwait(false);

            await WriteResponseAsync(context, 0, null).ConfigureAwait(false);
        }

        private static async Task HandleUnstarRequestAsync(HttpContext context)
        {
            var artistIds = new List<int>();
            var albumIds = new List<int>();
            var trackIds = new List<int>();

            var values = context.Request.Query["id"];
            for (int i = 0; i < values.Count; ++i)
            {
                int id;
                if (TryParseDirectoryArtistId(values[i], out int artistId))
                    artistIds.Add(artistId);
                else if (TryParseDirectoryAlbumId(values[i], out int albumId))
                    albumIds.Add(albumId);
                else if (TryParseTrackId(values[i], out id))
                    trackIds.Add(id);
                else
                    throw RestApiErrorException.GenericError($"Invalid value for 'id'.");
            }

            artistIds.AddRange(GetArtistIdParameterValues(context, "artistId"));
            albumIds.AddRange(GetAlbumIdParameterValues(context, "albumId"));

            var apiContext = (ApiContext)context.Items[_apiContextKey];
            int apiUserId = apiContext.User.UserId;
            var dbContext = context.RequestServices.GetRequiredService<MediaInfoContext>();

            foreach (int artistId in artistIds)
                await RestApiQueries.UnstarArtistAsync(dbContext, apiUserId, artistId, context.RequestAborted).ConfigureAwait(false);

            foreach (int albumId in albumIds)
                await RestApiQueries.UnstarAlbumAsync(dbContext, apiUserId, albumId, context.RequestAborted).ConfigureAwait(false);

            foreach (int trackId in trackIds)
                await RestApiQueries.UnstarTrackAsync(dbContext, apiUserId, trackId, context.RequestAborted).ConfigureAwait(false);

            await dbContext.SaveChangesAsync(context.RequestAborted).ConfigureAwait(false);

            await WriteResponseAsync(context, 0, null).ConfigureAwait(false);
        }

        private static Task HandleSetRatingRequestAsync(HttpContext context) => throw RestApiErrorException.GenericError("Not implemented.");

        private static Task HandleScrobbleRequestAsync(HttpContext context) => throw RestApiErrorException.GenericError("Not implemented.");

        #endregion

        #region Jukebox

        private static async Task HandleJukeboxControlRequestAsync(HttpContext context)
        {
            string action = GetRequiredStringParameterValue(context, "action");

            var apiContext = (ApiContext)context.Items[_apiContextKey];
            int apiUserId = apiContext.User.UserId;

            if (!apiContext.User.CanJukebox)
                throw RestApiErrorException.UserNotAuthorizedError();

            var jukeboxService = context.RequestServices.GetRequiredService<JukeboxService>();

            if (action == "get")
            {
                var dbContext = context.RequestServices.GetRequiredService<MediaInfoContext>();

                int[] trackIds;
                var jukeboxState = jukeboxService.GetState(out trackIds);

                Subsonic.Child[] entries = await RestApiQueries.GetTracksAsync(dbContext, apiUserId, trackIds, apiContext.Suffix, context.RequestAborted).ConfigureAwait(false);

                var jukeboxStatus = new Subsonic.JukeboxPlaylist()
                {
                    playing = jukeboxState.Playing,
                    currentIndex = jukeboxState.PlaylistIndex,
                    position = jukeboxState.TrackPosition,
                    positionSpecified = true,
                    gain = jukeboxState.Gain,
                    entry = entries,
                };

                await WriteResponseAsync(context, Subsonic.ItemChoiceType.jukeboxStatus, jukeboxStatus).ConfigureAwait(false);
            }
            else
            {
                switch (action)
                {
                    case "status":
                        break;
                    case "start":
                    {
                        jukeboxService.StartPlayback();
                        break;
                    }
                    case "stop":
                    {
                        jukeboxService.PausePlayback();
                        break;
                    }
                    case "skip":
                    {
                        int index = GetRequiredInt32ParameterValue(context, "index");
                        if (index < 0)
                            throw RestApiErrorException.GenericError("Invalid value for 'index'.");
                        int offset = GetOptionalInt32ParameterValue(context, "offset") ?? 0;

                        jukeboxService.SkipToTrack(index, offset);
                        jukeboxService.StartPlayback();
                        break;
                    }
                    case "set":
                    {
                        var dbContext = context.RequestServices.GetRequiredService<MediaInfoContext>();

                        int[] ids = GetTrackIdParameterValues(context, "id");

                        if (!await RestApiQueries.CanAddTracksAsync(dbContext, apiUserId, ids, context.RequestAborted).ConfigureAwait(false))
                            throw RestApiErrorException.DataNotFoundError();

                        jukeboxService.SetTracks(ids);
                        break;
                    }
                    case "add":
                    {
                        var dbContext = context.RequestServices.GetRequiredService<MediaInfoContext>();

                        int[] ids = GetTrackIdParameterValues(context, "id");

                        if (!await RestApiQueries.CanAddTracksAsync(dbContext, apiUserId, ids, context.RequestAborted).ConfigureAwait(false))
                            throw RestApiErrorException.DataNotFoundError();

                        jukeboxService.AddTracks(ids);
                        break;
                    }
                    case "remove":
                    {
                        int index = GetRequiredInt32ParameterValue(context, "index");
                        if (index < 0)
                            throw RestApiErrorException.GenericError("Invalid value for 'index'.");

                        jukeboxService.RemoveTrack(index);
                        break;
                    }
                    case "clear":
                    {
                        jukeboxService.ClearTracks();
                        jukeboxService.StopPlayback();
                        break;
                    }
                    case "shuffle":
                    {
                        jukeboxService.ShuffleTracks();
                        break;
                    }
                    case "setGain":
                    {
                        float gain = GetRequiredSingleParameterValue(context, "gain");
                        if (!(gain >= 0 && gain <= 1))
                            throw RestApiErrorException.GenericError("Invalid value for 'gain'.");

                        jukeboxService.SetGain(gain);
                        break;
                    }
                    default:
                        throw RestApiErrorException.GenericError("Invalid value for 'action'.");
                }

                var jukeboxState = jukeboxService.GetState();

                var jukeboxStatus = new Subsonic.JukeboxStatus()
                {
                    playing = jukeboxState.Playing,
                    currentIndex = jukeboxState.PlaylistIndex,
                    position = jukeboxState.TrackPosition,
                    positionSpecified = true,
                    gain = jukeboxState.Gain,
                };

                await WriteResponseAsync(context, Subsonic.ItemChoiceType.jukeboxStatus, jukeboxStatus).ConfigureAwait(false);
            }
        }

        #endregion

        #region User management

        private static async Task HandleGetUserRequestAsync(HttpContext context)
        {
            string username = GetRequiredStringParameterValue(context, "username");

            var apiContext = (ApiContext)context.Items[_apiContextKey];

            if (!apiContext.User.IsAdmin && username != apiContext.User.Name)
                throw RestApiErrorException.UserNotAuthorizedError();

            var dbContext = context.RequestServices.GetRequiredService<MediaInfoContext>();

            Subsonic.User user = await RestApiQueries.GetUserAsync(dbContext, username, context.RequestAborted).ConfigureAwait(false);

            await WriteResponseAsync(context, Subsonic.ItemChoiceType.user, user).ConfigureAwait(false);
        }

        private static async Task HandleGetUsersRequestAsync(HttpContext context)
        {
            var apiContext = (ApiContext)context.Items[_apiContextKey];

            if (!apiContext.User.IsAdmin)
                throw RestApiErrorException.UserNotAuthorizedError();

            var dbContext = context.RequestServices.GetRequiredService<MediaInfoContext>();

            Subsonic.Users users = await RestApiQueries.GetUsersAsync(dbContext, context.RequestAborted).ConfigureAwait(false);

            await WriteResponseAsync(context, Subsonic.ItemChoiceType.users, users).ConfigureAwait(false);
        }

        private static async Task HandleCreateUserRequestAsync(HttpContext context)
        {
            string username = GetRequiredStringParameterValue(context, "username");
            string password = GetRequiredStringParameterValue(context, "password");
            string email = GetRequiredStringParameterValue(context, "email");
            bool ldapAuthenticated = GetOptionalBooleanParameterValue(context, "ldapAuthenticated") ?? false;
            bool adminRole = GetOptionalBooleanParameterValue(context, "adminRole") ?? false;
            bool settingsRole = GetOptionalBooleanParameterValue(context, "settingsRole") ?? true;
            bool streamRole = GetOptionalBooleanParameterValue(context, "streamRole") ?? true;
            bool jukeboxRole = GetOptionalBooleanParameterValue(context, "jukeboxRole") ?? false;
            bool downloadRole = GetOptionalBooleanParameterValue(context, "downloadRole") ?? false;
            bool uploadRole = GetOptionalBooleanParameterValue(context, "uploadRole") ?? false;
            bool playlistRole = GetOptionalBooleanParameterValue(context, "playlistRole") ?? false;
            bool coverArtRole = GetOptionalBooleanParameterValue(context, "coverArtRole") ?? false;
            bool commentRole = GetOptionalBooleanParameterValue(context, "commentRole") ?? false;
            bool podcastRole = GetOptionalBooleanParameterValue(context, "podcastRole") ?? false;
            bool shareRole = GetOptionalBooleanParameterValue(context, "shareRole") ?? false;
            bool videoConversionRole = GetOptionalBooleanParameterValue(context, "videoConversionRole") ?? false;
            int[] musicFolderId = GetInt32ParameterValues(context, "musicFolderId");

            var apiContext = (ApiContext)context.Items[_apiContextKey];

            if (!apiContext.User.IsAdmin)
                throw RestApiErrorException.UserNotAuthorizedError();

            var dbContext = context.RequestServices.GetRequiredService<MediaInfoContext>();

            _ = email;
            _ = ldapAuthenticated;
            _ = streamRole;
            _ = jukeboxRole;
            _ = downloadRole;
            _ = uploadRole;
            _ = playlistRole;
            _ = coverArtRole;
            _ = commentRole;
            _ = podcastRole;
            _ = shareRole;
            _ = videoConversionRole;

            int userId = await RestApiQueries.CreateUserAsync(dbContext, username, password, isAdmin: adminRole, isGuest: !settingsRole, canJukebox: jukeboxRole, context.RequestAborted).ConfigureAwait(false);

            if (musicFolderId.Length > 0)
                await RestApiQueries.SetUserLibrariesAsync(dbContext, userId, musicFolderId, context.RequestAborted).ConfigureAwait(false);
            else
                await RestApiQueries.SetAllUserLibrariesAsync(dbContext, userId, context.RequestAborted).ConfigureAwait(false);

            await dbContext.SaveChangesAsync(context.RequestAborted).ConfigureAwait(false);

            await WriteResponseAsync(context, 0, null).ConfigureAwait(false);
        }

        private static async Task HandleUpdateUserRequestAsync(HttpContext context)
        {
            string username = GetRequiredStringParameterValue(context, "username");
            string password = GetOptionalStringParameterValue(context, "password");
            string email = GetOptionalStringParameterValue(context, "email");
            bool? ldapAuthenticated = GetOptionalBooleanParameterValue(context, "ldapAuthenticated");
            bool? adminRole = GetOptionalBooleanParameterValue(context, "adminRole");
            bool? settingsRole = GetOptionalBooleanParameterValue(context, "settingsRole");
            bool? streamRole = GetOptionalBooleanParameterValue(context, "streamRole");
            bool? jukeboxRole = GetOptionalBooleanParameterValue(context, "jukeboxRole");
            bool? downloadRole = GetOptionalBooleanParameterValue(context, "downloadRole");
            bool? uploadRole = GetOptionalBooleanParameterValue(context, "uploadRole");
            bool? playlistRole = GetOptionalBooleanParameterValue(context, "playlistRole");
            bool? coverArtRole = GetOptionalBooleanParameterValue(context, "coverArtRole");
            bool? commentRole = GetOptionalBooleanParameterValue(context, "commentRole");
            bool? podcastRole = GetOptionalBooleanParameterValue(context, "podcastRole");
            bool? shareRole = GetOptionalBooleanParameterValue(context, "shareRole");
            bool? videoConversionRole = GetOptionalBooleanParameterValue(context, "videoConversionRole");
            int[] musicFolderId = GetInt32ParameterValues(context, "musicFolderId");
            int? maxBitRate = GetOptionalInt32ParameterValue(context, "maxBitRate");

            var apiContext = (ApiContext)context.Items[_apiContextKey];

            if (!apiContext.User.IsAdmin)
                throw RestApiErrorException.UserNotAuthorizedError();

            var dbContext = context.RequestServices.GetRequiredService<MediaInfoContext>();

            _ = email;
            _ = ldapAuthenticated;
            _ = streamRole;
            _ = jukeboxRole;
            _ = downloadRole;
            _ = uploadRole;
            _ = playlistRole;
            _ = coverArtRole;
            _ = commentRole;
            _ = podcastRole;
            _ = shareRole;
            _ = videoConversionRole;

            int userId = await RestApiQueries.UpdateUserAsync(dbContext, username, password, maxBitRate, isAdmin: adminRole, isGuest: !settingsRole, canJukebox: jukeboxRole, context.RequestAborted).ConfigureAwait(false);

            if (musicFolderId.Length > 0)
                await RestApiQueries.SetUserLibrariesAsync(dbContext, userId, musicFolderId, context.RequestAborted).ConfigureAwait(false);

            await dbContext.SaveChangesAsync(context.RequestAborted).ConfigureAwait(false);

            await WriteResponseAsync(context, 0, null).ConfigureAwait(false);
        }

        private static async Task HandleDeleteUserRequestAsync(HttpContext context)
        {
            string username = GetRequiredStringParameterValue(context, "username");

            var apiContext = (ApiContext)context.Items[_apiContextKey];

            if (!apiContext.User.IsAdmin)
                throw RestApiErrorException.UserNotAuthorizedError();

            var dbContext = context.RequestServices.GetRequiredService<MediaInfoContext>();

            await RestApiQueries.DeleteUserAsync(dbContext, username, context.RequestAborted).ConfigureAwait(false);

            await dbContext.SaveChangesAsync(context.RequestAborted).ConfigureAwait(false);

            await WriteResponseAsync(context, 0, null).ConfigureAwait(false);
        }

        private static async Task HandleChangePassword(HttpContext context)
        {
            string username = GetRequiredStringParameterValue(context, "username");
            string password = HexDecodePassword(GetRequiredStringParameterValue(context, "password"));

            var apiContext = (ApiContext)context.Items[_apiContextKey];

            if (!apiContext.User.IsAdmin && username != apiContext.User.Name)
                throw RestApiErrorException.UserNotAuthorizedError();

            var dbContext = context.RequestServices.GetRequiredService<MediaInfoContext>();

            await RestApiQueries.UpdateUserAsync(dbContext, username, password, null, null, null, null, context.RequestAborted).ConfigureAwait(false);

            await dbContext.SaveChangesAsync(context.RequestAborted).ConfigureAwait(false);

            await WriteResponseAsync(context, 0, null).ConfigureAwait(false);
        }

        #endregion

        #region Media library scanning

        private static async Task HandleGetScanStatusRequestAsync(HttpContext context)
        {
            var dbContext = context.RequestServices.GetRequiredService<MediaInfoContext>();
            var mediaScanService = context.RequestServices.GetRequiredService<MediaScanService>();

            bool isScanning = mediaScanService.IsScanning;
            int fileCount = await dbContext.Files.CountAsync(context.RequestAborted).ConfigureAwait(false);

            var scanStatus = new Subsonic.ScanStatus()
            {
                scanning = isScanning,
                count = fileCount,
                countSpecified = true,
            };

            await WriteResponseAsync(context, Subsonic.ItemChoiceType.scanStatus, scanStatus).ConfigureAwait(false);
        }

        private static async Task HandleStartScanRequestAsync(HttpContext context)
        {
            var apiContext = (ApiContext)context.Items[_apiContextKey];

            if (!apiContext.User.IsAdmin)
                throw RestApiErrorException.UserNotAuthorizedError();

            var dbContext = context.RequestServices.GetRequiredService<MediaInfoContext>();
            var mediaScanService = context.RequestServices.GetRequiredService<MediaScanService>();

            _ = Task.Run(() => mediaScanService.ScanAsync());

            bool isScanning = mediaScanService.IsScanning;
            int fileCount = await dbContext.Files.CountAsync(context.RequestAborted).ConfigureAwait(false);

            var scanStatus = new Subsonic.ScanStatus()
            {
                scanning = isScanning,
                count = fileCount,
                countSpecified = true,
            };

            await WriteResponseAsync(context, Subsonic.ItemChoiceType.scanStatus, scanStatus).ConfigureAwait(false);
        }

        #endregion

        private static int GetRequiredArtistIdParameterValue(HttpContext context, string name)
        {
            var values = context.Request.Query[name];
            if (values.Count == 0)
                throw RestApiErrorException.RequiredParameterMissingError(name);
            if (values.Count > 1)
                throw RestApiErrorException.GenericError($"Specified multiple values for '{name}'.");
            int value;
            if (!TryParseArtistId((string)values, out value))
                throw RestApiErrorException.GenericError($"Invalid value for '{name}'.");
            return value;
        }

        private static int[] GetArtistIdParameterValues(HttpContext context, string name)
        {
            var values = context.Request.Query[name];
            int[] value = new int[values.Count];
            for (int i = 0; i < values.Count; ++i)
                if (!TryParseArtistId(values[i], out value[i]))
                    throw RestApiErrorException.GenericError($"Invalid value for '{name}'.");
            return value;
        }

        private static int GetRequiredAlbumIdParameterValue(HttpContext context, string name)
        {
            var values = context.Request.Query[name];
            if (values.Count == 0)
                throw RestApiErrorException.RequiredParameterMissingError(name);
            if (values.Count > 1)
                throw RestApiErrorException.GenericError($"Specified multiple values for '{name}'.");
            int value;
            if (!TryParseAlbumId((string)values, out value))
                throw RestApiErrorException.GenericError($"Invalid value for '{name}'.");
            return value;
        }

        private static int[] GetAlbumIdParameterValues(HttpContext context, string name)
        {
            var values = context.Request.Query[name];
            int[] value = new int[values.Count];
            for (int i = 0; i < values.Count; ++i)
                if (!TryParseAlbumId(values[i], out value[i]))
                    throw RestApiErrorException.GenericError($"Invalid value for '{name}'.");
            return value;
        }

        private static int GetRequiredTrackIdParameterValue(HttpContext context, string name)
        {
            var values = context.Request.Query[name];
            if (values.Count == 0)
                throw RestApiErrorException.RequiredParameterMissingError(name);
            if (values.Count > 1)
                throw RestApiErrorException.GenericError($"Specified multiple values for '{name}'.");
            int value;
            if (!TryParseTrackId((string)values, out value))
                throw RestApiErrorException.GenericError($"Invalid value for '{name}'.");
            return value;
        }

        private static int[] GetTrackIdParameterValues(HttpContext context, string name)
        {
            var values = context.Request.Query[name];
            int[] value = new int[values.Count];
            for (int i = 0; i < values.Count; ++i)
                if (!TryParseTrackId(values[i], out value[i]))
                    throw RestApiErrorException.GenericError($"Invalid value for '{name}'.");
            return value;
        }

        private static int GetRequiredPlaylistIdParameterValue(HttpContext context, string name)
        {
            var values = context.Request.Query[name];
            if (values.Count == 0)
                throw RestApiErrorException.RequiredParameterMissingError(name);
            if (values.Count > 1)
                throw RestApiErrorException.GenericError($"Specified multiple values for '{name}'.");
            int value;
            if (!TryParsePlaylistId((string)values, out value))
                throw RestApiErrorException.GenericError($"Invalid value for '{name}'.");
            return value;
        }

        private static int? GetOptionalPlaylistIdParameterValue(HttpContext context, string name)
        {
            var values = context.Request.Query[name];
            if (values.Count == 0)
                return null;
            if (values.Count > 1)
                throw RestApiErrorException.GenericError($"Specified multiple values for '{name}'.");
            int value;
            if (!TryParsePlaylistId((string)values, out value))
                throw RestApiErrorException.GenericError($"Invalid value for '{name}'.");
            return value;
        }

        private static string GetOptionalStringParameterValue(HttpContext context, string name)
        {
            var values = context.Request.Query[name];
            if (values.Count == 0)
                return null;
            if (values.Count > 1)
                throw RestApiErrorException.GenericError($"Specified multiple values for '{name}'.");
            return values;
        }

        private static string GetRequiredStringParameterValue(HttpContext context, string name)
        {
            var values = context.Request.Query[name];
            if (values.Count == 0)
                throw RestApiErrorException.RequiredParameterMissingError(name);
            if (values.Count > 1)
                throw RestApiErrorException.GenericError($"Specified multiple values for '{name}'.");
            return values;
        }

        private static bool? GetOptionalBooleanParameterValue(HttpContext context, string name)
        {
            var values = context.Request.Query[name];
            if (values.Count == 0)
                return null;
            if (values.Count > 1)
                throw RestApiErrorException.GenericError($"Specified multiple values for '{name}'.");
            bool value;
            if (!TryParseBoolean((string)values, out value))
                throw RestApiErrorException.GenericError($"Invalid value for '{name}'.");
            return value;
        }

        private static int? GetOptionalInt32ParameterValue(HttpContext context, string name)
        {
            var values = context.Request.Query[name];
            if (values.Count == 0)
                return null;
            if (values.Count > 1)
                throw RestApiErrorException.GenericError($"Specified multiple values for '{name}'.");
            int value;
            if (!TryParseInt32((string)values, out value))
                throw RestApiErrorException.GenericError($"Invalid value for '{name}'.");
            return value;
        }

        private static int GetRequiredInt32ParameterValue(HttpContext context, string name)
        {
            var values = context.Request.Query[name];
            if (values.Count == 0)
                throw RestApiErrorException.RequiredParameterMissingError(name);
            if (values.Count > 1)
                throw RestApiErrorException.GenericError($"Specified multiple values for '{name}'.");
            int value;
            if (!TryParseInt32((string)values, out value))
                throw RestApiErrorException.GenericError($"Invalid value for '{name}'.");
            return value;
        }

        private static int[] GetInt32ParameterValues(HttpContext context, string name)
        {
            var values = context.Request.Query[name];
            int[] value = new int[values.Count];
            for (int i = 0; i < values.Count; ++i)
                if (!TryParseInt32(values[i], out value[i]))
                    throw RestApiErrorException.GenericError($"Invalid value for '{name}'.");
            return value;
        }

        private static long? GetOptionalInt64ParameterValue(HttpContext context, string name)
        {
            var values = context.Request.Query[name];
            if (values.Count == 0)
                return null;
            if (values.Count > 1)
                throw RestApiErrorException.GenericError($"Specified multiple values for '{name}'.");
            long value;
            if (!TryParseInt64((string)values, out value))
                throw RestApiErrorException.GenericError($"Invalid value for '{name}'.");
            return value;
        }

        private static float GetRequiredSingleParameterValue(HttpContext context, string name)
        {
            var values = context.Request.Query[name];
            if (values.Count == 0)
                throw RestApiErrorException.RequiredParameterMissingError(name);
            if (values.Count > 1)
                throw RestApiErrorException.GenericError($"Specified multiple values for '{name}'.");
            float value;
            if (!TryParseSingle((string)values, out value))
                throw RestApiErrorException.GenericError($"Invalid value for '{name}'.");
            return value;
        }

        private static string HexDecodePassword(string password)
        {
            if (password.StartsWith("enc:", StringComparison.Ordinal))
                if (TryParseHexBytes(password.AsSpan(4), out byte[] bytes))
                    return Encoding.UTF8.GetString(bytes);

            return password;
        }

        private static bool TryParseBoolean(ReadOnlySpan<char> span, out bool value)
        {
            if (span.SequenceEqual("false"))
            {
                value = false;
                return true;
            }
            else if (span.SequenceEqual("true"))
            {
                value = true;
                return true;
            }

            value = default;
            return false;
        }

        private static bool TryParseHexByte(ReadOnlySpan<char> span, out byte value)
        {
            return byte.TryParse(span, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryParseInt32(ReadOnlySpan<char> span, out int value)
        {
            return int.TryParse(span, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryParseInt64(ReadOnlySpan<char> span, out long value)
        {
            return long.TryParse(span, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryParseHexInt64(ReadOnlySpan<char> span, out long value)
        {
            return long.TryParse(span, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryParseSingle(ReadOnlySpan<char> span, out float value)
        {
            return float.TryParse(span, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryParseHexBytes(ReadOnlySpan<char> hex, out byte[] bytes)
        {
            if (hex.Length % 2 != 0)
            {
                bytes = null;
                return false;
            }

            bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i += 1)
                if (!TryParseHexByte(hex.Slice(i * 2, 2), out bytes[i]))
                    return false;
            return true;
        }

        private static bool TryParseVersion(ReadOnlySpan<char> span, out int majorVersion, out int minorVersion)
        {
            var separatorSpan = ".".AsSpan();

            int index = span.IndexOf(separatorSpan, StringComparison.Ordinal);
            if (index == -1)
                index = span.Length;
            var majorVersionSpan = span.Slice(0, index);

            span = span.Slice(index + separatorSpan.Length);

            index = span.IndexOf(separatorSpan, StringComparison.Ordinal);
            if (index == -1)
                index = span.Length;
            var minorVersionSpan = span.Slice(0, index);

            if (int.TryParse(majorVersionSpan, NumberStyles.None, CultureInfo.InvariantCulture, out majorVersion) &&
                int.TryParse(minorVersionSpan, NumberStyles.None, CultureInfo.InvariantCulture, out minorVersion))
            {
                return true;
            }

            majorVersion = default;
            minorVersion = default;
            return false;
        }

        private enum ResponseFormat
        {
            Xml = 0,
            Json,
            JsonPadding,
        }

        private sealed class ApiContext
        {
            public ResponseFormat Format;
            public string FormatCallback;

            public string Version;
            public int MajorVersion;
            public int MinorVersion;

            public string Client;

            public User User;

            public string Suffix;
        }
    }
}
