//
// Copyright (C) 2019  Carl Reinke
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
using System.Linq;

namespace Hypersonic.Data
{
    internal static class Queries
    {
        /// <summary>
        /// Filters a query for libraries to those accessible by a specified user.
        /// </summary>
        internal static IQueryable<Library> WhereIsAccessibleBy(this IQueryable<Library> librariesQuery, int userId)
        {
            return librariesQuery
                // where library is accessible by user
                .Where(l => !l.IsAccessControlled || l.LibraryUsers.Any(lu => lu.UserId == userId));
        }

        ////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Filters a query for tracks to those accessible by a specified user.
        /// </summary>
        internal static IQueryable<Track> WhereIsAccessibleBy(this IQueryable<Track> tracksQuery, int userId)
        {
            return tracksQuery
                // where library containing track is accessible by user
                .Where(t => !t.Library.IsAccessControlled || t.Library.LibraryUsers.Any(lu => lu.UserId == userId));
        }

        /// <summary>
        /// Filters a query for tracks to those in a specified library and accessible by a specified
        /// user.
        /// </summary>
        internal static IQueryable<Track> WhereIsAccessibleBy(this IQueryable<Track> tracksQuery, int userId, int? libraryId)
        {
            return tracksQuery
                // where tracks are in requested library (if specified)
                .Where(t => !libraryId.HasValue || t.LibraryId == libraryId.Value)
                // where library containing track is accessible by user
                .Where(t => !t.Library.IsAccessControlled || t.Library.LibraryUsers.Any(lu => lu.UserId == userId));
        }

        /// <summary>
        /// Filters a query for tracks to those that have a specified genre.
        /// </summary>
        internal static IQueryable<Track> WhereHasGenre(this IQueryable<Track> tracksQuery, MediaInfoContext dbContext, int genreId)
        {
            return dbContext.TrackGenres
                // where track has requested genre
                .Where(tg => tg.GenreId == genreId)
                .Join(tracksQuery, tg => tg.TrackId, t => t.TrackId, (tg, t) => t);
        }

        /// <summary>
        /// Filters a query for tracks to those starred by a specified user.
        /// </summary>
        internal static IQueryable<TrackWithStarred> WhereIsStarredBy(this IQueryable<Track> tracksQuery, MediaInfoContext dbContext, int userId)
        {
            IQueryable<TrackStar> trackStarsQuery = GetTrackStarsQuery(dbContext, userId);

            return tracksQuery
                // include indication if track is starred by user (excludes tracks not starred by
                // user)
                .Join(trackStarsQuery, t => t.TrackId, s => s.TrackId, (t, s) => new TrackWithStarred
                {
                    Track = t,
                    Starred = s.Added,
                });
        }

        /// <summary>
        /// Supplements a query for tracks with stars for a specified user.
        /// </summary>
        internal static IQueryable<TrackWithStarred> WithStarredBy(this IQueryable<Track> tracksQuery, MediaInfoContext dbContext, int userId)
        {
            IQueryable<TrackStar> trackStarsQuery = GetTrackStarsQuery(dbContext, userId);

            return tracksQuery
                // include indication if track is starred by user
                .GroupJoin(trackStarsQuery, t => t.TrackId, s => s.TrackId, (t, ss) => new
                {
                    Track = t,
                    Stars = ss,
                })
                .SelectMany(e => e.Stars.DefaultIfEmpty(), (e, s) => new TrackWithStarred
                {
                    Track = e.Track,
                    Starred = s.Added as DateTime?,
                });
        }

        ////////////////////////////////////////////////////////////////////////////////////////////

        internal static IQueryable<Picture> WhereIsAccessibleBy(this IQueryable<Picture> picturesQuery, int userId)
        {
            return picturesQuery
                // where library containing picture file is accessible by user
                .Where(p => !p.File.Library.IsAccessControlled || p.File.Library.LibraryUsers.Any(lu => lu.UserId == userId));
        }

        ////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Filters a query for albums to those that are not placeholders used for non-album tracks.
        /// </summary>
        internal static IQueryable<Album> WhereIsNotPlaceholder(this IQueryable<Album> albumsQuery)
        {
            return albumsQuery
                // where album is not a placeholder for non-album tracks
                .Where(a => a.Title != null);
        }

        /// <summary>
        /// Filters a query for albums to those that have tracks in a specified library and
        /// accessible by a specified user.
        /// </summary>
        internal static IQueryable<Album> WhereIsAccessibleBy(this IQueryable<Album> albumsQuery, MediaInfoContext dbContext, int userId, int? libraryId)
        {
            IQueryable<Track> tracksQuery = dbContext.Tracks
                .WhereIsAccessibleBy(userId, libraryId);

            return albumsQuery
                // where album has tracks in library accessible by user
                .Where(a => tracksQuery.Any(t => t.AlbumId == a.AlbumId));
        }

