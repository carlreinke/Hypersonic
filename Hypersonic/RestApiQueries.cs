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
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Extensions.Internal;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static Hypersonic.Data.Queries;
using static Hypersonic.Subsonic.Helpers;

namespace Hypersonic
{
    internal static class RestApiQueries
    {
        #region Browsing

        internal static async Task<Subsonic.MusicFolders> GetMusicFoldersAsync(MediaInfoContext dbContext, int apiUserId, CancellationToken cancellationToken)
        {
            Subsonic.MusicFolder[] musicFolders = await dbContext.Libraries
                .WhereIsAccessibleBy(apiUserId)
                .Select(l => new
                {
                    l.LibraryId,
                    l.Name,
                })
                .AsAsyncEnumerable()
                .Select(l => new Subsonic.MusicFolder()
                {
                    id = l.LibraryId,
                    name = l.Name,
                })
                .ToArray(cancellationToken).ConfigureAwait(false);

            return new Subsonic.MusicFolders()
            {
                musicFolder = musicFolders,
            };
        }

        internal static async Task<Subsonic.Genres> GetGenresAsync(MediaInfoContext dbContext, int apiUserId, CancellationToken cancellationToken)
        {
            IQueryable<Track> tracksQuery = dbContext.Tracks
                .WhereIsAccessibleBy(apiUserId);

            Subsonic.Genre[] genres = await dbContext.Genres
                .WithCounts(dbContext, tracksQuery)
                .Select(e => CreateGenre(
                    e.Genre.Name,
                    e.AlbumsCount,
                    e.TracksCount))
                .ToArrayAsync(cancellationToken).ConfigureAwait(false);

            return new Subsonic.Genres()
            {
                genre = genres,
            };
        }

