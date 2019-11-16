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
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using static Hypersonic.Helpers;

namespace Hypersonic.Subsonic
{
    internal static class Helpers
    {
        internal static string ToFileId(int id)
        {
            return 'f' + id.ToStringInvariant();
        }

        internal static string ToArtistId(int id)
        {
            return 'r' + id.ToStringInvariant();
        }

        internal static string ToArtistId(int? id)
        {
            return id == null ? null : ToArtistId(id.Value);
        }

        internal static string ToAlbumId(int id)
        {
            return 'a' + id.ToStringInvariant();
        }

        internal static string ToTrackId(int id)
        {
            return 't' + id.ToStringInvariant();
        }

        internal static string ToPlaylistId(int id)
        {
            return 'p' + id.ToStringInvariant();
        }

        internal static bool TryParseFileId(ReadOnlySpan<char> span, out int id)
        {
            if (span.Length > 0 && span[0] == 'f')
                return int.TryParse(span.Slice(1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out id);

            id = default;
            return false;
        }

        internal static bool TryParseArtistId(ReadOnlySpan<char> span, out int id)
        {
            if (span.Length > 0 && span[0] == 'r')
                return int.TryParse(span.Slice(1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out id);

            id = default;
            return false;
        }

        internal static bool TryParseAlbumId(ReadOnlySpan<char> span, out int id)
        {
            if (span.Length > 0 && span[0] == 'a')
                return int.TryParse(span.Slice(1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out id);

            id = default;
            return false;
        }

        internal static bool TryParseTrackId(ReadOnlySpan<char> span, out int id)
        {
            if (span.Length > 0 && span[0] == 't')
                return int.TryParse(span.Slice(1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out id);

            id = default;
            return false;
        }

        internal static bool TryParsePlaylistId(ReadOnlySpan<char> span, out int id)
        {
            if (span.Length > 0 && span[0] == 'p')
                return int.TryParse(span.Slice(1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out id);

            id = default;
            return false;
        }

        internal static Genre CreateGenre(
                string genreName,
                int albumCount,
                int trackCount
            )
        {
            return new Genre
            {
                songCount = trackCount,
                albumCount = albumCount,
                Text = new[] { genreName },
            };
        }

        internal static Child CreateTrackChild(
                string fileName,
                long fileSize,
                int? artistId,
                string artistName,
                int albumId,
                string albumTitle,
                int trackId,
                int? trackBitRate,
                float? trackDuration,
                int? trackYear,
                int? discNumber,
                int? trackNumber,
                string trackTitle,
                long? coverPictureHash,
                string genreName,
                DateTime added,
                DateTime? starred,
                string transcodedSuffix
            )
        {
            return new Child
            {
                id = ToTrackId(trackId),
                parent = default,  // only tag-based browsing is supported
                isDir = false,
                title = trackTitle,
                album = albumTitle ?? "[no album]",
                artist = artistName ?? "[no artist]",
                track = trackNumber ?? 0,
                trackSpecified = trackNumber.HasValue,
                year = trackYear ?? 0,
                yearSpecified = trackYear.HasValue,
                genre = genreName,
                coverArt = coverPictureHash?.ToStringInvariant("X"),
                size = fileSize,
                sizeSpecified = true,
                contentType = default,  // not populated
                suffix = Path.GetExtension(fileName).TrimStart('.'),
                transcodedContentType = GetContentType(transcodedSuffix),
                transcodedSuffix = transcodedSuffix,
                duration = (int)Math.Round(trackDuration ?? 0),
                durationSpecified = trackDuration.HasValue,
                bitRate = (int)Math.Round(trackBitRate / 1e3 ?? 0),
                bitRateSpecified = trackBitRate.HasValue,
                path = default,  // not populated
                isVideo = default,
                isVideoSpecified = false,
                userRating = default,  // not implemented
                userRatingSpecified = false,  // not implemented
                averageRating = default,  // not implemented
                averageRatingSpecified = false,  // not implemented
                playCount = default,  // not implemented
                playCountSpecified = false,  // not implemented
                discNumber = discNumber ?? 0,
                discNumberSpecified = discNumber.HasValue,
                created = added,
                createdSpecified = true,
                starred = starred ?? default,
                starredSpecified = starred.HasValue,
                albumId = albumTitle != null ? ToAlbumId(albumId) : null,
                artistId = artistName != null ? ToArtistId(artistId) : null,
                type = MediaType.music,
                typeSpecified = true,
                bookmarkPosition = default,  // not implemented
                bookmarkPositionSpecified = false,  // not implemented
                originalWidth = default,
                originalWidthSpecified = false,
                originalHeight = default,
                originalHeightSpecified = false,
            };
        }

        internal static ArtistID3 CreateArtistID3(
                int artistId,
                string artistName,
                DateTime? starred,
                int albumCount
            )
        {
            return new ArtistID3
            {
                id = ToArtistId(artistId),
                name = artistName ?? "[no artist]",
                coverArt = null,
                albumCount = albumCount,
                starred = starred ?? default,
                starredSpecified = starred.HasValue,
            };
        }

        internal static ArtistWithAlbumsID3 CreateArtistWithAlbumsID3(
                int artistId,
                string artistName,
                DateTime? starred
            )
        {
            return new ArtistWithAlbumsID3
            {
                id = ToArtistId(artistId),
                name = artistName ?? "[no artist]",
                coverArt = null,
                albumCount = default,  // populate separately
                starred = starred ?? default,
                starredSpecified = starred.HasValue,
                album = default,  // populate separately
            };
        }

        internal static ArtistWithAlbumsID3 SetAlbums(
                this ArtistWithAlbumsID3 self,
                AlbumID3[] albums
            )
        {
            self.albumCount = albums.Length;
            self.album = albums;
            return self;
        }

        internal static AlbumID3 CreateAlbumID3(
                int? artistId,
                string albumArtistName,
                int albumId,
                int? albumYear,
                string albumTitle,
                long? coverPictureHash,
                string genreName,
                DateTime added,
                DateTime? starred,
                int tracksCount,
                float duration
            )
        {
            return new AlbumID3
            {
                id = ToAlbumId(albumId),
                name = albumTitle ?? "[no album]",
                artist = albumArtistName ?? "[no artist]",
                artistId = ToArtistId(artistId),
                coverArt = coverPictureHash?.ToStringInvariant("X"),
                songCount = tracksCount,
                duration = (int)Math.Round(duration),
                playCount = default,  // not implemented
                playCountSpecified = false,  // not implemented
                created = added,
                starred = starred ?? default,
                starredSpecified = starred.HasValue,
                year = albumYear ?? 0,
                yearSpecified = albumYear.HasValue,
                genre = genreName,
            };
        }

        internal static AlbumWithSongsID3 CreateAlbumWithSongsID3(
                int? artistId,
                string albumArtistName,
                int albumId,
                int? albumYear,
                string albumTitle,
                long? coverPictureHash,
                string genreName,
                DateTime added,
                DateTime? starred
            )
        {
            return new AlbumWithSongsID3
            {
                id = ToAlbumId(albumId),
                name = albumTitle ?? "[no album]",
                artist = albumArtistName ?? "[no artist]",
                artistId = ToArtistId(artistId),
                coverArt = coverPictureHash?.ToStringInvariant("X"),
                songCount = default,  // populate separately
                duration = default,  // populate separately
                playCount = default,  // not implemented
                playCountSpecified = false,  // not implemented
                created = added,
                starred = starred ?? default,
                starredSpecified = starred.HasValue,
                year = albumYear ?? 0,
                yearSpecified = albumYear.HasValue,
                genre = genreName,
                song = default,  // populate separately
            };
        }

        internal static AlbumWithSongsID3 SetSongs(
                this AlbumWithSongsID3 self,
                Child[] songs
            )
        {
            self.songCount = songs.Length;
            self.duration = songs.Sum(s => s.duration);
            self.song = songs;
            return self;
        }

        internal static Playlist CreatePlaylist(
                int playlistId,
                string playlistName,
                string playlistDescription,
                string ownerName,
                bool @public,
                DateTime created,
                DateTime modified,
                int tracksCount,
                float duration
            )
        {
            return new Playlist
            {
                allowedUser = null,  // not implemented
                id = ToPlaylistId(playlistId),
                name = playlistName,
                comment = playlistDescription,
                owner = ownerName,
                @public = @public,
                publicSpecified = true,
                songCount = tracksCount,
                duration = (int)Math.Round(duration),
                created = created,
                changed = modified,
                coverArt = null,  // not implemented
            };
        }

        internal static PlaylistWithSongs CreatePlaylistWithSongs(
                int playlistId,
                string playlistName,
                string playlistDescription,
                string ownerName,
                bool @public,
                DateTime created,
                DateTime modified
            )
        {
            return new PlaylistWithSongs
            {
                allowedUser = null,  // not implemented
                id = ToPlaylistId(playlistId),
                name = playlistName,
                comment = playlistDescription,
                owner = ownerName,
                @public = @public,
                publicSpecified = true,
                songCount = default,  // populate separately
                duration = default,  // populate separately
                created = created,
                changed = modified,
                coverArt = null,  // not implemented
                entry = default,  // populate separately
            };
        }

        internal static PlaylistWithSongs SetSongs(
                this PlaylistWithSongs self,
                Child[] entries
            )
        {
            self.songCount = entries.Length;
            self.duration = entries.Sum(s => s.duration);
            self.entry = entries;
            return self;
        }

        internal static bool TryParseDirectoryArtistId(ReadOnlySpan<char> span, out int artistId)
        {
            if (!span.StartsWith("d", StringComparison.Ordinal))
            {
                artistId = default;
                return false;
            }

            return TryParseArtistId(span.Slice(1), out artistId);
        }

        internal static bool TryParseDirectoryAlbumId(ReadOnlySpan<char> span, out int albumId)
        {
            if (!span.StartsWith("d", StringComparison.Ordinal))
            {
                albumId = default;
                return false;
            }

            return TryParseAlbumId(span.Slice(1), out albumId);
        }

        internal static Indexes CreateIndexes(ArtistsID3 artists)
        {
            return new Indexes()
            {
                ignoredArticles = artists.ignoredArticles,
                index = artists.index
                    .Select(index => new Index()
                    {
                        name = index.name,
                        artist = index.artist.Select(CreateArtist).ToArray(),
                    })
                    .ToArray(),
            };
        }

        internal static Artist CreateArtist(ArtistID3 artist)
        {
            return new Artist()
            {
                id = "d" + artist.id,
                name = artist.name,
                starred = artist.starred,
                starredSpecified = artist.starredSpecified,
                userRating = default,
                userRatingSpecified = false,
                averageRating = default,
                averageRatingSpecified = false,
            };
        }

        internal static Directory CreateDirectory(ArtistWithAlbumsID3 artist)
        {
            return new Directory()
            {
                id = "d" + artist.id,
                parent = null,
                name = artist.name,
                starred = artist.starred,
                starredSpecified = artist.starredSpecified,
                userRating = default,
                userRatingSpecified = false,
                averageRating = default,
                averageRatingSpecified = false,
                playCount = default,
                playCountSpecified = false,
                child = artist.album.Select(CreateDirectoryChild).ToArray(),
            };
        }

        internal static Directory CreateDirectory(AlbumWithSongsID3 album)
        {
            return new Directory()
            {
                id = "d" + album.id,
                parent = null,
                name = album.name,
                starred = album.starred,
                starredSpecified = album.starredSpecified,
                userRating = default,
                userRatingSpecified = false,
                averageRating = default,
                averageRatingSpecified = false,
                playCount = default,
                playCountSpecified = false,
                child = album.song.Select(CreateDirectoryChild).ToArray(),
            };
        }

        internal static Child CreateDirectoryChild(AlbumID3 album)
        {
            return new Child()
            {
                id = "d" + album.id,
                parent = "d" + album.artistId,
                isDir = true,
                title = album.name,
                album = null,
                artist = album.artist,
                track = default,
                trackSpecified = false,
                year = album.year,
                yearSpecified = album.yearSpecified,
                genre = album.genre,
                coverArt = album.coverArt,
                size = default,
                sizeSpecified = false,
                contentType = null,
                suffix = null,
                transcodedContentType = null,
                transcodedSuffix = null,
                duration = album.duration,
                durationSpecified = true,
                bitRate = default,
                bitRateSpecified = false,
                path = null,
                isVideo = default,
                isVideoSpecified = false,
                userRating = default,
                userRatingSpecified = false,
                averageRating = default,
                averageRatingSpecified = false,
                playCount = default,
                playCountSpecified = false,
                discNumber = default,
                discNumberSpecified = false,
                created = default,
                createdSpecified = false,
                starred = album.starred,
                starredSpecified = album.starredSpecified,
                albumId = album.id,
                artistId = album.artistId,
                type = default,
                typeSpecified = false,
                bookmarkPosition = default,
                bookmarkPositionSpecified = false,
                originalWidth = default,
                originalWidthSpecified = false,
                originalHeight = default,
                originalHeightSpecified = false,
            };
        }

        internal static Child CreateDirectoryChild(Child song)
        {
            return new Child()
            {
                id = song.id,
                parent = "d" + song.artistId,
                isDir = song.isDir,
                title = song.title,
                album = song.album,
                artist = song.artist,
                track = song.track,
                trackSpecified = song.trackSpecified,
                year = song.year,
                yearSpecified = song.yearSpecified,
                genre = song.genre,
                coverArt = song.coverArt,
                size = song.size,
                sizeSpecified = song.sizeSpecified,
                contentType = song.contentType,
                suffix = song.suffix,
                transcodedContentType = song.transcodedContentType,
                transcodedSuffix = song.transcodedSuffix,
                duration = song.duration,
                durationSpecified = song.durationSpecified,
                bitRate = song.bitRate,
                bitRateSpecified = song.bitRateSpecified,
                path = song.path,
                isVideo = song.isVideo,
                isVideoSpecified = song.isVideoSpecified,
                userRating = song.userRating,
                userRatingSpecified = song.userRatingSpecified,
                averageRating = song.averageRating,
                averageRatingSpecified = song.averageRatingSpecified,
                playCount = song.playCount,
                playCountSpecified = song.playCountSpecified,
                discNumber = song.discNumber,
                discNumberSpecified = song.discNumberSpecified,
                created = song.created,
                createdSpecified = song.createdSpecified,
                starred = song.starred,
                starredSpecified = song.starredSpecified,
                albumId = song.albumId,
                artistId = song.artistId,
                type = song.type,
                typeSpecified = song.typeSpecified,
                bookmarkPosition = song.bookmarkPosition,
                bookmarkPositionSpecified = song.bookmarkPositionSpecified,
                originalWidth = song.originalWidth,
                originalWidthSpecified = song.originalWidthSpecified,
                originalHeight = song.originalHeight,
                originalHeightSpecified = song.originalHeightSpecified,
            };
        }
    }
}