        /// <summary>
        /// Filters a query for albums to those that have tracks that have a specified genre.
        /// </summary>
        internal static IQueryable<Album> WhereHasTrackWithGenre(this IQueryable<Album> albumsQuery, MediaInfoContext dbContext, IQueryable<Track> tracksQuery, int genreId)
        {
            return albumsQuery
                // where album has tracks with requested genre
                .Where(a => tracksQuery
                    .Where(t => t.AlbumId == a.AlbumId)
                    .Join(dbContext.TrackGenres, t => t.TrackId, tg => tg.TrackId, (t, tg) => tg)
                    .Any(tg => tg.GenreId == genreId));
        }

        /// <summary>
        /// Filters a query for albums to those starred by a specified user.
        /// </summary>
        internal static IQueryable<AlbumWithStarred> WhereIsStarredBy(this IQueryable<Album> albumsQuery, MediaInfoContext dbContext, int userId)
        {
            IQueryable<AlbumStar> albumStarsQuery = GetAlbumStarsQuery(dbContext, userId);

            return albumsQuery
                // include indication if album is starred by user (excludes albums not starred by
                // user)
                .Join(albumStarsQuery, a => a.AlbumId, s => s.AlbumId, (a, s) => new AlbumWithStarred
                {
                    Album = a,
                    Starred = s.Added,
                });
        }

        /// <summary>
        /// Supplements a query for albums with stars for a specified user.
        /// </summary>
        internal static IQueryable<AlbumWithStarred> WithStarredBy(this IQueryable<Album> albumsQuery, MediaInfoContext dbContext, int userId)
        {
            IQueryable<AlbumStar> albumStarsQuery = GetAlbumStarsQuery(dbContext, userId);

            return albumsQuery
                // include indication if album is starred by user
                .GroupJoin(albumStarsQuery, a => a.AlbumId, s => s.AlbumId, (a, ss) => new
                {
                    Album = a,
                    Stars = ss,
                })
                .SelectMany(e => e.Stars.DefaultIfEmpty(), (e, s) => new AlbumWithStarred
                {
                    Album = e.Album,
                    Starred = s.Added as DateTime?,
                });
        }