        internal static async Task<Subsonic.ArtistsID3> GetArtistsAsync(MediaInfoContext dbContext, int apiUserId, int? musicFolderId, CancellationToken cancellationToken)
        {
            var comparer = CultureInfo.CurrentCulture.CompareInfo.GetStringComparer(CompareOptions.IgnoreCase);

            IQueryable<Track> tracksQuery = dbContext.Tracks
                .WhereIsAccessibleBy(apiUserId, musicFolderId);

            Subsonic.IndexID3[] indexes = await Task.WhenAll(await dbContext.Artists
                .WithStarredBy(dbContext, apiUserId)
                .WithAlbumsCount(dbContext, tracksQuery)
                .Select(e => new
                {
                    ArtistSortName = e.Artist.SortName ?? e.Artist.Name,
                    Item = CreateArtistID3(
                        e.Artist.ArtistId,
                        e.Artist.Name,
                        e.Starred,
                        e.AlbumsCount),
                })
                .AsAsyncEnumerable()
                // group by culture-aware uppercase first letter of artist name
                .GroupBy(e =>
                {
                    string name = e.ArtistSortName;
                    if (name != null)
                    {
                        string t = StringInfo.GetNextTextElement(name).Normalize();
                        if (t.Length > 0 && char.IsLetter(t, 0))
                            return t.ToUpper(CultureInfo.CurrentCulture);
                    }
                    return "#";
                })
                .OrderBy(g => g.Key.ToString(CultureInfo.CurrentCulture), comparer)
                // order by artist name using culture-aware comparison
                .Select(g => new { g.Key, Items = g.OrderBy(a => a.ArtistSortName, comparer) })
                .Select(async g => new Subsonic.IndexID3()
                {
                    name = g.Key.ToString(CultureInfo.CurrentCulture),
                    artist = await g.Items.Select(e => e.Item).ToArray(cancellationToken).ConfigureAwait(false),
                })
                .ToArray(cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);


            if (indexes.Length == 0 &&
                musicFolderId != null &&
                !dbContext.Libraries.Any(l => l.LibraryId == musicFolderId))
            {
                throw RestApiErrorException.DataNotFoundError();
            }

            return new Subsonic.ArtistsID3()
            {
                index = indexes,
                ignoredArticles = string.Empty,
            };
        }

        internal static async Task<Subsonic.ArtistWithAlbumsID3> GetArtistAsync(MediaInfoContext dbContext, int apiUserId, int artistId, CancellationToken cancellationToken)
        {
            var comparer = CultureInfo.CurrentCulture.CompareInfo.GetStringComparer(CompareOptions.IgnoreCase);

            Subsonic.ArtistWithAlbumsID3 artist = await dbContext.Artists
                // where artist is requested artist
                .Where(a => a.ArtistId == artistId)
                .WithStarredBy(dbContext, apiUserId)
                .Select(e => CreateArtistWithAlbumsID3(
                    e.Artist.ArtistId,
                    e.Artist.Name,
                    e.Starred))
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (artist == null)
                throw RestApiErrorException.DataNotFoundError();

            IQueryable<Track> tracksQuery = dbContext.Tracks
                .WhereIsAccessibleBy(apiUserId);

            Subsonic.AlbumID3[] albums = await dbContext.Albums
                // where album is by requested artist
                .Where(a => a.ArtistId == artistId)
                .WithStarredBy(dbContext, apiUserId)
                .WithTracksCount(dbContext, tracksQuery)
                .Select(e => new
                {
                    e.Album.AlbumId,
                    AlbumDate = e.Album.Date,
                    AlbumSortTitle = e.Album.SortTitle ?? e.Album.Title,
                    Item = CreateAlbumID3(
                        e.Album.ArtistId,
                        e.Album.Artist.Name,
                        e.Album.AlbumId,
                        e.Album.Date / 10000,
                        e.Album.Title,
                        e.Album.CoverPicture.StreamHash as long?,
                        e.Album.Genre.Name,
                        e.Album.Added,
                        e.Starred,
                        e.TracksCount,
                        e.Duration)
                })
                .AsAsyncEnumerable()
                // order by album date
                .OrderBy(e => e.AlbumDate)
                // then by album title using culture-aware comparison
                .ThenBy(e => e.AlbumSortTitle, comparer)
                // ensure stable ordering
                .ThenBy(e => e.AlbumId)
                .Select(e => e.Item)
                .ToArray(cancellationToken).ConfigureAwait(false);
            if (albums.Length == 0)
                throw RestApiErrorException.DataNotFoundError();

            return artist.SetAlbums(albums);
        }

        internal static async Task<Subsonic.AlbumWithSongsID3> GetAlbumAsync(MediaInfoContext dbContext, int apiUserId, int albumId, string transcodedSuffix, CancellationToken cancellationToken)
        {
            var comparer = CultureInfo.CurrentCulture.CompareInfo.GetStringComparer(CompareOptions.IgnoreCase);

            Subsonic.AlbumWithSongsID3 album = await dbContext.Albums
                // where album is requested album
                .Where(a => a.AlbumId == albumId)
                .WithStarredBy(dbContext, apiUserId)
                .Select(e => CreateAlbumWithSongsID3(
                    e.Album.ArtistId,
                    e.Album.Artist.Name,
                    e.Album.AlbumId,
                    e.Album.Date / 10000,
                    e.Album.Title,
                    e.Album.CoverPicture.StreamHash as long?,
                    e.Album.Genre.Name,
                    e.Album.Added,
                    e.Starred))
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (album == null)
                throw RestApiErrorException.DataNotFoundError();

            Subsonic.Child[] tracks = await dbContext.Tracks
                // where track is on requested album
                .Where(t => t.AlbumId == albumId)
                .WhereIsAccessibleBy(apiUserId)
                .WithStarredBy(dbContext, apiUserId)
                .Select(e => new
                {
                    e.Track.TrackId,
                    TrackDiscNumber = e.Track.DiscNumber,
                    TrackTrackNumber = e.Track.TrackNumber,
                    TrackSortTitle = e.Track.SortTitle ?? e.Track.Title,
                    Item = CreateTrackChild(
                        e.Track.File.Name,
                        e.Track.File.Size,
                        e.Track.ArtistId,
                        e.Track.Artist.Name,
                        e.Track.AlbumId,
                        e.Track.Album.Title,
                        e.Track.TrackId,
                        e.Track.BitRate,
                        e.Track.Duration,
                        e.Track.Date / 10000,
                        e.Track.DiscNumber,
                        e.Track.TrackNumber,
                        e.Track.Title,
                        e.Track.CoverPicture.StreamHash as long?,
                        e.Track.Genre.Name,
                        e.Track.Added,
                        e.Starred,
                        transcodedSuffix)
                })
                .AsAsyncEnumerable()
                // order by disc number
                .OrderBy(e => e.TrackDiscNumber ?? int.MaxValue)
                // then by track number
                .ThenBy(e => e.TrackTrackNumber ?? int.MaxValue)
                // then by track title using culture-aware comparison
                .ThenBy(e => e.TrackSortTitle, comparer)
                // ensure stable ordering
                .ThenBy(e => e.TrackId)
                .Select(e => e.Item)
                .ToArray(cancellationToken).ConfigureAwait(false);
            if (tracks.Length == 0)
                throw RestApiErrorException.DataNotFoundError();

            return album.SetSongs(tracks);
        }

        internal static async Task<Subsonic.Child> GetSongAsync(MediaInfoContext dbContext, int apiUserId, int trackId, string transcodedSuffix, CancellationToken cancellationToken)
        {
            var comparer = CultureInfo.CurrentCulture.CompareInfo.GetStringComparer(CompareOptions.IgnoreCase);

            Subsonic.Child track = await dbContext.Tracks
                // where track is requested track
                .Where(t => t.TrackId == trackId)
                .WhereIsAccessibleBy(apiUserId)
                .WithStarredBy(dbContext, apiUserId)
                .Select(e => CreateTrackChild(
                    e.Track.File.Name,
                    e.Track.File.Size,
                    e.Track.ArtistId,
                    e.Track.Artist.Name,
                    e.Track.AlbumId,
                    e.Track.Album.Title,
                    e.Track.TrackId,
                    e.Track.BitRate,
                    e.Track.Duration,
                    e.Track.Date / 10000,
                    e.Track.DiscNumber,
                    e.Track.TrackNumber,
                    e.Track.Title,
                    e.Track.CoverPicture.StreamHash as long?,
                    e.Track.Genre.Name,
                    e.Track.Added,
                    e.Starred,
                    transcodedSuffix))
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (track == null)
                throw RestApiErrorException.DataNotFoundError();

            return track;
        }

        #endregion

        #region Album/song list

        internal static async Task<Subsonic.AlbumList2> GetAlbumList2RandomAsync(MediaInfoContext dbContext, int apiUserId, int? musicFolderId, int count, CancellationToken cancellationToken)
        {
            IQueryable<Track> tracksQuery = dbContext.Tracks
                .WhereIsAccessibleBy(apiUserId, musicFolderId);

            Subsonic.AlbumID3[] albums = await dbContext.Albums
                .WhereIsNotPlaceholder()
                .WithStarredBy(dbContext, apiUserId)
                .WithTracksCount(dbContext, tracksQuery)
                // order randomly
                .OrderBy(_ => MediaInfoContext.Random())
                .Select(e => CreateAlbumID3(
                    e.Album.ArtistId,
                    e.Album.Artist.Name,
                    e.Album.AlbumId,
                    e.Album.Date / 10000,
                    e.Album.Title,
                    e.Album.CoverPicture.StreamHash as long?,
                    e.Album.Genre.Name,
                    e.Album.Added,
                    e.Starred,
                    e.TracksCount,
                    e.Duration))
                // limit number of results
                .Take(count)
                .ToArrayAsync(cancellationToken).ConfigureAwait(false);

            if (albums.Length == 0 &&
                musicFolderId != null &&
                !dbContext.Libraries.Any(l => l.LibraryId == musicFolderId))
            {
                throw RestApiErrorException.DataNotFoundError();
            }

            return new Subsonic.AlbumList2()
            {
                album = albums,
            };
        }

        internal static async Task<Subsonic.AlbumList2> GetAlbumList2NewestAsync(MediaInfoContext dbContext, int apiUserId, int? musicFolderId, int offset, int count, CancellationToken cancellationToken)
        {
            IQueryable<Track> tracksQuery = dbContext.Tracks
                .WhereIsAccessibleBy(apiUserId, musicFolderId);

            Subsonic.AlbumID3[] albums = await dbContext.Albums
                .WhereIsNotPlaceholder()
                .WithStarredBy(dbContext, apiUserId)
                .WithTracksCount(dbContext, tracksQuery)
                // order by reverse "added" timestamp
                .OrderByDescending(e => e.Album.Added)
                // ensure stable ordering for pagination
                .ThenBy(e => e.Album.AlbumId)
                .Select(e => CreateAlbumID3(
                    e.Album.ArtistId,
                    e.Album.Artist.Name,
                    e.Album.AlbumId,
                    e.Album.Date / 10000,
                    e.Album.Title,
                    e.Album.CoverPicture.StreamHash as long?,
                    e.Album.Genre.Name,
                    e.Album.Added,
                    e.Starred,
                    e.TracksCount,
                    e.Duration))
                // paginate
                .Skip(offset)
                .Take(count)
                .ToArrayAsync(cancellationToken).ConfigureAwait(false);

            if (albums.Length == 0 &&
                musicFolderId != null &&
                !dbContext.Libraries.Any(l => l.LibraryId == musicFolderId))
            {
                throw RestApiErrorException.DataNotFoundError();
            }

            return new Subsonic.AlbumList2()
            {
                album = albums,
            };
        }

        internal static async Task<Subsonic.AlbumList2> GetAlbumList2OrderedByAlbumTitleAsync(MediaInfoContext dbContext, int apiUserId, int? musicFolderId, int offset, int count, CancellationToken cancellationToken)
        {
            var comparer = CultureInfo.CurrentCulture.CompareInfo.GetStringComparer(CompareOptions.IgnoreCase);

            int[] albumIds = await dbContext.Albums
                .WhereIsNotPlaceholder()
                .WhereIsAccessibleBy(dbContext, apiUserId, musicFolderId)
                .Select(a => new
                {
                    a.AlbumId,
                    AlbumSortTitle = a.SortTitle ?? a.Title,
                })
                .AsAsyncEnumerable()
                // order by album title using culture-aware comparison
                .OrderBy(e => e.AlbumSortTitle, comparer)
                // ensure stable ordering for pagination
                .ThenBy(a => a.AlbumId)
                .Select(e => e.AlbumId)
                // paginate
                .Skip(offset)
                .Take(count)
                .ToArray(cancellationToken).ConfigureAwait(false);

            if (albumIds.Length == 0 &&
                musicFolderId != null &&
                !dbContext.Libraries.Any(l => l.LibraryId == musicFolderId))
            {
                throw RestApiErrorException.DataNotFoundError();
            }

            IQueryable<Track> tracksQuery = dbContext.Tracks
                .WhereIsAccessibleBy(apiUserId, musicFolderId);

            Subsonic.AlbumID3[] albums = await dbContext.Albums
                // where album is in results
                .Where(a => albumIds.Contains(a.AlbumId))
                .WithStarredBy(dbContext, apiUserId)
                .WithTracksCount(dbContext, tracksQuery)
                .Select(e => new
                {
                    e.Album.AlbumId,
                    AlbumSortTitle = e.Album.SortTitle ?? e.Album.Title,
                    Item = CreateAlbumID3(
                        e.Album.ArtistId,
                        e.Album.Artist.Name,
                        e.Album.AlbumId,
                        e.Album.Date / 10000,
                        e.Album.Title,
                        e.Album.CoverPicture.StreamHash as long?,
                        e.Album.Genre.Name,
                        e.Album.Added,
                        e.Starred,
                        e.TracksCount,
                        e.Duration),
                })
                .AsAsyncEnumerable()
                // order by album title using culture-aware comparison
                .OrderBy(e => e.AlbumSortTitle, comparer)
                // ensure stable ordering
                .ThenBy(a => a.AlbumId)
                .Select(e => e.Item)
                .ToArray(cancellationToken).ConfigureAwait(false);

            return new Subsonic.AlbumList2()
            {
                album = albums,
            };
        }

        internal static async Task<Subsonic.AlbumList2> GetAlbumList2OrderedByArtistNameAsync(MediaInfoContext dbContext, int apiUserId, int? musicFolderId, int offset, int count, CancellationToken cancellationToken)
        {
            var comparer = CultureInfo.CurrentCulture.CompareInfo.GetStringComparer(CompareOptions.IgnoreCase);

            int[] albumIds = await dbContext.Albums
                // where album has an artist
                .Where(a => a.Artist != null)
                .WhereIsNotPlaceholder()
                .WhereIsAccessibleBy(dbContext, apiUserId, musicFolderId)
                .Select(a => new
                {
                    a.AlbumId,
                    AlbumArtistSortName = a.Artist.SortName ?? a.Artist.Name,
                    AlbumDate = a.Date,
                    AlbumSortTitle = a.SortTitle ?? a.Title,
                })
                .AsAsyncEnumerable()
                // order by album artist name using culture-aware comparison
                .OrderBy(e => e.AlbumArtistSortName, comparer)
                // order by album date
                .ThenBy(e => e.AlbumDate)
                // order by album title using culture-aware comparison
                .ThenBy(e => e.AlbumSortTitle, comparer)
                // ensure stable ordering for pagination
                .ThenBy(e => e.AlbumId)
                .Select(e => e.AlbumId)
                // paginate
                .Skip(offset)
                .Take(count)
                .ToArray(cancellationToken).ConfigureAwait(false);

            if (albumIds.Length == 0 &&
                musicFolderId != null &&
                !dbContext.Libraries.Any(l => l.LibraryId == musicFolderId))
            {
                throw RestApiErrorException.DataNotFoundError();
            }

            IQueryable<Track> tracksQuery = dbContext.Tracks
                .WhereIsAccessibleBy(apiUserId, musicFolderId);

            Subsonic.AlbumID3[] albums = await dbContext.Albums
                // where album is in results
                .Where(a => albumIds.Contains(a.AlbumId))
                .WithStarredBy(dbContext, apiUserId)
                .WithTracksCount(dbContext, tracksQuery)
                .Select(e => new
                {
                    e.Album.AlbumId,
                    AlbumArtistSortName = e.Album.Artist.SortName ?? e.Album.Artist.Name,
                    AlbumDate = e.Album.Date,
                    AlbumSortTitle = e.Album.SortTitle ?? e.Album.Title,
                    Item = CreateAlbumID3(
                        e.Album.ArtistId,
                        e.Album.Artist.Name,
                        e.Album.AlbumId,
                        e.Album.Date / 10000,
                        e.Album.Title,
                        e.Album.CoverPicture.StreamHash as long?,
                        e.Album.Genre.Name,
                        e.Album.Added,
                        e.Starred,
                        e.TracksCount,
                        e.Duration),
                })
                .AsAsyncEnumerable()
                // order by album artist name using culture-aware comparison
                .OrderBy(e => e.AlbumArtistSortName, comparer)
                // then by album date
                .ThenBy(e => e.AlbumDate)
                // then by album title using culture-aware comparison
                .ThenBy(e => e.AlbumSortTitle, comparer)
                // ensure stable ordering
                .ThenBy(a => a.AlbumId)
                .Select(e => e.Item)
                .ToArray(cancellationToken).ConfigureAwait(false);

            return new Subsonic.AlbumList2()
            {
                album = albums,
            };
        }

        internal static async Task<Subsonic.AlbumList2> GetAlbumList2StarredAsync(MediaInfoContext dbContext, int apiUserId, int? musicFolderId, int offset, int count, CancellationToken cancellationToken)
        {
            var comparer = CultureInfo.CurrentCulture.CompareInfo.GetStringComparer(CompareOptions.IgnoreCase);

            int[] albumIds = await dbContext.Albums
                .WhereIsAccessibleBy(dbContext, apiUserId, musicFolderId)
                .WhereIsStarredBy(dbContext, apiUserId)
                .Select(e => new
                {
                    e.Album.AlbumId,
                    AlbumArtistSortName = e.Album.Artist.SortName ?? e.Album.Artist.Name,
                    AlbumDate = e.Album.Date,
                    AlbumSortTitle = e.Album.SortTitle ?? e.Album.Title,
                })
                .AsAsyncEnumerable()
                // order by album title using culture-aware comparison
                .OrderBy(e => e.AlbumSortTitle, comparer)
                // ensure stable ordering for pagination
                .ThenBy(e => e.AlbumId)
                .Select(e => e.AlbumId)
                // paginate
                .Skip(offset)
                .Take(count)
                .ToArray(cancellationToken).ConfigureAwait(false);

            if (albumIds.Length == 0 &&
                musicFolderId != null &&
                !dbContext.Libraries.Any(l => l.LibraryId == musicFolderId))
            {
                throw RestApiErrorException.DataNotFoundError();
            }

            IQueryable<Track> tracksQuery = dbContext.Tracks
                .WhereIsAccessibleBy(apiUserId, musicFolderId);

            Subsonic.AlbumID3[] albums = await dbContext.Albums
                // where album is in results
                .Where(a => albumIds.Contains(a.AlbumId))
                .WithStarredBy(dbContext, apiUserId)
                .WithTracksCount(dbContext, tracksQuery)
                .Select(e => new
                {
                    e.Album.AlbumId,
                    AlbumArtistSortName = e.Album.Artist.SortName ?? e.Album.Artist.Name,
                    AlbumDate = e.Album.Date,
                    AlbumSortTitle = e.Album.SortTitle ?? e.Album.Title,
                    Item = CreateAlbumID3(
                        e.Album.ArtistId,
                        e.Album.Artist.Name,
                        e.Album.AlbumId,
                        e.Album.Date / 10000,
                        e.Album.Title,
                        e.Album.CoverPicture.StreamHash as long?,
                        e.Album.Genre.Name,
                        e.Album.Added,
                        e.Starred,
                        e.TracksCount,
                        e.Duration),
                })
                .AsAsyncEnumerable()
                // order by album title using culture-aware comparison
                .OrderBy(e => e.AlbumSortTitle, comparer)
                // ensure stable ordering
                .ThenBy(e => e.AlbumId)
                .Select(e => e.Item)
                .ToArray(cancellationToken).ConfigureAwait(false);

            return new Subsonic.AlbumList2()
            {
                album = albums,
            };
        }

        internal static async Task<Subsonic.AlbumList2> GetAlbumList2ByYearAsync(MediaInfoContext dbContext, int apiUserId, int? musicFolderId, int offset, int count, int fromYear, int toYear, CancellationToken cancellationToken)
        {
            var comparer = CultureInfo.CurrentCulture.CompareInfo.GetStringComparer(CompareOptions.IgnoreCase);

            var albumIdsEnumerable = dbContext.Albums
                // where album year is in requested year range
                .Where(a => fromYear <= toYear
                    ? a.Date >= fromYear * 10000 && a.Date < (toYear + 1) * 10000
                    : a.Date >= toYear * 10000 && a.Date < (fromYear + 1) * 10000)
                .WhereIsNotPlaceholder()
                .WhereIsAccessibleBy(dbContext, apiUserId, musicFolderId)
                .Select(a => new
                {
                    a.AlbumId,
                    AlbumDate = a.Date,
                    AlbumSortTitle = a.SortTitle ?? a.Title,
                })
                .AsAsyncEnumerable();

            if (fromYear <= toYear)
            {
                albumIdsEnumerable = albumIdsEnumerable
                    // order by album date
                    .OrderBy(e => e.AlbumDate)
                    // then by album title using culture-aware comparison
                    .ThenBy(e => e.AlbumSortTitle, comparer)
                    // ensure stable ordering for pagination
                    .ThenBy(e => e.AlbumId);
            }
            else
            {
                albumIdsEnumerable = albumIdsEnumerable
                    // order by album date
                    .OrderByDescending(e => e.AlbumDate)
                    // then by album title using culture-aware comparison
                    .ThenByDescending(e => e.AlbumSortTitle, comparer)
                    // ensure stable ordering for pagination
                    .ThenByDescending(e => e.AlbumId);
            }

            int[] albumIds = await albumIdsEnumerable
                .Select(e => e.AlbumId)
                // paginate
                .Skip(offset)
                .Take(count)
                .ToArray(cancellationToken).ConfigureAwait(false);

            if (albumIds.Length == 0 &&
                musicFolderId != null &&
                !dbContext.Libraries.Any(l => l.LibraryId == musicFolderId))
            {
                throw RestApiErrorException.DataNotFoundError();
            }

            IQueryable<Track> tracksQuery = dbContext.Tracks
                .WhereIsAccessibleBy(apiUserId, musicFolderId);

            var albumsEnumerable = dbContext.Albums
                // where album is in results
                .Where(a => albumIds.Contains(a.AlbumId))
                .WithStarredBy(dbContext, apiUserId)
                .WithTracksCount(dbContext, tracksQuery)
                .Select(e => new
                {
                    e.Album.AlbumId,
                    AlbumDate = e.Album.Date,
                    AlbumSortTitle = e.Album.SortTitle ?? e.Album.Title,
                    Item = CreateAlbumID3(
                        e.Album.ArtistId,
                        e.Album.Artist.Name,
                        e.Album.AlbumId,
                        e.Album.Date / 10000,
                        e.Album.Title,
                        e.Album.CoverPicture.StreamHash as long?,
                        e.Album.Genre.Name,
                        e.Album.Added,
                        e.Starred,
                        e.TracksCount,
                        e.Duration),
                })
                .AsAsyncEnumerable();

            if (fromYear <= toYear)
            {
                albumsEnumerable = albumsEnumerable
                    // order by album date
                    .OrderBy(e => e.AlbumDate)
                    // then by album title using culture-aware comparison
                    .ThenBy(e => e.AlbumSortTitle, comparer)
                    // ensure stable ordering
                    .ThenBy(a => a.AlbumId);
            }
            else
            {
                albumsEnumerable = albumsEnumerable
                    // order by album date
                    .OrderByDescending(e => e.AlbumDate)
                    // then by album title using culture-aware comparison
                    .ThenByDescending(e => e.AlbumSortTitle, comparer)
                    // ensure stable ordering
                    .ThenByDescending(a => a.AlbumId);
            }

            Subsonic.AlbumID3[] albums = await albumsEnumerable
                .Select(e => e.Item)
                .ToArray(cancellationToken).ConfigureAwait(false);

            return new Subsonic.AlbumList2()
            {
                album = albums,
            };
        }

        internal static async Task<Subsonic.AlbumList2> GetAlbumList2ByGenreAsync(MediaInfoContext dbContext, int apiUserId, int? musicFolderId, int offset, int count, string genre, CancellationToken cancellationToken)
        {
            var comparer = CultureInfo.CurrentCulture.CompareInfo.GetStringComparer(CompareOptions.IgnoreCase);

            int? genreId = await dbContext.Genres
                .Where(g => g.Name == genre)
                .Select(g => g.GenreId as int?)
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (genreId == null)
            {
                return new Subsonic.AlbumList2()
                {
                    album = Array.Empty<Subsonic.AlbumID3>(),
                };
            }

            IQueryable<Track> tracksQuery = dbContext.Tracks
                .WhereIsAccessibleBy(apiUserId, musicFolderId);

            int[] albumIds = await dbContext.Albums
                .WhereIsNotPlaceholder()
                .WhereHasTrackWithGenre(dbContext, tracksQuery, (int)genreId)
                .Select(a => new
                {
                    a.AlbumId,
                    AlbumArtistSortName = a.Artist.SortName ?? a.Artist.Name,
                    AlbumDate = a.Date,
                    AlbumSortTitle = a.SortTitle ?? a.Title,
                })
                .AsAsyncEnumerable()
                // order by album title using culture-aware comparison
                .OrderBy(e => e.AlbumSortTitle, comparer)
                // ensure stable ordering for pagination
                .ThenBy(e => e.AlbumId)
                .Select(e => e.AlbumId)
                // paginate
                .Skip(offset)
                .Take(count)
                .ToArray(cancellationToken).ConfigureAwait(false);

            if (albumIds.Length == 0 &&
                musicFolderId != null &&
                !dbContext.Libraries.Any(l => l.LibraryId == musicFolderId))
            {
                throw RestApiErrorException.DataNotFoundError();
            }

            Subsonic.AlbumID3[] albums = await dbContext.Albums
                // where album is in results
                .Where(a => albumIds.Contains(a.AlbumId))
                .WithStarredBy(dbContext, apiUserId)
                .WithTracksCount(dbContext, tracksQuery)
                .Select(e => new
                {
                    e.Album.AlbumId,
                    AlbumArtistSortName = e.Album.Artist.SortName ?? e.Album.Artist.Name,
                    AlbumDate = e.Album.Date,
                    AlbumSortTitle = e.Album.SortTitle ?? e.Album.Title,
                    Item = CreateAlbumID3(
                        e.Album.ArtistId,
                        e.Album.Artist.Name,
                        e.Album.AlbumId,
                        e.Album.Date / 10000,
                        e.Album.Title,
                        e.Album.CoverPicture.StreamHash as long?,
                        e.Album.Genre.Name,
                        e.Album.Added,
                        e.Starred,
                        e.TracksCount,
                        e.Duration)
                })
                .AsAsyncEnumerable()
                // order by album title using culture-aware comparison
                .OrderBy(e => e.AlbumSortTitle, comparer)
                // ensure stable ordering
                .ThenBy(a => a.AlbumId)
                .Select(e => e.Item)
                .ToArray(cancellationToken).ConfigureAwait(false);

            return new Subsonic.AlbumList2()
            {
                album = albums,
            };
        }

        internal static async Task<Subsonic.Songs> GetRandomSongsAsync(MediaInfoContext dbContext, int apiUserId, int? musicFolderId, string genre, int? fromYear, int? toYear, int count, string transcodedSuffix, CancellationToken cancellationToken)
        {
            int? genreId = genre == null
                ? null
                : await dbContext.Genres
                    .Where(g => g.Name == genre)
                    .Select(g => g.GenreId as int?)
                    .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            Subsonic.Child[] tracks = await dbContext.Tracks
                .WhereIsAccessibleBy(apiUserId, musicFolderId)
                // where tracks have requested genre (if specified)
                .Where(t => genreId == null || t.TrackGenres.Any(tg => tg.GenreId == genreId))
                // where album year is in requested year range
                .Where(t => !fromYear.HasValue || t.Date >= fromYear.Value * 10000)
                .Where(t => !toYear.HasValue || t.Date < (toYear.Value + 1) * 10000)
                // order randomly
                .OrderBy(_ => MediaInfoContext.Random())
                // limit number of results
                .Take(count)
                .WithStarredBy(dbContext, apiUserId)
                .Select(e => CreateTrackChild(
                    e.Track.File.Name,
                    e.Track.File.Size,
                    e.Track.ArtistId,
                    e.Track.Artist.Name,
                    e.Track.AlbumId,
                    e.Track.Album.Title,
                    e.Track.TrackId,
                    e.Track.BitRate,
                    e.Track.Duration,
                    e.Track.Date / 10000,
                    e.Track.DiscNumber,
                    e.Track.TrackNumber,
                    e.Track.Title,
                    e.Track.CoverPicture.StreamHash as long?,
                    e.Track.Genre.Name,
                    e.Track.Added,
                    e.Starred,
                    transcodedSuffix))
                .ToArrayAsync(cancellationToken).ConfigureAwait(false);

            return new Subsonic.Songs()
            {
                song = tracks,
            };
        }

        internal static async Task<Subsonic.Songs> GetSongsByGenreAsync(MediaInfoContext dbContext, int apiUserId, int? musicFolderId, string genre, int offset, int count, string transcodedSuffix, CancellationToken cancellationToken)
        {
            var comparer = CultureInfo.CurrentCulture.CompareInfo.GetStringComparer(CompareOptions.IgnoreCase);

            int? genreId = await dbContext.Genres
                .Where(g => g.Name == genre)
                .Select(g => g.GenreId as int?)
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (genreId == null)
            {
                return new Subsonic.Songs()
                {
                    song = Array.Empty<Subsonic.Child>(),
                };
            }

            int[] trackIds = await dbContext.Tracks
                .WhereIsAccessibleBy(apiUserId, musicFolderId)
                .WhereHasGenre(dbContext, (int)genreId)
                .Select(t => new
                {
                    t.TrackId,
                    TrackSortTitle = t.SortTitle ?? t.Title,
                })
                .AsAsyncEnumerable()
                // order by track title using culture-aware comparison
                .OrderBy(e => e.TrackSortTitle, comparer)
                // ensure stable ordering for pagination
                .ThenBy(e => e.TrackId)
                .Select(e => e.TrackId)
                // paginate
                .Skip(offset)
                .Take(count)
                .ToArray(cancellationToken).ConfigureAwait(false);

            Subsonic.Child[] tracks = await dbContext.Tracks
                // where track has requested genre
                .Where(t => trackIds.Contains(t.TrackId))
                .WithStarredBy(dbContext, apiUserId)
                .Select(e => new
                {
                    e.Track.TrackId,
                    TrackSortTitle = e.Track.SortTitle ?? e.Track.Title,
                    Item = CreateTrackChild(
                        e.Track.File.Name,
                        e.Track.File.Size,
                        e.Track.ArtistId,
                        e.Track.Artist.Name,
                        e.Track.AlbumId,
                        e.Track.Album.Title,
                        e.Track.TrackId,
                        e.Track.BitRate,
                        e.Track.Duration,
                        e.Track.Date / 10000,
                        e.Track.DiscNumber,
                        e.Track.TrackNumber,
                        e.Track.Title,
                        e.Track.CoverPicture.StreamHash as long?,
                        e.Track.Genre.Name,
                        e.Track.Added,
                        e.Starred,
                        transcodedSuffix)
                })
                .AsAsyncEnumerable()
                // order by track title using culture-aware comparison
                .OrderBy(e => e.TrackSortTitle, comparer)
                // ensure stable ordering for pagination
                .ThenBy(e => e.TrackId)
                .Select(e => e.Item)
                .ToArray(cancellationToken).ConfigureAwait(false);

            return new Subsonic.Songs()
            {
                song = tracks,
            };
        }

        internal static async Task<Subsonic.Starred2> GetStarred2Async(MediaInfoContext dbContext, int apiUserId, int? musicFolderId, string transcodedSuffix, CancellationToken cancellationToken)
        {
            var comparer = CultureInfo.CurrentCulture.CompareInfo.GetStringComparer(CompareOptions.IgnoreCase);

            IQueryable<Track> tracksQuery = dbContext.Tracks
                .WhereIsAccessibleBy(apiUserId, musicFolderId);

            Subsonic.ArtistID3[] artists = await dbContext.Artists
                .WhereIsStarredBy(dbContext, apiUserId)
                .WithAlbumsCount(dbContext, tracksQuery)
                .Select(e => new
                {
                    ArtistSortName = e.Artist.SortName ?? e.Artist.Name,
                    Item = CreateArtistID3(
                        e.Artist.ArtistId,
                        e.Artist.Name,
                        e.Starred,
                        e.AlbumsCount),
                })
                .AsAsyncEnumerable()
                // order by artist name using culture-aware comparison
                .OrderBy(e => e.ArtistSortName, comparer)
                .Select(e => e.Item)
                .ToArray(cancellationToken).ConfigureAwait(false);

            Subsonic.AlbumID3[] albums = await dbContext.Albums
                .WhereIsStarredBy(dbContext, apiUserId)
                .WithTracksCount(dbContext, tracksQuery)
                .Select(e => new
                {
                    e.Album.AlbumId,
                    AlbumArtistSortName = e.Album.Artist.SortName ?? e.Album.Artist.Name,
                    AlbumDate = e.Album.Date,
                    AlbumSortTitle = e.Album.SortTitle ?? e.Album.Title,
                    Item = CreateAlbumID3(
                        e.Album.ArtistId,
                        e.Album.Artist.Name,
                        e.Album.AlbumId,
                        e.Album.Date / 10000,
                        e.Album.Title,
                        e.Album.CoverPicture.StreamHash as long?,
                        e.Album.Genre.Name,
                        e.Album.Added,
                        e.Starred,
                        e.TracksCount,
                        e.Duration),
                })
                .AsAsyncEnumerable()
                // order by album title using culture-aware comparison
                .OrderBy(e => e.AlbumSortTitle, comparer)
                .Select(e => e.Item)
                .ToArray(cancellationToken).ConfigureAwait(false);

            Subsonic.Child[] tracks = await dbContext.Tracks
                .WhereIsStarredBy(dbContext, apiUserId)
                .Select(e => new
                {
                    e.Track.TrackId,
                    TrackSortTitle = e.Track.SortTitle ?? e.Track.Title,
                    Item = CreateTrackChild(
                        e.Track.File.Name,
                        e.Track.File.Size,
                        e.Track.ArtistId,
                        e.Track.Artist.Name,
                        e.Track.AlbumId,
                        e.Track.Album.Title,
                        e.Track.TrackId,
                        e.Track.BitRate,
                        e.Track.Duration,
                        e.Track.Date / 10000,
                        e.Track.DiscNumber,
                        e.Track.TrackNumber,
                        e.Track.Title,
                        e.Track.CoverPicture.StreamHash as long?,
                        e.Track.Genre.Name,
                        e.Track.Added,
                        e.Starred,
                        transcodedSuffix)
                })
                .AsAsyncEnumerable()
                // order by track title using culture-aware comparison
                .OrderBy(e => e.TrackSortTitle, comparer)
                // ensure stable ordering for pagination
                .ThenBy(e => e.TrackId)
                .Select(e => e.Item)
                .ToArray(cancellationToken).ConfigureAwait(false);

            return new Subsonic.Starred2()
            {
                artist = artists,
                album = albums,
                song = tracks,
            };
        }

        #endregion

        #region Searching

        internal static async Task<Subsonic.SearchResult3> GetSearch3ResultsAsync(MediaInfoContext dbContext, int apiUserId, int? musicFolderId, string query, int artistOffset, int artistCount, int albumOffset, int albumCount, int songOffset, int songCount, string transcodedSuffix, CancellationToken cancellationToken)
        {
            var positiveTerms = new List<string>();
            var negativeTerms = new List<string>();

            foreach (string term in query.Split(" ", 100, StringSplitOptions.RemoveEmptyEntries))
                if (term.StartsWith('-'))
                    negativeTerms.Add(" " + term.Substring(1) + " ");
                else
                    positiveTerms.Add(" " + term + " ");

            Subsonic.ArtistID3[] artists;
            Subsonic.AlbumID3[] albums;
            Subsonic.Child[] tracks;

            IQueryable<Track> tracksQuery = dbContext.Tracks
                .WhereIsAccessibleBy(apiUserId, musicFolderId);

            if (artistCount == 0)
            {
                artists = Array.Empty<Subsonic.ArtistID3>();
            }
            else
            {
                int[] artistIds = await tracksQuery
                    .Select(t => new
                    {
                        t.ArtistId,
                    })
                    .Distinct()
                    .Join(dbContext.Artists, e => e.ArtistId, a => a.ArtistId, (e, a) => new
                    {
                        a.ArtistId,
                        ArtistName = a.Name,
                    })
                    // where artist is not a placeholder for non-artist tracks
                    .Where(a => a.ArtistName != null)
                    .OrderBy(t => t.ArtistId)
                    .AsAsyncEnumerable()
                    // where artist matches search query
                    .Where(e => Match(e.ArtistName))
                    .Select(e => e.ArtistId)
                    // paginate
                    .Skip(artistOffset)
                    .Take(artistCount)
                    .ToArray(cancellationToken).ConfigureAwait(false);

                artists = await dbContext.Artists
                    // where artist is search hit
                    .Where(a => artistIds.Contains(a.ArtistId))
                    .WithStarredBy(dbContext, apiUserId)
                    .WithAlbumsCount(dbContext, tracksQuery)
                    .Select(e => CreateArtistID3(
                        e.Artist.ArtistId,
                        e.Artist.Name,
                        e.Starred,
                        e.AlbumsCount))
                    .ToArrayAsync(cancellationToken).ConfigureAwait(false);
            }

            if (artistCount == 0)
            {
                albums = Array.Empty<Subsonic.AlbumID3>();
            }
            else
            {
                int[] albumIds = await tracksQuery
                    .Select(t => new
                    {
                        t.AlbumId,
                    })
                    .Distinct()
                    .Join(dbContext.Albums, e => e.AlbumId, a => a.AlbumId, (e, a) => a)
                    .WhereIsNotPlaceholder()
                    .Select(a => new
                    {
                        a.AlbumId,
                        ArtistName = a.Artist.Name,
                        AlbumTitle = a.Title,
                    })
                    .OrderBy(t => t.AlbumId)
                    .AsAsyncEnumerable()
                    // where album matches search query
                    .Where(e => Match(e.ArtistName, e.AlbumTitle))
                    .Select(e => e.AlbumId)
                    // paginate
                    .Skip(albumOffset)
                    .Take(albumCount)
                    .ToArray(cancellationToken).ConfigureAwait(false);

                albums = await dbContext.Albums
                    // where album is a search hit
                    .Where(a => albumIds.Contains(a.AlbumId))
                    .WithStarredBy(dbContext, apiUserId)
                    .WithTracksCount(dbContext, tracksQuery)
                    .Select(e => CreateAlbumID3(
                        e.Album.ArtistId,
                        e.Album.Artist.Name,
                        e.Album.AlbumId,
                        e.Album.Date / 10000,
                        e.Album.Title,
                        e.Album.CoverPicture.StreamHash as long?,
                        e.Album.Genre.Name,
                        e.Album.Added,
                        e.Starred,
                        e.TracksCount,
                        e.Duration))
                    .ToArrayAsync(cancellationToken).ConfigureAwait(false);
            }

            if (songCount == 0)
            {
                tracks = Array.Empty<Subsonic.Child>();
            }
            else
            {
                int[] trackIds = await tracksQuery
                    .Select(t => new
                    {
                        t.TrackId,
                        ArtistName = t.Artist.Name,
                        AlbumTitle = t.Album.Title,
                        t.Title,
                    })
                    .OrderBy(t => t.TrackId)
                    .AsAsyncEnumerable()
                    // where track matches search query
                    .Where(e => Match(e.ArtistName, e.AlbumTitle, e.Title))
                    .Select(t => t.TrackId)
                    // paginate
                    .Skip(songOffset)
                    .Take(songCount)
                    .ToArray(cancellationToken).ConfigureAwait(false);

                tracks = await dbContext.Tracks
                    // where track is a search hit
                    .Where(t => trackIds.Contains(t.TrackId))
                    .WithStarredBy(dbContext, apiUserId)
                    .Select(e => CreateTrackChild(
                        e.Track.File.Name,
                        e.Track.File.Size,
                        e.Track.ArtistId,
                        e.Track.Artist.Name,
                        e.Track.AlbumId,
                        e.Track.Album.Title,
                        e.Track.TrackId,
                        e.Track.BitRate,
                        e.Track.Duration,
                        e.Track.Date / 10000,
                        e.Track.DiscNumber,
                        e.Track.TrackNumber,
                        e.Track.Title,
                        e.Track.CoverPicture.StreamHash as long?,
                        e.Track.Genre.Name,
                        e.Track.Added,
                        e.Starred,
                        transcodedSuffix))
                    .ToArrayAsync(cancellationToken).ConfigureAwait(false);
            }

            return new Subsonic.SearchResult3()
            {
                artist = artists,
                album = albums,
                song = tracks,
            };

            bool Match(string s1, string s2 = null, string s3 = null)
            {
                foreach (string term in positiveTerms)
                {
                    if (s1 != null && (" " + s1 + " ").Contains(term, StringComparison.CurrentCultureIgnoreCase))
                        continue;
                    if (s2 != null && (" " + s2 + " ").Contains(term, StringComparison.CurrentCultureIgnoreCase))
                        continue;
                    if (s3 != null && (" " + s3 + " ").Contains(term, StringComparison.CurrentCultureIgnoreCase))
                        continue;

                    return false;
                }

                foreach (string term in negativeTerms)
                {
                    if (s1 != null && (" " + s1 + " ").Contains(term, StringComparison.CurrentCultureIgnoreCase))
                        return false;
                    if (s2 != null && (" " + s2 + " ").Contains(term, StringComparison.CurrentCultureIgnoreCase))
                        return false;
                    if (s3 != null && (" " + s3 + " ").Contains(term, StringComparison.CurrentCultureIgnoreCase))
                        return false;
                }

                return true;
            }
        }

        #endregion

        #region Playlists

        internal static async Task<Subsonic.Playlists> GetPlaylistsAsync(MediaInfoContext dbContext, int apiUserId, CancellationToken cancellationToken)
        {
            IQueryable<Track> tracksQuery = dbContext.Tracks
                .WhereIsAccessibleBy(apiUserId);

            var playlistIdsWithTracksCountQuery = dbContext.Playlists
                // find playlist tracks (includes playlists without tracks)
                .GroupJoin(dbContext.PlaylistTracks, p => p.PlaylistId, pt => pt.PlaylistId, (p, pts) => new
                {
                    p.PlaylistId,
                    PlaylistTracks = pts,
                })
                .SelectMany(e => e.PlaylistTracks.DefaultIfEmpty(), (e, pt) => new
                {
                    e.PlaylistId,
                    pt.TrackId,
                })
                .GroupJoin(tracksQuery, e => e.TrackId, t => t.TrackId, (e, ts) => new
                {
                    e.PlaylistId,
                    Tracks = ts,
                })
                .SelectMany(e => e.Tracks.DefaultIfEmpty(), (e, t) => new
                {
                    e.PlaylistId,
                    TrackOne = t != null ? 1 : 0,
                    TrackDuration = t.Duration ?? 0,
                })
                .GroupBy(e => e.PlaylistId)
                // count playlist tracks and compute playlist duration
                .Select(grouping => new
                {
                    PlaylistId = grouping.Key,
                    TracksCount = grouping.Sum(e => e.TrackOne),
                    Duration = grouping.Sum(e => e.TrackDuration),
                });

            Subsonic.Playlist[] playlists = await dbContext.Playlists
                // where playlist is owned by user or is public
                .Where(p => p.UserId == apiUserId || p.IsPublic)
                // include track aggregations (excludes playlists without tracks)
                .Join(playlistIdsWithTracksCountQuery, p => p.PlaylistId, e => e.PlaylistId, (p, e) => new
                {
                    Playlist = p,
                    e.TracksCount,
                    e.Duration,
                })
                .Select(e => CreatePlaylist(
                    e.Playlist.PlaylistId,
                    e.Playlist.Name,
                    e.Playlist.Description,
                    e.Playlist.User.Name,
                    e.Playlist.IsPublic,
                    e.Playlist.Created,
                    e.Playlist.Modified,
                    e.TracksCount,
                    e.Duration))
                .ToArrayAsync(cancellationToken).ConfigureAwait(false);

            return new Subsonic.Playlists
            {
                playlist = playlists,
            };
        }

        internal static async Task<Subsonic.PlaylistWithSongs> GetPlaylistAsync(MediaInfoContext dbContext, int apiUserId, int playlistId, string transcodedSuffix, CancellationToken cancellationToken)
        {
            Subsonic.PlaylistWithSongs playlist = await dbContext.Playlists
                // where playlist is requested playlist
                .Where(p => p.PlaylistId == playlistId)
                // where playlist is owned by user or is public
                .Where(p => p.UserId == apiUserId || p.IsPublic)
                .Select(p => CreatePlaylistWithSongs(
                    p.PlaylistId,
                    p.Name,
                    p.Description,
                    p.User.Name,
                    p.IsPublic,
                    p.Created,
                    p.Modified))
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (playlist == null)
                throw RestApiErrorException.DataNotFoundError();

            var playlistTracksQuery = dbContext.PlaylistTracks
                // where track is on requested playlist
                .Where(pt => pt.PlaylistId == playlistId);

            Subsonic.Child[] tracks = await dbContext.Tracks
                .WhereIsAccessibleBy(apiUserId)
                .WithStarredBy(dbContext, apiUserId)
                .Join(playlistTracksQuery, e => e.Track.TrackId, pt => pt.TrackId, (e, pt) => new
                {
                    e.Track,
                    e.Starred,
                    PlaylistIndex = pt.Index,
                })
                // order by playlist index
                .OrderBy(e => e.PlaylistIndex)
                .Select(e => CreateTrackChild(
                    e.Track.File.Name,
                    e.Track.File.Size,
                    e.Track.ArtistId,
                    e.Track.Artist.Name,
                    e.Track.AlbumId,
                    e.Track.Album.Title,
                    e.Track.TrackId,
                    e.Track.BitRate,
                    e.Track.Duration,
                    e.Track.Date / 10000,
                    e.Track.DiscNumber,
                    e.Track.TrackNumber,
                    e.Track.Title,
                    e.Track.CoverPicture.StreamHash as long?,
                    e.Track.Genre.Name,
                    e.Track.Added,
                    e.Starred,
                    transcodedSuffix))
                .ToArrayAsync(cancellationToken).ConfigureAwait(false);

            return playlist.SetSongs(tracks);
        }

        internal static async Task<int> CreatePlaylistAsync(MediaInfoContext dbContext, int apiUserId, string name, CancellationToken cancellationToken)
        {
            var playlist = new Playlist
            {
                UserId = apiUserId,
                Name = name,
                Description = null,
                IsPublic = false,
                Created = DateTime.UtcNow,
                Modified = DateTime.UtcNow,
            };
            await dbContext.Playlists.AddAsync(playlist, cancellationToken).ConfigureAwait(false);

            return dbContext.Entry(playlist).Property(p => p.PlaylistId).CurrentValue;
        }

        internal static async Task RecreatePlaylistAsync(MediaInfoContext dbContext, int apiUserId, int playlistId, string name, CancellationToken cancellationToken)
        {
            Playlist playlist = await dbContext.Playlists
                // where playlist is requested playlist
                .Where(p => p.PlaylistId == playlistId)
                // where playlist is owned by user or is public
                .Where(p => p.UserId == apiUserId || p.IsPublic)
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (playlist == null)
                throw RestApiErrorException.DataNotFoundError();

            if (playlist.UserId != apiUserId)
                throw RestApiErrorException.UserNotAuthorizedError();

            if (name != null)
                playlist.Name = name;
            playlist.Modified = DateTime.UtcNow;
        }

        internal static async Task SetPlaylistSongsAsync(MediaInfoContext dbContext, int apiUserId, int playlistId, int[] songIds, CancellationToken cancellationToken)
        {
            // NOTE: Assumes playlist ownership has already been checked.

            await dbContext.PlaylistTracks
                // where playlist track is in requested playlist
                .Where(pt => pt.PlaylistId == playlistId)
                // remove each track from playlist
                .ForEachAsync(pt => dbContext.Remove(pt), cancellationToken).ConfigureAwait(false);

            if (!await CanAddTracksAsync(dbContext, apiUserId, songIds, cancellationToken).ConfigureAwait(false))
                throw RestApiErrorException.DataNotFoundError();

            for (int i = 0; i < songIds.Length; ++i)
            {
                var playlistTrack = new PlaylistTrack
                {
                    PlaylistId = playlistId,
                    TrackId = songIds[i],
                    Index = i,
                };
                await dbContext.PlaylistTracks.AddAsync(playlistTrack, cancellationToken).ConfigureAwait(false);
            }
        }

        internal static async Task UpdatePlaylistAsync(MediaInfoContext dbContext, int apiUserId, int playlistId, string name, string comment, bool? @public, CancellationToken cancellationToken)
        {
            Playlist playlist = await dbContext.Playlists
                // where playlist is requested playlist
                .Where(p => p.PlaylistId == playlistId)
                // where playlist is owned by user or is public
                .Where(p => p.UserId == apiUserId || p.IsPublic)
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (playlist == null)
                throw RestApiErrorException.DataNotFoundError();

            if (playlist.UserId != apiUserId)
                throw RestApiErrorException.UserNotAuthorizedError();

            if (name != null)
                playlist.Name = name;
            if (comment != null)
                playlist.Description = comment;
            if (@public.HasValue)
                playlist.IsPublic = @public.Value;
            playlist.Modified = DateTime.UtcNow;
        }

        internal static async Task AddPlaylistSongsAsync(MediaInfoContext dbContext, int apiUserId, int playlistId, int[] songIds, CancellationToken cancellationToken)
        {
            // NOTE: Assumes playlist ownership has already been checked.

            int? lastIndex = await dbContext.PlaylistTracks
                // where playlist track is in requested playlist
                .Where(pt => pt.PlaylistId == playlistId)
                // get the maximum playlist track index
                .OrderByDescending(pt => pt.Index)
                .Select(pt => pt.Index as int?)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            if (!await CanAddTracksAsync(dbContext, apiUserId, songIds, cancellationToken).ConfigureAwait(false))
                throw RestApiErrorException.DataNotFoundError();

            int baseIndex = (lastIndex + 1) ?? 0;
            for (int i = 0; i < songIds.Length; ++i)
            {
                var playlistTrack = new PlaylistTrack
                {
                    PlaylistId = playlistId,
                    TrackId = songIds[i],
                    Index = baseIndex + i,
                };
                await dbContext.PlaylistTracks.AddAsync(playlistTrack, cancellationToken).ConfigureAwait(false);
            }
        }

        internal static async Task RemovePlaylistSongsAsync(MediaInfoContext dbContext, int apiUserId, int playlistId, int[] songIndexes, CancellationToken cancellationToken)
        {
            // NOTE: Assumes playlist ownership has already been checked.

            IQueryable<Track> tracksQuery = dbContext.Tracks
                .WhereIsAccessibleBy(apiUserId);

            foreach (int index in songIndexes)
            {
                var playlistTrack = await dbContext.PlaylistTracks
                    // where playlist track is in requested playlist
                    .Where(pt => pt.PlaylistId == playlistId)
                    // where track is accessible by user
                    .Join(tracksQuery, pt => pt.TrackId, t => t.TrackId, (pt, t) => pt)
                    // order by playlist track index
                    .OrderBy(pt => pt.Index)
                    // skip to the playlist track to be removed
                    .Skip(index)
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                if (playlistTrack == null)
                    throw RestApiErrorException.DataNotFoundError();

                dbContext.Remove(playlistTrack);
            }
        }

        internal static async Task DeletePlaylistAsync(MediaInfoContext dbContext, int apiUserId, bool canDeleteAllPublic, int playlistId, CancellationToken cancellationToken)
        {
            Playlist playlist = await dbContext.Playlists
                // where playlist is requested playlist
                .Where(p => p.PlaylistId == playlistId)
                // where playlist is owned by user or is public
                .Where(p => p.UserId == apiUserId || p.IsPublic)
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (playlist == null)
                throw RestApiErrorException.DataNotFoundError();

            if (playlist.UserId != apiUserId && !(playlist.IsPublic && canDeleteAllPublic))
                throw RestApiErrorException.UserNotAuthorizedError();

            dbContext.Playlists.Remove(playlist);
        }

        internal static async Task<bool> CanAddTracksAsync(MediaInfoContext dbContext, int apiUserId, IReadOnlyList<int> trackIds, CancellationToken cancellationToken)
        {
            foreach (int trackId in trackIds)
            {
                IQueryable<Track> trackByIdQuery = dbContext.Tracks
                    // where track is requested track
                    .Where(t => t.TrackId == trackId)
                    .WhereIsAccessibleBy(apiUserId);

                bool trackExists = await trackByIdQuery
                    .AnyAsync(cancellationToken).ConfigureAwait(false);
                if (!trackExists)
                    return false;
            }

            return true;
        }

        #endregion

        #region Media retrieval

        internal static async Task<TrackStreamInfo> GetTrackStreamInfoAsync(MediaInfoContext dbContext, int apiUserId, int trackId, CancellationToken cancellationToken)
        {
            IQueryable<Track> trackByIdQuery = dbContext.Tracks
                // where track is requested track
                .Where(t => t.TrackId == trackId)
                .WhereIsAccessibleBy(apiUserId);

            TrackStreamInfo trackStreamInfo = await trackByIdQuery
                // find files for tracks
                .Join(dbContext.Files, t => t.FileId, f => f.FileId, (t, f) => new TrackStreamInfo
                {
                    DirectoryPath = f.Directory.Path,
                    FileName = f.Name,
                    FormatName = f.FormatName,
                    StreamIndex = t.StreamIndex,
                    CodecName = t.CodecName,
                    BitRate = t.BitRate,
                    AlbumGain = t.AlbumGain,
                    TrackGain = t.TrackGain,
                })
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (trackStreamInfo == null)
                throw RestApiErrorException.DataNotFoundError();

            return trackStreamInfo;
        }

        internal static async Task<CoverArtStreamInfo> GetCoverArtStreamInfoAsync(MediaInfoContext dbContext, int apiUserId, long hash, CancellationToken cancellationToken)
        {
            CoverArtStreamInfo coverArtStreamInfo = await dbContext.Pictures
                // where picture is requested picture
                .Where(p => p.StreamHash == hash)
                .WhereIsAccessibleBy(apiUserId)
                // find files for pictures
                .Join(dbContext.Files, p => p.FileId, f => f.FileId, (p, f) => new CoverArtStreamInfo
                {
                    DirectoryPath = f.Directory.Path,
                    FileName = f.Name,
                    StreamIndex = p.StreamIndex,
                })
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (coverArtStreamInfo == null)
                throw RestApiErrorException.DataNotFoundError();

            return coverArtStreamInfo;
        }

        #endregion

        #region Media annotation

        internal static async Task StarArtistAsync(MediaInfoContext dbContext, int apiUserId, int artistId, CancellationToken cancellationToken)
        {
            bool artistExists = await dbContext.Artists
                .Where(a => a.ArtistId == artistId)
                .Select(a => a.ArtistId as int?)
                .AnyAsync(cancellationToken).ConfigureAwait(false);
            if (!artistExists)
                throw RestApiErrorException.DataNotFoundError();

            ArtistStar artistStar = await dbContext.ArtistStars
                .Where(s => s.ArtistId == artistId && s.UserId == apiUserId)
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (artistStar == null)
                dbContext.ArtistStars.Add(new ArtistStar()
                {
                    ArtistId = artistId,
                    UserId = apiUserId,
                    Added = DateTime.UtcNow,
                });
        }

        internal static async Task StarAlbumAsync(MediaInfoContext dbContext, int apiUserId, int albumId, CancellationToken cancellationToken)
        {
            bool albumExists = await dbContext.Albums
                .Where(a => a.AlbumId == albumId)
                .Select(a => a.AlbumId as int?)
                .AnyAsync(cancellationToken).ConfigureAwait(false);
            if (!albumExists)
                throw RestApiErrorException.DataNotFoundError();

            AlbumStar albumStar = await dbContext.AlbumStars
                .Where(s => s.AlbumId == albumId && s.UserId == apiUserId)
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (albumStar == null)
                dbContext.AlbumStars.Add(new AlbumStar()
                {
                    AlbumId = albumId,
                    UserId = apiUserId,
                    Added = DateTime.UtcNow,
                });
        }

        internal static async Task StarTrackAsync(MediaInfoContext dbContext, int apiUserId, int trackId, CancellationToken cancellationToken)
        {
            bool trackExists = await dbContext.Tracks
                .Where(t => t.TrackId == trackId)
                .Select(t => t.TrackId as int?)
                .AnyAsync(cancellationToken).ConfigureAwait(false);
            if (!trackExists)
                throw RestApiErrorException.DataNotFoundError();

            TrackStar trackStar = await dbContext.TrackStars
                .Where(s => s.TrackId == trackId && s.UserId == apiUserId)
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (trackStar == null)
                dbContext.TrackStars.Add(new TrackStar()
                {
                    TrackId = trackId,
                    UserId = apiUserId,
                    Added = DateTime.UtcNow,
                });
        }

        internal static async Task UnstarArtistAsync(MediaInfoContext dbContext, int apiUserId, int artistId, CancellationToken cancellationToken)
        {
            bool artistExists = await dbContext.Artists
                .Where(a => a.ArtistId == artistId)
                .Select(a => a.ArtistId as int?)
                .AnyAsync(cancellationToken).ConfigureAwait(false);
            if (!artistExists)
                throw RestApiErrorException.DataNotFoundError();

            ArtistStar artistStar = await dbContext.ArtistStars
                .Where(s => s.ArtistId == artistId && s.UserId == apiUserId)
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (artistStar != null)
                dbContext.ArtistStars.Remove(artistStar);
        }

        internal static async Task UnstarAlbumAsync(MediaInfoContext dbContext, int apiUseId, int albumId, CancellationToken cancellationToken)
        {
            bool albumExists = await dbContext.Albums
                .Where(a => a.AlbumId == albumId)
                .Select(a => a.AlbumId as int?)
                .AnyAsync(cancellationToken).ConfigureAwait(false);
            if (!albumExists)
                throw RestApiErrorException.DataNotFoundError();

            AlbumStar albumStar = await dbContext.AlbumStars
                .Where(s => s.AlbumId == albumId && s.UserId == apiUseId)
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (albumStar != null)
                dbContext.AlbumStars.Remove(albumStar);
        }

        internal static async Task UnstarTrackAsync(MediaInfoContext dbContext, int apiUserId, int trackId, CancellationToken cancellationToken)
        {
            bool trackExists = await dbContext.Tracks
                .Where(t => t.TrackId == trackId)
                .Select(t => t.TrackId as int?)
                .AnyAsync(cancellationToken).ConfigureAwait(false);
            if (!trackExists)
                throw RestApiErrorException.DataNotFoundError();

            TrackStar trackStar = await dbContext.TrackStars
                .Where(s => s.TrackId == trackId && s.UserId == apiUserId)
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (trackStar != null)
                dbContext.TrackStars.Remove(trackStar);
        }

        #endregion

        #region Jukebox

        internal static async Task<Subsonic.Child[]> GetTracksAsync(MediaInfoContext dbContext, int apiUserId, IReadOnlyList<int> trackIds, string transcodedSuffix, CancellationToken cancellationToken)
        {
            Subsonic.Child[] tracks = await dbContext.Tracks
                // where track is in playlist
                .Where(t => trackIds.Contains(t.TrackId))
                .WithStarredBy(dbContext, apiUserId)
                // find files for tracks
                .Join(dbContext.Files, e => e.Track.FileId, f => f.FileId, (e, f) => new
                {
                    File = f,
                    e.Track,
                    e.Starred,
                })
                .Select(e => CreateTrackChild(
                    e.File.Name,
                    e.File.Size,
                    e.Track.ArtistId,
                    e.Track.Artist.Name,
                    e.Track.AlbumId,
                    e.Track.Album.Title,
                    e.Track.TrackId,
                    e.Track.BitRate,
                    e.Track.Duration,
                    e.Track.Date / 10000,
                    e.Track.DiscNumber,
                    e.Track.TrackNumber,
                    e.Track.Title,
                    e.Track.CoverPicture.StreamHash as long?,
                    e.Track.Genre.Name,
                    e.Track.Added,
                    e.Starred,
                    transcodedSuffix))
                .ToArrayAsync(cancellationToken).ConfigureAwait(false);

            var entries = new Subsonic.Child[trackIds.Count];
            for (int i = 0; i < trackIds.Count; ++i)
            {
                string id = ToTrackId(trackIds[i]);
                int index = Array.FindIndex(tracks, t => t.id == id);
                entries[i] = index >= 0
                    ? tracks[index]
                    : new Subsonic.Child()
                    {
                        id = id,
                    };
            }
            return entries;
        }

        #endregion

        #region User management

        internal static async Task<Subsonic.User> GetUserAsync(MediaInfoContext dbContext, string username, CancellationToken cancellationToken)
        {
            User user = await dbContext.Users
                .Where(u => u.Name == username)
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (user == null)
                throw RestApiErrorException.DataNotFoundError();

            int[] libraryIds = await dbContext.Libraries
                // where library is accessible by user
                .Where(l => !l.IsAccessControlled || l.LibraryUsers.Any(lu => lu.UserId == user.UserId))
                .Select(l => l.LibraryId)
                .ToArrayAsync(cancellationToken).ConfigureAwait(false);

            return new Subsonic.User()
            {
                username = username,
                email = null,
                scrobblingEnabled = false,
                maxBitRate = user.MaxBitRate,
                maxBitRateSpecified = true,
                adminRole = user.IsAdmin,
                settingsRole = !user.IsGuest,
                downloadRole = false,
                uploadRole = false,
                playlistRole = true,
                coverArtRole = false,
                commentRole = false,
                podcastRole = false,
                streamRole = true,
                jukeboxRole = user.CanJukebox,
                shareRole = false,
                videoConversionRole = false,
                avatarLastChanged = default,
                avatarLastChangedSpecified = false,
                folder = libraryIds,
            };
        }

        internal static async Task<Subsonic.Users> GetUsersAsync(MediaInfoContext dbContext, CancellationToken cancellationToken)
        {
            var comparer = CultureInfo.CurrentCulture.CompareInfo.GetStringComparer(CompareOptions.IgnoreCase);

            Subsonic.User[] users = await dbContext.Users
                .GroupJoin(dbContext.LibraryUsers, u => u.UserId, lu => lu.UserId, (u, lus) => new Subsonic.User
                {
                    username = u.Name,
                    email = null,
                    scrobblingEnabled = false,
                    maxBitRate = u.MaxBitRate,
                    maxBitRateSpecified = true,
                    adminRole = u.IsAdmin,
                    settingsRole = !u.IsGuest,
                    downloadRole = false,
                    uploadRole = false,
                    playlistRole = true,
                    coverArtRole = false,
                    commentRole = false,
                    podcastRole = false,
                    streamRole = true,
                    jukeboxRole = u.CanJukebox,
                    shareRole = false,
                    videoConversionRole = false,
                    avatarLastChanged = default,
                    avatarLastChangedSpecified = false,
                    folder = lus
                        .Select(lu => lu.LibraryId)
                        .ToArray(),
                })
                .AsAsyncEnumerable()
                .OrderBy(u => u.username, comparer)
                .ToArray(cancellationToken).ConfigureAwait(false);

            return new Subsonic.Users()
            {
                user = users,
            };
        }

        internal static async Task<int> CreateUserAsync(MediaInfoContext dbContext, string username, string password, bool isAdmin, bool isGuest, bool canJukebox, CancellationToken cancellationToken)
        {
            bool userExists = await dbContext.Users
                .Where(u => u.Name == username)
                .AnyAsync(cancellationToken).ConfigureAwait(false);
            if (userExists)
                throw RestApiErrorException.GenericError("User already exists.");

            var user = new User
            {
                Name = username,
                Password = password,
                MaxBitRate = 128_000,
                IsAdmin = isAdmin,
                IsGuest = isGuest,
                CanJukebox = canJukebox,
            };
            await dbContext.Users.AddAsync(user, cancellationToken).ConfigureAwait(false);

            return dbContext.Entry(user).Property(u => u.UserId).CurrentValue;
        }

        internal static async Task<int> UpdateUserAsync(MediaInfoContext dbContext, string username, string password, int? maxBitRate, bool? isAdmin, bool? isGuest, bool? canJukebox, CancellationToken cancellationToken)
        {
            User user = await dbContext.Users
                .Where(u => u.Name == username)
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (user == null)
                throw RestApiErrorException.DataNotFoundError();

            if (password != null)
                user.Password = password;
            if (maxBitRate.HasValue)
                user.MaxBitRate = maxBitRate.Value;
            if (isAdmin.HasValue)
                user.IsAdmin = isAdmin.Value;
            if (isGuest.HasValue)
                user.IsGuest = isGuest.Value;
            if (canJukebox.HasValue)
                user.CanJukebox = canJukebox.Value;

            return dbContext.Entry(user).Property(u => u.UserId).CurrentValue;
        }

        internal static async Task SetAllUserLibrariesAsync(MediaInfoContext dbContext, int userId, CancellationToken cancellationToken)
        {
            IAsyncEnumerable<int> libraryIds = dbContext.Libraries
                .Select(l => l.LibraryId)
                .AsAsyncEnumerable();

            await libraryIds.ForEachAwaitAsync(async libraryId =>
            {
                bool libraryUserExists = await dbContext.LibraryUsers
                    .Where(lu => lu.LibraryId == libraryId && lu.UserId == userId)
                    .AnyAsync(cancellationToken).ConfigureAwait(false);
                if (!libraryUserExists)
                {
                    var libraryUser = new LibraryUser
                    {
                        LibraryId = libraryId,
                        UserId = userId,
                    };
                    await dbContext.LibraryUsers.AddAsync(libraryUser, cancellationToken).ConfigureAwait(false);
                }
            }, cancellationToken).ConfigureAwait(false);
        }

        internal static async Task SetUserLibrariesAsync(MediaInfoContext dbContext, int userId, int[] libraryIds, CancellationToken cancellationToken)
        {
            await dbContext.LibraryUsers
                .Where(lu => lu.UserId == userId && !libraryIds.Contains(lu.LibraryId))
                .ForEachAsync(lu => dbContext.Remove(lu), cancellationToken).ConfigureAwait(false);

            foreach (int libraryId in libraryIds)
            {
                bool libraryExists = await dbContext.Libraries
                    .Where(l => l.LibraryId == libraryId)
                    .AnyAsync(cancellationToken).ConfigureAwait(false);
                if (!libraryExists)
                    throw RestApiErrorException.DataNotFoundError();

                bool libraryUserExists = await dbContext.LibraryUsers
                    .Where(lu => lu.LibraryId == libraryId && lu.UserId == userId)
                    .AnyAsync(cancellationToken).ConfigureAwait(false);
                if (!libraryUserExists)
                {
                    var libraryUser = new LibraryUser
                    {
                        LibraryId = libraryId,
                        UserId = userId,
                    };
                    await dbContext.LibraryUsers.AddAsync(libraryUser, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        internal static async Task DeleteUserAsync(MediaInfoContext dbContext, string username, CancellationToken cancellationToken)
        {
            User user = await dbContext.Users
                .Where(u => u.Name == username)
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (user == null)
                throw RestApiErrorException.DataNotFoundError();

            dbContext.Users.Remove(user);
        }

        #endregion

        internal sealed class TrackStreamInfo
        {
            public string DirectoryPath;
            public string FileName;
            public string FormatName;
            public int StreamIndex;
            public string CodecName;
            public int? BitRate;
            public float? AlbumGain;
            public float? TrackGain;
        }

        internal class CoverArtStreamInfo
        {
            public string DirectoryPath;
            public string FileName;
            public int StreamIndex;
        }
    }
}
