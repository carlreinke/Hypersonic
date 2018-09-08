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
using static Hypersonic.Subsonic.Helpers;

namespace Hypersonic
{
    internal static class RestApiQueries
    {
        #region Browsing

        internal static async Task<Subsonic.MusicFolders> GetMusicFoldersAsync(MediaInfoContext dbContext, int apiUserId, CancellationToken cancellationToken)
        {
            Subsonic.MusicFolder[] musicFolders = await dbContext.Libraries
                // where library is accessible by user
                .Where(l => !l.IsAccessControlled || l.LibraryUsers.Any(lu => lu.UserId == apiUserId))
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
            IQueryable<Track> tracksQuery = GetTracksQuery(dbContext, apiUserId);

            // TODO: Albums count and tracks count should be doable in one query if EF will let us.

            var genresWithAlbumsCountQuery = dbContext.TrackGenres
                // find genre and album of tracks (also excludes genres without any tracks)
                .Join(tracksQuery, tg => tg.TrackId, t => t.TrackId, (tg, t) => new
                {
                    tg.GenreId,
                    t.AlbumId,
                })
                .Distinct()
                .GroupBy(e => e.GenreId)
                // count albums
                .Select(grouping => new
                {
                    GenreId = grouping.Key,
                    AlbumsCount = grouping.Count(),
                });

            var genresWithTracksCountQuery = dbContext.TrackGenres
                // find genre of tracks (also excludes genres without any tracks)
                .Join(tracksQuery, tg => tg.TrackId, t => t.TrackId, (tg, t) => new
                {
                    tg.GenreId,
                    t.TrackId,
                })
                .GroupBy(e => e.GenreId)
                // count tracks
                .Select(grouping => new
                {
                    GenreId = grouping.Key,
                    TracksCount = grouping.Count(),
                });

            Subsonic.Genre[] genres = await dbContext.Genres
                .Join(genresWithAlbumsCountQuery, g => g.GenreId, e => e.GenreId, (g, a) => new
                {
                    Genre = g,
                    a.AlbumsCount,
                })
                .Join(genresWithTracksCountQuery, e => e.Genre.GenreId, e => e.GenreId, (e, t) => new
                {
                    e.Genre,
                    e.AlbumsCount,
                    t.TracksCount,
                })
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

            IQueryable<Track> tracksQuery = GetTracksQuery(dbContext, apiUserId, musicFolderId);

            IQueryable<ArtistIdWithAlbumsCount> artistIdsWithAlbumsCountQuery = GetArtistIdsWithAlbumsCountQuery(dbContext, tracksQuery);

            IQueryable<ArtistStar> artistStarsQuery = GetArtistStarsQuery(dbContext, apiUserId);

            Subsonic.IndexID3[] indexes = await Task.WhenAll(await dbContext.Artists
                // include album aggregation (excludes artists without albums with tracks)
                .Join(artistIdsWithAlbumsCountQuery, a => a.ArtistId, e => e.ArtistId, (a, e) => new
                {
                    Artist = a,
                    e.AlbumsCount,
                })
                // include indication if album is starred by user
                .GroupJoin(artistStarsQuery, e => e.Artist.ArtistId, s => s.ArtistId, (e, ss) => new
                {
                    e.Artist,
                    Stars = ss,
                    e.AlbumsCount,
                })
                .SelectMany(e => e.Stars.DefaultIfEmpty(), (e, s) => new
                {
                    e.Artist,
                    Starred = s.Added as DateTime?,
                    e.AlbumsCount,
                })
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
                    if (name != null && name.Length > 0)
                    {
                        char c = char.ToUpper(name[0], CultureInfo.CurrentCulture);
                        if (char.IsLetter(c))
                            return c;
                    }
                    return '#';
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

            return new Subsonic.ArtistsID3()
            {
                index = indexes,
                ignoredArticles = "",
            };
        }

        internal static async Task<Subsonic.ArtistWithAlbumsID3> GetArtistAsync(MediaInfoContext dbContext, int apiUserId, int artistId, CancellationToken cancellationToken)
        {
            IQueryable<ArtistStar> artistStarsQuery = GetArtistStarsQuery(dbContext, apiUserId);

            Subsonic.ArtistWithAlbumsID3 artist = await dbContext.Artists
                // where artist is requested artist
                .Where(a => a.ArtistId == artistId)
                // include indication if artist is starred by user
                .GroupJoin(artistStarsQuery, a => a.ArtistId, s => s.ArtistId, (a, ss) => new
                {
                    Artist = a,
                    Stars = ss,
                })
                .SelectMany(e => e.Stars.DefaultIfEmpty(), (e, s) => new
                {
                    e.Artist,
                    Starred = s.Added as DateTime?
                })
                .Select(e => CreateArtistWithAlbumsID3(
                    e.Artist.ArtistId,
                    e.Artist.Name,
                    e.Starred))
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (artist == null)
                throw RestApiErrorException.DataNotFoundError();

            IQueryable<Track> tracksQuery = GetTracksQuery(dbContext, apiUserId);

            IQueryable<AlbumIdWithTracksCount> albumIdsWithTracksCountQuery = GetAlbumIdsWithTracksCountQuery(dbContext, tracksQuery);

            IQueryable<AlbumStar> albumStarsQuery = GetAlbumStarsQuery(dbContext, apiUserId);

            Subsonic.AlbumID3[] albums = await dbContext.Albums
                // where album is by requested artist
                .Where(a => a.ArtistId == artistId)
                // include track aggregations (excludes albums without tracks)
                .Join(albumIdsWithTracksCountQuery, a => a.AlbumId, e => e.AlbumId, (a, e) => new
                {
                    Album = a,
                    e.TracksCount,
                    e.Duration,
                })
                // include indication if album is starred by user
                .GroupJoin(albumStarsQuery, e => e.Album.AlbumId, s => s.AlbumId, (e, ss) => new
                {
                    e.Album,
                    Stars = ss,
                    e.TracksCount,
                    e.Duration,
                })
                .SelectMany(e => e.Stars.DefaultIfEmpty(), (e, s) => new
                {
                    e.Album,
                    Starred = s.Added as DateTime?,
                    e.TracksCount,
                    e.Duration,
                })
                // order by album date
                .OrderBy(e => e.Album.Date)
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

            return artist.SetAlbums(albums);
        }

        internal static async Task<Subsonic.AlbumWithSongsID3> GetAlbumAsync(MediaInfoContext dbContext, int apiUserId, int albumId, CancellationToken cancellationToken)
        {
            IQueryable<AlbumStar> albumStarsQuery = GetAlbumStarsQuery(dbContext, apiUserId);

            Subsonic.AlbumWithSongsID3 album = await dbContext.Albums
                // where album is requested album
                .Where(a => a.AlbumId == albumId)
                // include indication if album is starred by user
                .GroupJoin(albumStarsQuery, a => a.AlbumId, s => s.AlbumId, (a, ss) => new
                {
                    Album = a,
                    Stars = ss,
                })
                .SelectMany(e => e.Stars.DefaultIfEmpty(), (e, s) => new
                {
                    e.Album,
                    Starred = s.Added as DateTime?,
                })
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

            IQueryable<Track> tracksByAlbumIdQuery = GetTracksByAlbumIdQuery(dbContext, apiUserId, albumId);

            IQueryable<TrackStar> trackStarsQuery = GetTrackStarsQuery(dbContext, apiUserId);

            Subsonic.Child[] tracks = await tracksByAlbumIdQuery
                // include indication if track is starred by user
                .GroupJoin(trackStarsQuery, t => t.TrackId, s => s.TrackId, (t, ss) => new
                {
                    Track = t,
                    Stars = ss,
                })
                .SelectMany(e => e.Stars.DefaultIfEmpty(), (e, s) => new
                {
                    e.Track,
                    Starred = s.Added as DateTime?,
                })
                // order by disc number then track number
                .OrderBy(e => e.Track.DiscNumber ?? int.MaxValue)
                .ThenBy(e => e.Track.TrackNumber ?? int.MaxValue)
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
                    e.Starred))
                .ToArrayAsync(cancellationToken).ConfigureAwait(false);
            if (tracks.Length == 0)
                throw RestApiErrorException.DataNotFoundError();