        /// <summary>
        /// Supplements a query for albums with the number of tracks from a specified query for
        /// tracks and filters out those with no applicable tracks.
        /// </summary>
        internal static IQueryable<AlbumWithStarredAndTracksCount> WithTracksCount(this IQueryable<AlbumWithStarred> albumsQuery, MediaInfoContext dbContext, IQueryable<Track> tracksQuery)
        {
            IQueryable<AlbumIdWithTracksCount> albumIdsWithTracksCountQuery = GetAlbumIdsWithTracksCountQuery(dbContext, tracksQuery);

            return albumsQuery
                // include track aggregations (excludes albums without tracks)
                .Join(albumIdsWithTracksCountQuery, e1 => e1.Album.AlbumId, e2 => e2.AlbumId, (e1, e2) => new AlbumWithStarredAndTracksCount
                {
                    Album = e1.Album,
                    Starred = e1.Starred,
                    TracksCount = e2.TracksCount,
                    Duration = e2.Duration,
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

        ////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Filters a query for artists to those that have albums that have tracks in a specified
        /// library and accessible by a specified user.
        /// </summary>
        internal static IQueryable<Artist> WhereIsAccessibleBy(this IQueryable<Artist> artistsQuery, MediaInfoContext dbContext, int userId, int? libraryId)
        {
            IQueryable<Track> tracksQuery = dbContext.Tracks
                .WhereIsAccessibleBy(userId, libraryId);

            return artistsQuery
                // where artist has tracks in library accessible by user
                .Where(a => tracksQuery.Any(t => t.Album.ArtistId == a.ArtistId));
        }

        internal static IQueryable<ArtistWithStarred> WhereIsStarredBy(this IQueryable<Artist> artistsQuery, MediaInfoContext dbContext, int userId)
        {
            IQueryable<ArtistStar> artistStarsQuery = GetArtistStarsQuery(dbContext, userId);

            return artistsQuery
                // include indication if artist is starred by user (excludes albums not starred by
                // user)
                .Join(artistStarsQuery, a => a.ArtistId, s => s.ArtistId, (a, s) => new ArtistWithStarred
                {
                    Artist = a,
                    Starred = s.Added,
                });
        }

        /// <summary>
        /// Supplements a query for artists with stars for a specified user.
        /// </summary>
        internal static IQueryable<ArtistWithStarred> WithStarredBy(this IQueryable<Artist> artistsQuery, MediaInfoContext dbContext, int userId)
        {
            IQueryable<ArtistStar> artistStarsQuery = GetArtistStarsQuery(dbContext, userId);

            return artistsQuery
                // include indication if album is starred by user
                .GroupJoin(artistStarsQuery, a => a.ArtistId, s => s.ArtistId, (a, ss) => new
                {
                    Artist = a,
                    Stars = ss,
                })
                .SelectMany(e => e.Stars.DefaultIfEmpty(), (e, s) => new ArtistWithStarred
                {
                    Artist = e.Artist,
                    Starred = s.Added as DateTime?,
                });
        }

        /// <summary>
        /// Supplements a query for artists with the number of albums with tracks from a specified
        /// track query and filters out those with no applicable albums.
        /// </summary>
        internal static IQueryable<ArtistWithStarredAndAlbumsCount> WithAlbumsCount(this IQueryable<ArtistWithStarred> artistsQuery, MediaInfoContext dbContext, IQueryable<Track> tracksQuery)
        {
            IQueryable<ArtistIdWithAlbumsCount> albumArtistIdsWithAlbumsCountQuery = GetAlbumArtistIdsWithAlbumsCountQuery(dbContext, tracksQuery);

            return artistsQuery
                // include album aggregation (excludes artists without albums with tracks)
                .Join(albumArtistIdsWithAlbumsCountQuery, e1 => e1.Artist.ArtistId, e2 => e2.ArtistId, (e1, e2) => new ArtistWithStarredAndAlbumsCount
                {
                    Artist = e1.Artist,
                    Starred = e1.Starred,
                    AlbumsCount = e2.AlbumsCount,
                });
        }

        private static IQueryable<ArtistIdWithAlbumsCount> GetAlbumArtistIdsWithAlbumsCountQuery(MediaInfoContext dbContext, IQueryable<Track> tracksQuery)
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

        ////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Supplements a query for genres with the number of albums with tracks from a specified
        /// query for tracks that have the specified genre and the number of tracks from a specified
        /// query for tracks that have specified genre and filters out genres with no applicable
        /// tracks.
        /// </summary>
        internal static IQueryable<GenreWithCounts> WithCounts(this IQueryable<Genre> genresQuery, MediaInfoContext dbContext, IQueryable<Track> tracksQuery)
        {
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

            return genresQuery
                .Join(genresWithAlbumsCountQuery, g => g.GenreId, e => e.GenreId, (g, a) => new
                {
                    Genre = g,
                    a.AlbumsCount,
                })
                .Join(genresWithTracksCountQuery, e => e.Genre.GenreId, e => e.GenreId, (e, t) => new GenreWithCounts
                {
                    Genre = e.Genre,
                    AlbumsCount = e.AlbumsCount,
                    TracksCount = t.TracksCount,
                });
        }

        ////////////////////////////////////////////////////////////////////////////////////////////

        private static IQueryable<TrackStar> GetTrackStarsQuery(MediaInfoContext dbContext, int userId)
        {
            return dbContext.TrackStars
                .Where(s => s.UserId == userId);
        }

        private static IQueryable<AlbumStar> GetAlbumStarsQuery(MediaInfoContext dbContext, int userId)
        {
            return dbContext.AlbumStars
                .Where(s => s.UserId == userId);
        }

        private static IQueryable<ArtistStar> GetArtistStarsQuery(MediaInfoContext dbContext, int userId)
        {
            return dbContext.ArtistStars
                .Where(s => s.UserId == userId);
        }

        ////////////////////////////////////////////////////////////////////////////////////////////

        internal sealed class TrackWithStarred
        {
            public Track Track;
            public DateTime? Starred;
        }

        internal sealed class ArtistWithStarred
        {
            public Artist Artist;
            public DateTime? Starred;
        }

        internal sealed class ArtistIdWithAlbumsCount
        {
            public int? ArtistId;
            public int AlbumsCount;
        }

        internal sealed class ArtistWithStarredAndAlbumsCount
        {
            public Artist Artist;
            public DateTime? Starred;
            public int AlbumsCount;
        }

        internal sealed class AlbumWithStarred
        {
            public Album Album;
            public DateTime? Starred;
        }

        internal sealed class AlbumIdWithTracksCount
        {
            public int AlbumId;
            public int TracksCount;
            public float Duration;
        }

        internal sealed class AlbumWithStarredAndTracksCount
        {
            public Album Album;
            public DateTime? Starred;
            public int TracksCount;
            public float Duration;
        }

        internal sealed class GenreWithCounts
        {
            public Genre Genre;
            public int AlbumsCount;
            public int TracksCount;
        }
    }
}