            return album.SetSongs(tracks);
        }

        internal static async Task<Subsonic.Child> GetSongAsync(MediaInfoContext dbContext, int apiUserId, int trackId, CancellationToken cancellationToken)
        {
            var comparer = CultureInfo.CurrentCulture.CompareInfo.GetStringComparer(CompareOptions.IgnoreCase);

            IQueryable<Track> tracksQuery = GetTracksQuery(dbContext, apiUserId);

            IQueryable<TrackStar> trackStarsQuery = GetTrackStarsQuery(dbContext, apiUserId);

            Subsonic.Child track = await tracksQuery
                // where track is requested track
                .Where(t => t.TrackId == trackId)
                // include indication if track is starred by user
                .GroupJoin(trackStarsQuery, t => t.TrackId, s => s.TrackId, (t, ss) => new
                {
                    Track = t,
                    Stars = ss,
                })
                .SelectMany(e => e.Stars.DefaultIfEmpty(), (e, s) => new
                {
                    e.Track,
                    Starred = s.Added as DateTime?,
                })
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
                    e.Starred))
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (track == null)
                throw RestApiErrorException.DataNotFoundError();

            return track;
        }

        #endregion

        #region Album/song list

        internal static async Task<Subsonic.AlbumList2> GetAlbumList2RandomAsync(MediaInfoContext dbContext, int apiUserId, int? musicFolderId, int count, CancellationToken cancellationToken)
        {
            IQueryable<Track> tracksQuery = GetTracksQuery(dbContext, apiUserId, musicFolderId);

            IQueryable<AlbumIdWithTracksCount> albumIdsWithTracksCountQuery = GetAlbumIdsWithTracksCountQuery(dbContext, tracksQuery);

            IQueryable<AlbumStar> albumStarsQuery = GetAlbumStarsQuery(dbContext, apiUserId);

            Subsonic.AlbumID3[] albums = await dbContext.Albums
                // where album is not a placeholder for non-album tracks
                .Where(a => a.Title != null)
                // include track aggregations (excludes albums without tracks)
                .Join(albumIdsWithTracksCountQuery, a => a.AlbumId, e => e.AlbumId, (a, e) => new
                {
                    Album = a,
                    e.TracksCount,
                    e.Duration,
                })
                // include indication if album is starred by user
                .GroupJoin(albumStarsQuery, e => e.Album.AlbumId, s => s.AlbumId, (e, ss) => new
                {
                    e.Album,
                    Stars = ss,
                    e.TracksCount,
                    e.Duration,
                })
                .SelectMany(e => e.Stars.DefaultIfEmpty(), (e, s) => new
                {
                    e.Album,
                    Starred = s.Added as DateTime?,
                    e.TracksCount,
                    e.Duration,
                })
                // order randomly
                .OrderBy(a => MediaInfoContext.Random())
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

            return new Subsonic.AlbumList2()
            {
                album = albums,
            };
        }

        internal static async Task<Subsonic.AlbumList2> GetAlbumList2NewestAsync(MediaInfoContext dbContext, int apiUserId, int? musicFolderId, int offset, int count, CancellationToken cancellationToken)
        {
            IQueryable<Track> tracksQuery = GetTracksQuery(dbContext, apiUserId, musicFolderId);

            IQueryable<AlbumIdWithTracksCount> albumIdsWithTracksCountQuery = GetAlbumIdsWithTracksCountQuery(dbContext, tracksQuery);

            IQueryable<AlbumStar> albumStarsQuery = GetAlbumStarsQuery(dbContext, apiUserId);

            Subsonic.AlbumID3[] albums = await dbContext.Albums
                // where album is not a placeholder for non-album tracks
                .Where(a => a.Title != null)
                // include track aggregations (excludes albums without tracks)
                .Join(albumIdsWithTracksCountQuery, a => a.AlbumId, e => e.AlbumId, (a, e) => new
                {
                    Album = a,
                    e.TracksCount,
                    e.Duration,
                })
                // include indication if album is starred by user
                .GroupJoin(albumStarsQuery, e => e.Album.AlbumId, s => s.AlbumId, (e, ss) => new
                {
                    e.Album,
                    Stars = ss,
                    e.TracksCount,
                    e.Duration,
                })
                .SelectMany(e => e.Stars.DefaultIfEmpty(), (e, s) => new
                {
                    e.Album,
                    Starred = s.Added as DateTime?,
                    e.TracksCount,
                    e.Duration,
                })
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

            return new Subsonic.AlbumList2()
            {
                album = albums,
            };
        }

        internal static async Task<Subsonic.AlbumList2> GetAlbumList2OrderedByAlbumTitleAsync(MediaInfoContext dbContext, int apiUserId, int? musicFolderId, int offset, int count, CancellationToken cancellationToken)
        {
            var comparer = CultureInfo.CurrentCulture.CompareInfo.GetStringComparer(CompareOptions.IgnoreCase);

            IQueryable<Track> tracksQuery = GetTracksQuery(dbContext, apiUserId, musicFolderId);

            int[] albumIds = await dbContext.Albums
                // where album is not a placeholder for non-album tracks
                .Where(a => a.Title != null)
                // where album has tracks in library accessible by user
                .Where(a => tracksQuery.Any(t => t.AlbumId == a.AlbumId))
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

            IQueryable<AlbumIdWithTracksCount> albumIdsWithTracksCountQuery = GetAlbumIdsWithTracksCountQuery(dbContext, tracksQuery);

            IQueryable<AlbumStar> albumStarsQuery = GetAlbumStarsQuery(dbContext, apiUserId);

            Subsonic.AlbumID3[] albums = await dbContext.Albums
                // where album is in results
                .Where(a => albumIds.Contains(a.AlbumId))
                // include track aggregations
                .Join(albumIdsWithTracksCountQuery, a => a.AlbumId, e => e.AlbumId, (a, e) => new
                {
                    Album = a,
                    e.TracksCount,
                    e.Duration,
                })
                // include indication if album is starred by user
                .GroupJoin(albumStarsQuery, e => e.Album.AlbumId, s => s.AlbumId, (e, ss) => new
                {
                    e.Album,
                    Stars = ss,
                    e.TracksCount,
                    e.Duration,
                })
                .SelectMany(e => e.Stars.DefaultIfEmpty(), (e, s) => new
                {
                    e.Album,
                    Starred = s.Added as DateTime?,
                    e.TracksCount,
                    e.Duration,
                })
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
                // ensure stable ordering for pagination
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

            IQueryable<Track> tracksQuery = GetTracksQuery(dbContext, apiUserId, musicFolderId);

            int[] albumIds = await dbContext.Albums
                // where album has an artist
                .Where(a => a.Artist != null)
                // where album is not a placeholder for non-album tracks
                .Where(a => a.Title != null)
                // where album has tracks in library accessible by user
                .Where(a => tracksQuery.Any(t => t.AlbumId == a.AlbumId))
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

            IQueryable<AlbumIdWithTracksCount> albumIdsWithTracksCountQuery = GetAlbumIdsWithTracksCountQuery(dbContext, tracksQuery);

            IQueryable<AlbumStar> albumStarsQuery = GetAlbumStarsQuery(dbContext, apiUserId);

            Subsonic.AlbumID3[] albums = await dbContext.Albums
                // where album is in results
                .Where(a => albumIds.Contains(a.AlbumId))
                // include track aggregations
                .Join(albumIdsWithTracksCountQuery, a => a.AlbumId, e => e.AlbumId, (a, e) => new
                {
                    Album = a,
                    e.TracksCount,
                    e.Duration,
                })
                // include indication if album is starred by user
                .GroupJoin(albumStarsQuery, e => e.Album.AlbumId, s => s.AlbumId, (e, ss) => new
                {
                    e.Album,
                    Stars = ss,
                    e.TracksCount,
                    e.Duration,
                })
                .SelectMany(e => e.Stars.DefaultIfEmpty(), (e, s) => new
                {
                    e.Album,
                    Starred = s.Added as DateTime?,
                    e.TracksCount,
                    e.Duration,
                })
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
                // order by album date
                .ThenBy(e => e.AlbumDate)
                // order by album title using culture-aware comparison
                .ThenBy(e => e.AlbumSortTitle, comparer)
                // ensure stable ordering for pagination
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

            IQueryable<Track> tracksQuery = GetTracksQuery(dbContext, apiUserId, musicFolderId);

            IQueryable<AlbumStar> albumStarsQuery = GetAlbumStarsQuery(dbContext, apiUserId);

            int[] albumIds = await dbContext.Albums
                // where album has tracks in library accessible by user
                .Where(a => tracksQuery.Any(t => t.AlbumId == a.AlbumId))
                // exclude albums not starred by user
                .Join(albumStarsQuery, a => a.AlbumId, s => s.AlbumId, (a, s) => new
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

            IQueryable<AlbumIdWithTracksCount> albumIdsWithTracksCountQuery = GetAlbumIdsWithTracksCountQuery(dbContext, tracksQuery);

            Subsonic.AlbumID3[] albums = await dbContext.Albums
                // where album is in results
                .Where(a => albumIds.Contains(a.AlbumId))
                // include track aggregations (excludes albums without tracks)
                .Join(albumIdsWithTracksCountQuery, a => a.AlbumId, e => e.AlbumId, (a, e) => new
                {
                    Album = a,
                    e.TracksCount,
                    e.Duration,
                })
                // include indication if album is starred by user (excludes albums not starred by
                // user)
                .Join(albumStarsQuery, e => e.Album.AlbumId, s => s.AlbumId, (e, s) => new
                {
                    e.Album,
                    Starred = s.Added,
                    e.TracksCount,
                    e.Duration,
                })
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
                // ensure stable ordering for pagination
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

            IQueryable<Track> tracksQuery = GetTracksQuery(dbContext, apiUserId, musicFolderId);

            var albumIdsEnumerable = dbContext.Albums
                // where album is not a placeholder for non-album tracks
                .Where(a => a.Title != null)
                // where album year is in requested year range
                .Where(a => fromYear <= toYear
                    ? a.Date >= fromYear * 10000 && a.Date < (toYear + 1) * 10000
                    : a.Date >= toYear * 10000 && a.Date < (fromYear + 1) * 10000)
                // where album has tracks in library accessible by user
                .Where(a => tracksQuery.Any(t => t.AlbumId == a.AlbumId))
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
                    // order by album title using culture-aware comparison
                    .ThenBy(e => e.AlbumSortTitle, comparer)
                    // ensure stable ordering for pagination
                    .ThenBy(e => e.AlbumId);
            }
            else
            {
                albumIdsEnumerable = albumIdsEnumerable
                    // order by album date
                    .OrderByDescending(e => e.AlbumDate)
                    // order by album title using culture-aware comparison
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

            IQueryable<AlbumIdWithTracksCount> albumIdsWithTracksCountQuery = GetAlbumIdsWithTracksCountQuery(dbContext, tracksQuery);

            IQueryable<AlbumStar> albumStarsQuery = GetAlbumStarsQuery(dbContext, apiUserId);

            var albumsEnumerable = dbContext.Albums
                // where album is in results
                .Where(a => albumIds.Contains(a.AlbumId))
                // include track aggregations
                .Join(albumIdsWithTracksCountQuery, a => a.AlbumId, e => e.AlbumId, (a, e) => new
                {
                    Album = a,
                    e.TracksCount,
                    e.Duration,
                })
                // include indication if album is starred by user
                .GroupJoin(albumStarsQuery, e => e.Album.AlbumId, s => s.AlbumId, (e, ss) => new
                {
                    e.Album,
                    Stars = ss,
                    e.TracksCount,
                    e.Duration,
                })
                .SelectMany(e => e.Stars.DefaultIfEmpty(), (e, s) => new
                {
                    e.Album,
                    Starred = s.Added as DateTime?,
                    e.TracksCount,
                    e.Duration,
                })
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
                    // order by album title using culture-aware comparison
                    .ThenBy(e => e.AlbumSortTitle, comparer)
                    // ensure stable ordering for pagination
                    .ThenBy(a => a.AlbumId);
            }
            else
            {
                albumsEnumerable = albumsEnumerable
                    // order by album date
                    .OrderByDescending(e => e.AlbumDate)
                    // order by album title using culture-aware comparison
                    .ThenByDescending(e => e.AlbumSortTitle, comparer)
                    // ensure stable ordering for pagination
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

            IQueryable<Track> tracksQuery = GetTracksQuery(dbContext, apiUserId, musicFolderId);

            int? genreId = await dbContext.Genres
                .Where(g => g.Name == genre)
                .Select(g => g.GenreId as int?)
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            int[] albumIds = await dbContext.Albums
                // where album is not a placeholder for non-album tracks
                .Where(a => a.Title != null)
                // where album has tracks with requested genre in library accessible by user
                .Where(a => tracksQuery
                    .Where(t => t.AlbumId == a.AlbumId)
                    .Join(dbContext.TrackGenres, t => t.TrackId, tg => tg.TrackId, (t, tg) => tg)
                    .Any(tg => tg.GenreId == genreId))
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

            IQueryable<AlbumIdWithTracksCount> albumIdsWithTracksCountQuery = GetAlbumIdsWithTracksCountQuery(dbContext, tracksQuery);

            IQueryable<AlbumStar> albumStarsQuery = GetAlbumStarsQuery(dbContext, apiUserId);

            Subsonic.AlbumID3[] albums = await dbContext.Albums
                // where album is in results
                .Where(a => albumIds.Contains(a.AlbumId))
                // include track aggregations
                .Join(albumIdsWithTracksCountQuery, a => a.AlbumId, e => e.AlbumId, (a, e) => new
                {
                    Album = a,
                    e.TracksCount,
                    e.Duration,
                })
                // include indication if album is starred by user
                .GroupJoin(albumStarsQuery, e => e.Album.AlbumId, s => s.AlbumId, (e, ss) => new
                {
                    e.Album,
                    Stars = ss,
                    e.TracksCount,
                    e.Duration,
                })
                .SelectMany(e => e.Stars.DefaultIfEmpty(), (e, s) => new
                {
                    e.Album,
                    Starred = s.Added as DateTime?,
                    e.TracksCount,
                    e.Duration,
                })
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
                // ensure stable ordering for pagination
                .ThenBy(a => a.AlbumId)
                .Select(e => e.Item)
                .ToArray(cancellationToken).ConfigureAwait(false);

            return new Subsonic.AlbumList2()
            {
                album = albums,
            };
        }

        internal static async Task<Subsonic.Songs> GetRandomSongsAsync(MediaInfoContext dbContext, int apiUserId, int? musicFolderId, string genre, int? fromYear, int? toYear, int count, CancellationToken cancellationToken)
        {
            int? genreId = genre == null
                ? null
                : await dbContext.Genres
                    .Where(g => g.Name == genre)
                    .Select(g => g.GenreId as int?)
                    .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            IQueryable<Track> tracksQuery = GetTracksQuery(dbContext, apiUserId, musicFolderId);

            IQueryable<TrackStar> trackStarsQuery = GetTrackStarsQuery(dbContext, apiUserId);

            Subsonic.Child[] tracks = await tracksQuery
                // where tracks have requested genre (if specified)
                .Where(t => genreId == null || t.TrackGenres.Any(tg => tg.GenreId == genreId))
                // where album year is in requested year range
                .Where(t => !fromYear.HasValue || t.Date >= fromYear.Value * 10000)
                .Where(t => !toYear.HasValue || t.Date < (toYear.Value + 1) * 10000)
                // order randomly
                .OrderBy(t => MediaInfoContext.Random())
                // limit number of results
                .Take(count)
                // include indication if track is starred by user
                .GroupJoin(trackStarsQuery, t => t.TrackId, s => s.TrackId, (t, ss) => new
                {
                    Track = t,
                    Stars = ss,
                })
                .SelectMany(e => e.Stars.DefaultIfEmpty(), (e, s) => new
                {
                    e.Track,
                    Starred = s.Added as DateTime?,
                })
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
                    e.Starred))
                .ToArrayAsync(cancellationToken).ConfigureAwait(false);

            return new Subsonic.Songs()
            {
                song = tracks,
            };
        }

        internal static async Task<Subsonic.Songs> GetSongsByGenreAsync(MediaInfoContext dbContext, int apiUserId, int? musicFolderId, string genre, int offset, int count, CancellationToken cancellationToken)
        {
            var comparer = CultureInfo.CurrentCulture.CompareInfo.GetStringComparer(CompareOptions.IgnoreCase);

            int? genreId = await dbContext.Genres
                .Where(g => g.Name == genre)
                .Select(g => g.GenreId as int?)
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            IQueryable<Track> tracksQuery = GetTracksQuery(dbContext, apiUserId, musicFolderId);

            int[] trackIds = await dbContext.TrackGenres
                // where track has requested genre
                .Where(tg => tg.GenreId == genreId)
                .Join(tracksQuery, tg => tg.TrackId, t => t.TrackId, (tg, t) => t)
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

            IQueryable<TrackStar> trackStarsQuery = GetTrackStarsQuery(dbContext, apiUserId);

            Subsonic.Child[] tracks = await dbContext.Tracks
                // where track has requested genre
                .Where(t => trackIds.Contains(t.TrackId))
                // include indication if track is starred by user
                .GroupJoin(trackStarsQuery, t => t.TrackId, s => s.TrackId, (t, ss) => new
                {
                    Track = t,
                    Stars = ss,
                })
                .SelectMany(e => e.Stars.DefaultIfEmpty(), (e, s) => new
                {
                    e.Track,
                    Starred = s.Added as DateTime?,
                })
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
                        e.Starred)
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

        internal static async Task<Subsonic.Starred2> GetStarred2Async(MediaInfoContext dbContext, int apiUserId, int? musicFolderId, CancellationToken cancellationToken)
        {
            var comparer = CultureInfo.CurrentCulture.CompareInfo.GetStringComparer(CompareOptions.IgnoreCase);

            IQueryable<Track> tracksQuery = GetTracksQuery(dbContext, apiUserId, musicFolderId);

            IQueryable<ArtistIdWithAlbumsCount> artistIdsWithAlbumsCountQuery = GetArtistIdsWithAlbumsCountQuery(dbContext, tracksQuery);

            IQueryable<ArtistStar> artistStarsQuery = GetArtistStarsQuery(dbContext, apiUserId);

            Subsonic.ArtistID3[] artists = await dbContext.Artists
                // include indication if artist is starred by user (excludes albums not starred by
                // user)
                .Join(artistStarsQuery, a => a.ArtistId, s => s.ArtistId, (a, s) => new
                {
                    Artist = a,
                    Starred = s.Added,
                })
                // include album aggregation (excludes artists without albums with tracks)
                .Join(artistIdsWithAlbumsCountQuery, e => e.Artist.ArtistId, e => e.ArtistId, (e1, e2) => new
                {
                    e1.Artist,
                    e1.Starred,
                    e2.AlbumsCount,
                })
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

            IQueryable<AlbumStar> albumStarsQuery = GetAlbumStarsQuery(dbContext, apiUserId);

            IQueryable<AlbumIdWithTracksCount> albumIdsWithTracksCountQuery = GetAlbumIdsWithTracksCountQuery(dbContext, tracksQuery);

            Subsonic.AlbumID3[] albums = await dbContext.Albums
                // include indication if album is starred by user (excludes albums not starred by
                // user)
                .Join(albumStarsQuery, a => a.AlbumId, s => s.AlbumId, (a, s) => new
                {
                    Album = a,
                    Starred = s.Added,
                })
                // include track aggregations (excludes albums without tracks)
                .Join(albumIdsWithTracksCountQuery, e => e.Album.AlbumId, e => e.AlbumId, (e1, e2) => new
                {
                    e1.Album,
                    e1.Starred,
                    e2.TracksCount,
                    e2.Duration,
                })
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

            IQueryable<TrackStar> trackStarsQuery = GetTrackStarsQuery(dbContext, apiUserId);

            Subsonic.Child[] tracks = await dbContext.Tracks
                // include indication if track is starred by user (excludes tracks not starred by
                // user)
                .Join(trackStarsQuery, t => t.TrackId, s => s.TrackId, (t, s) => new
                {
                    Track = t,
                    Starred = s.Added,
                })
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
                        e.Starred)
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

        internal static async Task<Subsonic.SearchResult3> GetSearch3ResultsAsync(MediaInfoContext dbContext, int apiUserId, int? musicFolderId, string query, int artistOffset, int artistCount, int albumOffset, int albumCount, int songOffset, int songCount, CancellationToken cancellationToken)
        {
            var positiveTerms = new List<string>();
            var negativeTerms = new List<string>();

            foreach (var term in query.Split(" ", 100, StringSplitOptions.RemoveEmptyEntries))
                if (term.StartsWith('-'))
                    negativeTerms.Add(" " + term.Substring(1) + " ");
                else
                    positiveTerms.Add(" " + term + " ");

            Subsonic.ArtistID3[] artists;
            Subsonic.AlbumID3[] albums;
            Subsonic.Child[] tracks;

            IQueryable<Track> tracksQuery = GetTracksQuery(dbContext, apiUserId, musicFolderId);

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

                IQueryable<ArtistIdWithAlbumsCount> artistIdsWithAlbumsCountQuery = GetArtistIdsWithAlbumsCountQuery(dbContext, tracksQuery);

                IQueryable<ArtistStar> artistStarsQuery = GetArtistStarsQuery(dbContext, apiUserId);

                artists = await dbContext.Artists
                    // where artist is search hit
                    .Where(a => artistIds.Contains(a.ArtistId))
                    // include album aggregation (excludes artists without albums with tracks)
                    .Join(artistIdsWithAlbumsCountQuery, a => a.ArtistId, e => e.ArtistId, (a, e) => new
                    {
                        Artist = a,
                        e.AlbumsCount,
                    })
                    // include indication if album is starred by user
                    .GroupJoin(artistStarsQuery, e => e.Artist.ArtistId, s => s.ArtistId, (e, ss) => new
                    {
                        e.Artist,
                        Stars = ss,
                        e.AlbumsCount,
                    })
                    .SelectMany(e => e.Stars.DefaultIfEmpty(), (e, s) => new
                    {
                        e.Artist,
                        Starred = s.Added as DateTime?,
                        e.AlbumsCount,
                    })
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
                    .Join(dbContext.Albums, e => e.AlbumId, a => a.AlbumId, (e, a) => new
                    {
                        a.AlbumId,
                        ArtistName = a.Artist.Name,
                        AlbumTitle = a.Title,
                    })
                    // where album is not a placeholder for non-album tracks
                    .Where(a => a.AlbumTitle != null)
                    .OrderBy(t => t.AlbumId)
                    .AsAsyncEnumerable()
                    // where album matches search query
                    .Where(e => Match(e.ArtistName, e.AlbumTitle))
                    .Select(e => e.AlbumId)
                    // paginate
                    .Skip(albumOffset)
                    .Take(albumCount)
                    .ToArray(cancellationToken).ConfigureAwait(false);

                IQueryable<AlbumIdWithTracksCount> albumIdsWithTracksCountQuery = GetAlbumIdsWithTracksCountQuery(dbContext, tracksQuery);

                IQueryable<AlbumStar> albumStarsQuery = GetAlbumStarsQuery(dbContext, apiUserId);

                albums = await dbContext.Albums
                    // where album is a search hit
                    .Where(a => albumIds.Contains(a.AlbumId))
                    // include track aggregations (excludes albums without tracks)
                    .Join(albumIdsWithTracksCountQuery, a => a.AlbumId, e => e.AlbumId, (a, e) => new
                    {
                        Album = a,
                        e.TracksCount,
                        e.Duration,
                    })
                    // include indication if album is starred by user
                    .GroupJoin(albumStarsQuery, e => e.Album.AlbumId, s => s.AlbumId, (e, ss) => new
                    {
                        e.Album,
                        Stars = ss,
                        e.TracksCount,
                        e.Duration,
                    })
                    .SelectMany(e => e.Stars.DefaultIfEmpty(), (e, s) => new
                    {
                        e.Album,
                        Starred = s.Added as DateTime?,
                        e.TracksCount,
                        e.Duration,
                    })
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

                IQueryable<TrackStar> trackStarsQuery = GetTrackStarsQuery(dbContext, apiUserId);

                tracks = await dbContext.Tracks
                    // where track is a search hit
                    .Where(t => trackIds.Contains(t.TrackId))
                    // include indication if track is starred by user
                    .GroupJoin(trackStarsQuery, t => t.TrackId, s => s.TrackId, (t, ss) => new
                    {
                        Track = t,
                        Stars = ss,
                    })
                    .SelectMany(e => e.Stars.DefaultIfEmpty(), (e, s) => new
                    {
                        e.Track,
                        Starred = s.Added as DateTime?,
                    })
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
                        e.Starred))
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
                foreach (var term in positiveTerms)
                {
                    if (s1 != null && (" " + s1 + " ").Contains(term, StringComparison.CurrentCultureIgnoreCase))
                        continue;
                    if (s2 != null && (" " + s2 + " ").Contains(term, StringComparison.CurrentCultureIgnoreCase))
                        continue;
                    if (s3 != null && (" " + s3 + " ").Contains(term, StringComparison.CurrentCultureIgnoreCase))
                        continue;

                    return false;
                }

                foreach (var term in negativeTerms)
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
            IQueryable<Track> tracksQuery = GetTracksQuery(dbContext, apiUserId);

            IQueryable<PlaylistIdWithTracksCount> playlistIdsWithTracksCountQuery = GetPlaylistIdsWithTracksCountQuery(dbContext, tracksQuery);

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

        internal static async Task<Subsonic.PlaylistWithSongs> GetPlaylistAsync(MediaInfoContext dbContext, int apiUserId, int playlistId, CancellationToken cancellationToken)
        {
            IQueryable<Track> tracksQuery = GetTracksQuery(dbContext, apiUserId);

            IQueryable<AlbumStar> albumStarsQuery = GetAlbumStarsQuery(dbContext, apiUserId);

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

            IQueryable<TrackWithPlaylistIndex> tracksByPlaylistIdQuery = GetTracksByPlaylistIdQuery(dbContext, apiUserId, playlistId);

            IQueryable<TrackStar> trackStarsQuery = GetTrackStarsQuery(dbContext, apiUserId);

            Subsonic.Child[] tracks = await tracksByPlaylistIdQuery
                // include indication if track is starred by user
                .GroupJoin(trackStarsQuery, e => e.Track.TrackId, s => s.TrackId, (e, ss) => new
                {
                    e.Track,
                    Stars = ss,
                    e.PlaylistIndex,
                })
                .SelectMany(e => e.Stars.DefaultIfEmpty(), (e, s) => new
                {
                    e.Track,
                    Starred = s.Added as DateTime?,
                    e.PlaylistIndex,
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
                    e.Starred))
                .ToArrayAsync(cancellationToken).ConfigureAwait(false);

            return playlist.SetSongs(tracks);
        }

        internal static async Task<Playlist> CreatePlaylistAsync(MediaInfoContext dbContext, int apiUserId, string name, CancellationToken cancellationToken)
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

            return playlist;
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
                .ForEachAsync(pt => dbContext.Remove(pt)).ConfigureAwait(false);

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
                await dbContext.PlaylistTracks.AddAsync(playlistTrack).ConfigureAwait(false);
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
                .Select(pt => pt.Index)
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

            var tracksQuery = GetTracksQuery(dbContext, apiUserId);

            foreach (var index in songIndexes)
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

        #endregion

        #region Media retrieval

        internal static async Task<TrackStreamInfo> GetTrackStreamInfoAsync(MediaInfoContext dbContext, int apiUserId, int trackId, CancellationToken cancellationToken)
        {
            IQueryable<Track> trackByIdQuery = GetTrackByIdQuery(dbContext, apiUserId, trackId);

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
            IQueryable<Picture> picturesByStreamHashQuery = GetPicturesByStreamHashQuery(dbContext, apiUserId, hash);

            CoverArtStreamInfo coverArtStreamInfo = await picturesByStreamHashQuery
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

        internal static async Task<bool> CanAddTracksAsync(MediaInfoContext dbContext, int apiUserId, IReadOnlyList<int> trackIds, CancellationToken cancellationToken)
        {
            foreach (var trackId in trackIds)
            {
                IQueryable<Track> trackByIdQuery = GetTrackByIdQuery(dbContext, apiUserId, trackId);

                bool trackExists = await trackByIdQuery
                    .AnyAsync(cancellationToken).ConfigureAwait(false);
                if (!trackExists)
                    return false;
            }

            return true;
        }

        internal static async Task<Subsonic.Child[]> GetTracksAsync(MediaInfoContext dbContext, int apiUserId, IReadOnlyList<int> trackIds, CancellationToken cancellationToken)
        {
            IQueryable<TrackStar> trackStarsQuery = GetTrackStarsQuery(dbContext, apiUserId);

            Subsonic.Child[] tracks = await dbContext.Tracks
                // where track is in playlist
                .Where(t => trackIds.Contains(t.TrackId))
                // find files for tracks
                .Join(dbContext.Files, t => t.FileId, f => f.FileId, (t, f) => new
                {
                    File = f,
                    Track = t,
                })
                // include indication if track is starred by user
                .GroupJoin(trackStarsQuery, e => e.Track.TrackId, s => s.TrackId, (e, ss) => new
                {
                    e.File,
                    e.Track,
                    Stars = ss,
                })
                .SelectMany(e => e.Stars.DefaultIfEmpty(), (e, s) => new
                {
                    e.File,
                    e.Track,
                    Starred = s.Added as DateTime?,
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
                    e.Starred))
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
                // where user is requested user
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
                scrobblingEnabled = false,
                maxBitRate = user.MaxBitRate,
                maxBitRateSpecified = true,
                adminRole = user.IsAdmin,
                settingsRole = !user.IsGuest,
                downloadRole = false,
                uploadRole = false,
                playlistRole = false,
                coverArtRole = false,
                commentRole = false,
                podcastRole = false,
                streamRole = true,
                jukeboxRole = user.CanJukebox,
                shareRole = false,
                videoConversionRole = false,
                folder = libraryIds,
            };
        }

        internal static async Task SetUserPasswordAsync(MediaInfoContext dbContext, string username, string password, CancellationToken cancellationToken)
        {
            User user = await dbContext.Users
                // where user is requested user
                .Where(u => u.Name == username)
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (user == null)
                throw RestApiErrorException.DataNotFoundError();

            user.Password = password;
        }

        #endregion

        private static IQueryable<Directory> GetDirectoriesQuery(MediaInfoContext dbContext, int apiUserId, int? musicFolderId)
        {
            return dbContext.Directories
                // where directory is in requested library (if specified)
                .Where(d => !musicFolderId.HasValue || d.LibraryId == musicFolderId.Value)
                // where library containing directory is accessible by user
                .Where(d => !d.Library.IsAccessControlled || d.Library.LibraryUsers.Any(lu => lu.UserId == apiUserId));
        }

        private static IQueryable<Directory> GetDirectoryByIdQuery(MediaInfoContext dbContext, int apiUserId, int directoryId)
        {
            return dbContext.Directories
                // where directory is requested directory
                .Where(d => d.DirectoryId == directoryId)
                // where library containing directory is accessible by user
                .Where(d => !d.Library.IsAccessControlled || d.Library.LibraryUsers.Any(lu => lu.UserId == apiUserId));
        }

        private static IQueryable<File> GetFilesQuery(MediaInfoContext dbContext, int apiUserId, int? musicFolderId)
        {
            return dbContext.Files
                // where file is in requested library (if specified)
                .Where(f => !musicFolderId.HasValue || f.LibraryId == musicFolderId.Value)
                // where library containing file is accessible by user
                .Where(f => !f.Library.IsAccessControlled || f.Library.LibraryUsers.Any(lu => lu.UserId == apiUserId));
        }

        private static IQueryable<Track> GetTracksQuery(MediaInfoContext dbContext, int apiUserId)
        {
            return dbContext.Tracks
                // where library containing track is accessible by user
                .Where(t => !t.Library.IsAccessControlled || t.Library.LibraryUsers.Any(lu => lu.UserId == apiUserId));
        }

        private static IQueryable<Track> GetTracksQuery(MediaInfoContext dbContext, int apiUserId, int? musicFolderId)
        {
            return dbContext.Tracks
                // where tracks are in requested library (if specified)
                .Where(t => !musicFolderId.HasValue || t.LibraryId == musicFolderId.Value)
                // where library containing track is accessible by user
                .Where(t => !t.Library.IsAccessControlled || t.Library.LibraryUsers.Any(lu => lu.UserId == apiUserId));
        }

        private static IQueryable<Track> GetTrackByIdQuery(MediaInfoContext dbContext, int apiUserId, int trackId)
        {
            return dbContext.Tracks
                // where track is requested track
                .Where(t => t.TrackId == trackId)
                // where library containing track is accessible by user
                .Where(t => !t.Library.IsAccessControlled || t.Library.LibraryUsers.Any(lu => lu.UserId == apiUserId));
        }

        private static IQueryable<Track> GetTracksByAlbumIdQuery(MediaInfoContext dbContext, int apiUserId, int albumId)
        {
            return dbContext.Tracks
                // where track is on requested album
                .Where(t => t.AlbumId == albumId)
                // where library containing track is accessible by user
                .Where(t => !t.Library.IsAccessControlled || t.Library.LibraryUsers.Any(lu => lu.UserId == apiUserId));
        }

        private static IQueryable<TrackWithPlaylistIndex> GetTracksByPlaylistIdQuery(MediaInfoContext dbContext, int apiUserId, int playlistId)
        {
            IQueryable<Track> tracks = GetTracksQuery(dbContext, apiUserId);

            return dbContext.PlaylistTracks
                // where track is on requested playlist
                .Where(pt => pt.PlaylistId == playlistId)
                .Join(tracks, pt => pt.TrackId, t => t.TrackId, (pt, t) => new TrackWithPlaylistIndex
                {
                    Track = t,
                    PlaylistIndex = pt.Index,
                });
        }

        private static IQueryable<Picture> GetPicturesByStreamHashQuery(MediaInfoContext dbContext, int apiUserId, long hash)
        {
            return dbContext.Pictures
                // where picture is requested picture
                .Where(p => p.StreamHash == hash)
                // where library containing picture file is accessible by user
                .Where(p => !p.File.Library.IsAccessControlled || p.File.Library.LibraryUsers.Any(lu => lu.UserId == apiUserId));
        }

        private static IQueryable<ArtistStar> GetArtistStarsQuery(MediaInfoContext dbContext, int apiUserId)
        {
            return dbContext.ArtistStars
                .Where(s => s.UserId == apiUserId);
        }

        private static IQueryable<AlbumStar> GetAlbumStarsQuery(MediaInfoContext dbContext, int apiUserId)
        {
            return dbContext.AlbumStars
                .Where(s => s.UserId == apiUserId);
        }

        private static IQueryable<TrackStar> GetTrackStarsQuery(MediaInfoContext dbContext, int apiUserId)
        {
            return dbContext.TrackStars
                .Where(s => s.UserId == apiUserId);
        }

        private static IQueryable<ArtistIdWithAlbumsCount> GetArtistIdsWithAlbumsCountQuery(MediaInfoContext dbContext, IQueryable<Track> tracksQuery)
        {
            return dbContext.Albums
                // find album artist and album of tracks (excludes artists without albums with
                // tracks)
                .Join(tracksQuery, a => a.AlbumId, t => t.AlbumId, (a, t) => new
                {
                    a.ArtistId,
                    t.AlbumId,
                })
                .Distinct()
                .GroupBy(e => e.ArtistId)
                // count albums
                .Select(grouping => new ArtistIdWithAlbumsCount
                {
                    ArtistId = grouping.Key,
                    AlbumsCount = grouping.Count(),
                });
        }

        private static IQueryable<AlbumIdWithTracksCount> GetAlbumIdsWithTracksCountQuery(MediaInfoContext dbContext, IQueryable<Track> tracksQuery)
        {
            return dbContext.Albums
                // find album tracks (excludes albums without tracks)
                .Join(tracksQuery, a => a.AlbumId, t => t.AlbumId, (a, t) => new
                {
                    a.AlbumId,
                    TrackDuration = t.Duration ?? 0,
                })
                .GroupBy(e => e.AlbumId)
                // count album tracks and compute album duration
                .Select(grouping => new AlbumIdWithTracksCount
                {
                    AlbumId = grouping.Key,
                    TracksCount = grouping.Count(),
                    Duration = grouping.Sum(e => e.TrackDuration),
                });
        }

        private static IQueryable<PlaylistIdWithTracksCount> GetPlaylistIdsWithTracksCountQuery(MediaInfoContext dbContext, IQueryable<Track> tracksQuery)
        {
            return dbContext.Playlists
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
                .Select(grouping => new PlaylistIdWithTracksCount
                {
                    PlaylistId = grouping.Key,
                    TracksCount = grouping.Sum(e => e.TrackOne),
                    Duration = grouping.Sum(e => e.TrackDuration),
                });
        }

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

        private sealed class TrackWithPlaylistIndex
        {
            public Track Track;
            public int PlaylistIndex;
        }

        private sealed class ArtistIdWithAlbumsCount
        {
            public int? ArtistId;
            public int AlbumsCount;
        }

        private sealed class AlbumIdWithTracksCount
        {
            public int AlbumId;
            public int TracksCount;
            public float Duration;
        }

        private sealed class PlaylistIdWithTracksCount
        {
            public int PlaylistId;
            public int TracksCount;
            public float Duration;
        }
    }
}
