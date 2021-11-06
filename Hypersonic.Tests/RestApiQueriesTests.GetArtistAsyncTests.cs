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
using Hypersonic.Data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Xunit;
using static Hypersonic.Tests.Helpers;

namespace Hypersonic.Tests
{
    public static partial class RestApiQueriesTests
    {
        public static class GetArtistAsyncTests
        {
            private static readonly StringComparer _stringComparer = CultureInfo.CurrentCulture.CompareInfo.GetStringComparer(CompareOptions.IgnoreCase);

            [Fact]
            public static void GetArtistAsync_ArtistIdDoesNotExist_ThrowsDataNotFoundError()
            {
                var dbConnection = OpenSqliteDatabase();

                var dbContextOptionsBuilder = new DbContextOptionsBuilder<MediaInfoContext>()
                    .DisableClientSideEvaluation()
                    .UseSqlite(dbConnection);

                using (var dbContext = new MediaInfoContext(dbContextOptionsBuilder.Options))
                {
                    var random = new RandomPopulator(dbContext);
                    var user = random.AddUser();
                    var library = random.AddLibrary();
                    var artist = random.AddArtist();
                    _ = dbContext.SaveChanges();

                    var ex = Assert.Throws<RestApiErrorException>(() => RestApiQueries.GetArtistAsync(dbContext, user.UserId, artist.ArtistId + 1, CancellationToken.None).GetAwaiter().GetResult());

                    var expectedException = RestApiErrorException.DataNotFoundError();
                    Assert.Equal(expectedException.Message, ex.Message);
                    Assert.Equal(expectedException.Code, ex.Code);
                }
            }

            [Fact]
            public static void GetArtistAsync_ArtistHasNoAccessibleTrack_ThrowsDataNotFoundError()
            {
                var dbConnection = OpenSqliteDatabase();

                var dbContextOptionsBuilder = new DbContextOptionsBuilder<MediaInfoContext>()
                    .DisableClientSideEvaluation()
                    .UseSqlite(dbConnection);

                using (var dbContext = new MediaInfoContext(dbContextOptionsBuilder.Options))
                {
                    var random = new RandomPopulator(dbContext);
                    var user = random.AddUser();
                    var library = random.AddLibrary(accessControlled: true);
                    var artist = random.AddArtist();
                    var album = random.AddAlbum(artist);
                    var directory = random.AddDirectory(library);
                    var file = random.AddFile(directory);
                    var track = random.AddTrack(file, artist, album);
                    _ = dbContext.SaveChanges();

                    var ex = Assert.Throws<RestApiErrorException>(() => RestApiQueries.GetArtistAsync(dbContext, user.UserId, artist.ArtistId, CancellationToken.None).GetAwaiter().GetResult());

                    var expectedException = RestApiErrorException.DataNotFoundError();
                    Assert.Equal(expectedException.Message, ex.Message);
                    Assert.Equal(expectedException.Code, ex.Code);
                }
            }

            [Fact]
            public static void GetArtistAsync_ArtistHasAccessibleTrack_ReturnsExpectedArtistDetails()
            {
                var dbConnection = OpenSqliteDatabase();

                var dbContextOptionsBuilder = new DbContextOptionsBuilder<MediaInfoContext>()
                    .DisableClientSideEvaluation()
                    .UseSqlite(dbConnection);

                using (var dbContext = new MediaInfoContext(dbContextOptionsBuilder.Options))
                {
                    var random = new RandomPopulator(dbContext);
                    var user = random.AddUser();
                    var library = random.AddLibrary();
                    var artist = random.AddArtist();
                    var album = random.AddAlbum(artist);
                    var directory = random.AddDirectory(library);
                    var file = random.AddFile(directory);
                    var track = random.AddTrack(file, artist, album);
                    _ = dbContext.SaveChanges();

                    var result = RestApiQueries.GetArtistAsync(dbContext, user.UserId, artist.ArtistId, CancellationToken.None).GetAwaiter().GetResult();

                    Assert.Equal("r" + artist.ArtistId, result.id);
                    Assert.Equal(artist.Name, result.name);
                    Assert.Null(result.coverArt);
                    Assert.Equal(1, result.albumCount);
                    Assert.False(result.starredSpecified);
                    Assert.Equal(default, result.starred);
                }
            }

            [Fact]
            public static void GetArtistAsync_ArtistIsPlaceholder_ReturnsPlaceholderName()
            {
                var dbConnection = OpenSqliteDatabase();

                var dbContextOptionsBuilder = new DbContextOptionsBuilder<MediaInfoContext>()
                    .DisableClientSideEvaluation()
                    .UseSqlite(dbConnection);

                using (var dbContext = new MediaInfoContext(dbContextOptionsBuilder.Options))
                {
                    var random = new RandomPopulator(dbContext);
                    var user = random.AddUser();
                    var library = random.AddLibrary();
                    var artist = random.AddArtist();
                    artist.Name = null;
                    artist.SortName = null;
                    var album = random.AddAlbum(artist);
                    var directory = random.AddDirectory(library);
                    var file = random.AddFile(directory);
                    var track = random.AddTrack(file, artist, album);
                    _ = dbContext.SaveChanges();

                    var result = RestApiQueries.GetArtistAsync(dbContext, user.UserId, artist.ArtistId, CancellationToken.None).GetAwaiter().GetResult();

                    Assert.Equal("[no artist]", result.name);

                    var resultAlbum = Assert.Single(result.album);
                    Assert.Equal("[no artist]", resultAlbum.artist);
                }
            }

            [Theory]
            [InlineData(new int[] { 2 })]
            [InlineData(new int[] { 1, 3 })]
            public static void GetArtistAsync_Always_ReturnsExpectedAlbumDetails(int[] albumTrackCounts)
            {
                var dbConnection = OpenSqliteDatabase();

                var dbContextOptionsBuilder = new DbContextOptionsBuilder<MediaInfoContext>()
                    .DisableClientSideEvaluation()
                    .UseSqlite(dbConnection);

                using (var dbContext = new MediaInfoContext(dbContextOptionsBuilder.Options))
                {
                    var random = new RandomPopulator(dbContext);
                    var user = random.AddUser();
                    var library = random.AddLibrary();
                    var artist = random.AddArtist();
                    var albums = new List<Album>();
                    var tracks = new List<Track>();
                    for (int i = 0; i < albumTrackCounts.Length; ++i)
                    {
                        var directory = random.AddDirectory(library);
                        var album = random.AddAlbum(artist);
                        albums.Add(album);
                        for (int j = 0; j < albumTrackCounts[i]; ++j)
                        {
                            var file = random.AddFile(directory);
                            var track = random.AddTrack(file, artist, album);
                            tracks.Add(track);
                        }
                    }
                    var otherArtist = random.AddArtist();
                    var otherAlbum = random.AddAlbum(otherArtist);
                    var otherDirectory = random.AddDirectory(library);
                    var otherFile = random.AddFile(otherDirectory);
                    var otherTrack = random.AddTrack(otherFile, otherArtist, otherAlbum);
                    _ = dbContext.SaveChanges();

                    var result = RestApiQueries.GetArtistAsync(dbContext, user.UserId, artist.ArtistId, CancellationToken.None).GetAwaiter().GetResult();

                    Assert.Equal(albums.Count, result.albumCount);
                    Assert.Equal(albums.Count, result.album.Length);
                    Assert.Equal(new SortedSet<string>(albums.Select(a => "a" + a.AlbumId)),
                                 new SortedSet<string>(result.album.Select(a => a.id)));
                    foreach (var resultAlbum in result.album)
                    {
                        var album = albums.Single(a => "a" + a.AlbumId == resultAlbum.id);
                        var albumTracks = tracks.Where(t => t.Album == album);

                        Assert.Equal(album.Title, resultAlbum.name);
                        Assert.Equal(album.Artist.Name, resultAlbum.artist);
                        Assert.Equal("r" + album.ArtistId, resultAlbum.artistId);
                        Assert.Equal(album.CoverPictureId?.ToString("X8"), resultAlbum.coverArt);
                        Assert.Equal(albumTracks.Count(), resultAlbum.songCount);
                        Assert.Equal(Math.Round(albumTracks.Sum(t => t.Duration) ?? 0), resultAlbum.duration);
                        Assert.False(resultAlbum.playCountSpecified);
                        Assert.Equal(default, resultAlbum.playCount);
                        Assert.Equal(album.Added, resultAlbum.created);
                        Assert.False(resultAlbum.starredSpecified);
                        Assert.Equal(default, resultAlbum.starred);
                        Assert.Equal(album.Date.HasValue, resultAlbum.yearSpecified);
                        Assert.Equal(album.Date / 1_00_00 ?? 0, resultAlbum.year);
                        Assert.Equal(album.Genre?.Name, resultAlbum.genre);
                    }
                }
            }

            [Fact]
            public static void GetArtistAsync_AlbumIsPlaceholder_ReturnsPlaceholderTitle()
            {
                var dbConnection = OpenSqliteDatabase();

                var dbContextOptionsBuilder = new DbContextOptionsBuilder<MediaInfoContext>()
                    .DisableClientSideEvaluation()
                    .UseSqlite(dbConnection);

                using (var dbContext = new MediaInfoContext(dbContextOptionsBuilder.Options))
                {
                    var random = new RandomPopulator(dbContext);
                    var user = random.AddUser();
                    var library = random.AddLibrary();
                    var artist = random.AddArtist();
                    var album = random.AddAlbum(artist);
                    album.Title = null;
                    album.SortTitle = null;
                    var directory = random.AddDirectory(library);
                    var file = random.AddFile(directory);
                    var track = random.AddTrack(file, artist, album);
                    _ = dbContext.SaveChanges();

                    var result = RestApiQueries.GetArtistAsync(dbContext, user.UserId, artist.ArtistId, CancellationToken.None).GetAwaiter().GetResult();

                    var resultAlbum = Assert.Single(result.album);
                    Assert.Equal("[no album]", resultAlbum.name);
                }
            }

            [Fact]
            public static void GetArtistAsync_ArtistHasNoStar_StarredHasNoValue()
            {
                var dbConnection = OpenSqliteDatabase();

                var dbContextOptionsBuilder = new DbContextOptionsBuilder<MediaInfoContext>()
                    .DisableClientSideEvaluation()
                    .UseSqlite(dbConnection);

                using (var dbContext = new MediaInfoContext(dbContextOptionsBuilder.Options))
                {
                    var random = new RandomPopulator(dbContext);
                    var user = random.AddUser();
                    var library = random.AddLibrary();
                    var artist = random.AddArtist();
                    var album = random.AddAlbum(artist);
                    var directory = random.AddDirectory(library);
                    var file = random.AddFile(directory);
                    var track = random.AddTrack(file, artist, album);
                    _ = dbContext.SaveChanges();

                    var result = RestApiQueries.GetArtistAsync(dbContext, user.UserId, artist.ArtistId, CancellationToken.None).GetAwaiter().GetResult();

                    Assert.False(result.starredSpecified);
                    Assert.Equal(default, result.starred);
                }
            }

            [Fact]
            public static void GetArtistAsync_ArtistHasStar_StarredHasExpectedValue()
            {
                var dbConnection = OpenSqliteDatabase();

                var dbContextOptionsBuilder = new DbContextOptionsBuilder<MediaInfoContext>()
                    .DisableClientSideEvaluation()
                    .UseSqlite(dbConnection);

                using (var dbContext = new MediaInfoContext(dbContextOptionsBuilder.Options))
                {
                    var random = new RandomPopulator(dbContext);
                    var user = random.AddUser();
                    var library = random.AddLibrary();
                    var artist = random.AddArtist();
                    var artistStar = random.AddArtistStar(artist, user);
                    var album = random.AddAlbum(artist);
                    var directory = random.AddDirectory(library);
                    var file = random.AddFile(directory);
                    var track = random.AddTrack(file, artist, album);
                    _ = dbContext.SaveChanges();

                    var result = RestApiQueries.GetArtistAsync(dbContext, user.UserId, artist.ArtistId, CancellationToken.None).GetAwaiter().GetResult();

                    Assert.True(result.starredSpecified);
                    Assert.Equal(artistStar.Added, result.starred);
                }
            }

            [Fact]
            public static void GetArtistAsync_AlbumHasNoAccessibleTrack_AlbumIsNotReturned()
            {
                var dbConnection = OpenSqliteDatabase();

                var dbContextOptionsBuilder = new DbContextOptionsBuilder<MediaInfoContext>()
                    .DisableClientSideEvaluation()
                    .UseSqlite(dbConnection);

                using (var dbContext = new MediaInfoContext(dbContextOptionsBuilder.Options))
                {
                    var random = new RandomPopulator(dbContext);
                    var user = random.AddUser();
                    var artist = random.AddArtist();
                    var inaccessibleLibrary = random.AddLibrary(accessControlled: true);
                    var inaccessibleAlbum = random.AddAlbum(artist);
                    var inaccessibleDirectory = random.AddDirectory(inaccessibleLibrary);
                    var inaccessibleFile = random.AddFile(inaccessibleDirectory);
                    var inaccessibleTrack = random.AddTrack(inaccessibleFile, artist, inaccessibleAlbum);
                    var accessibleLibrary = random.AddLibrary(accessControlled: false);
                    var accessibleAlbum = random.AddAlbum(artist);
                    var accessibleDirectory = random.AddDirectory(accessibleLibrary);
                    var accessibleFile = random.AddFile(accessibleDirectory);
                    var accessibleTrack = random.AddTrack(accessibleFile, artist, accessibleAlbum);
                    _ = dbContext.SaveChanges();

                    var result = RestApiQueries.GetArtistAsync(dbContext, user.UserId, artist.ArtistId, CancellationToken.None).GetAwaiter().GetResult();

                    Assert.Equal(1, result.albumCount);
                    var resultAlbum = Assert.Single(result.album);
                    Assert.Equal("a" + accessibleAlbum.AlbumId, resultAlbum.id);
                }
            }

            [Fact]
            public static void GetArtistAsync_AlbumHasInaccessibleTrack_TrackIsNotCounted()
            {
                var dbConnection = OpenSqliteDatabase();

                var dbContextOptionsBuilder = new DbContextOptionsBuilder<MediaInfoContext>()
                    .DisableClientSideEvaluation()
                    .UseSqlite(dbConnection);

                using (var dbContext = new MediaInfoContext(dbContextOptionsBuilder.Options))
                {
                    var random = new RandomPopulator(dbContext);
                    var user = random.AddUser();
                    var artist = random.AddArtist();
                    var album = random.AddAlbum(artist);
                    var inaccessibleLibrary = random.AddLibrary(accessControlled: true);
                    var inaccessibleDirectory = random.AddDirectory(inaccessibleLibrary);
                    var inaccessibleFile = random.AddFile(inaccessibleDirectory);
                    var inaccessibleTrack = random.AddTrack(inaccessibleFile, artist, album);
                    var accessibleLibrary = random.AddLibrary(accessControlled: false);
                    var accessibleDirectory = random.AddDirectory(accessibleLibrary);
                    var accessibleFile = random.AddFile(accessibleDirectory);
                    var accessibleTrack = random.AddTrack(accessibleFile, artist, album);
                    _ = dbContext.SaveChanges();

                    var result = RestApiQueries.GetArtistAsync(dbContext, user.UserId, artist.ArtistId, CancellationToken.None).GetAwaiter().GetResult();

                    Assert.Equal(1, result.albumCount);
                    var resultAlbum = Assert.Single(result.album);
                    Assert.Equal(1, resultAlbum.songCount);
                    Assert.Equal(Math.Round(accessibleTrack.Duration ?? 0), resultAlbum.duration);
                }
            }

            [Fact]
            public static void GetArtistAsync_AlbumHasAccessibleAccessControlledTrack_AlbumIsReturned()
            {
                var dbConnection = OpenSqliteDatabase();

                var dbContextOptionsBuilder = new DbContextOptionsBuilder<MediaInfoContext>()
                    .DisableClientSideEvaluation()
                    .UseSqlite(dbConnection);

                using (var dbContext = new MediaInfoContext(dbContextOptionsBuilder.Options))
                {
                    var random = new RandomPopulator(dbContext);
                    var user = random.AddUser();
                    var library = random.AddLibrary(accessControlled: true);
                    var libraryUser = random.AddLibraryUser(library, user);
                    var artist = random.AddArtist();
                    var album = random.AddAlbum(artist);
                    var directory = random.AddDirectory(library);
                    var file = random.AddFile(directory);
                    var track = random.AddTrack(file, artist, album);
                    _ = dbContext.SaveChanges();

                    var result = RestApiQueries.GetArtistAsync(dbContext, user.UserId, artist.ArtistId, CancellationToken.None).GetAwaiter().GetResult();

                    Assert.Equal(1, result.albumCount);
                    var resultAlbum = Assert.Single(result.album);
                    Assert.Equal("a" + album.AlbumId, resultAlbum.id);
                }
            }

            [Fact]
            public static void GetArtistAsync_AlbumHasAccessibleNonAccessControlledTrack_AlbumIsReturned()
            {
                var dbConnection = OpenSqliteDatabase();

                var dbContextOptionsBuilder = new DbContextOptionsBuilder<MediaInfoContext>()
                    .DisableClientSideEvaluation()
                    .UseSqlite(dbConnection);

                using (var dbContext = new MediaInfoContext(dbContextOptionsBuilder.Options))
                {
                    var random = new RandomPopulator(dbContext);
                    var user = random.AddUser();
                    var library = random.AddLibrary(accessControlled: false);
                    var artist = random.AddArtist();
                    var album = random.AddAlbum(artist);
                    var directory = random.AddDirectory(library);
                    var file = random.AddFile(directory);
                    var track = random.AddTrack(file, artist, album);
                    _ = dbContext.SaveChanges();

                    var result = RestApiQueries.GetArtistAsync(dbContext, user.UserId, artist.ArtistId, CancellationToken.None).GetAwaiter().GetResult();

                    Assert.Equal(1, result.albumCount);
                    var resultAlbum = Assert.Single(result.album);
                    Assert.Equal("a" + album.AlbumId, resultAlbum.id);
                }
            }

            [Fact]
            public static void GetArtistAsync_AlbumHasNoStar_StarredHasNoValue()
            {
                var dbConnection = OpenSqliteDatabase();

                var dbContextOptionsBuilder = new DbContextOptionsBuilder<MediaInfoContext>()
                    .DisableClientSideEvaluation()
                    .UseSqlite(dbConnection);

                using (var dbContext = new MediaInfoContext(dbContextOptionsBuilder.Options))
                {
                    var random = new RandomPopulator(dbContext);
                    var user = random.AddUser();
                    var library = random.AddLibrary();
                    var artist = random.AddArtist();
                    var album = random.AddAlbum(artist);
                    var directory = random.AddDirectory(library);
                    var file = random.AddFile(directory);
                    var track = random.AddTrack(file, artist, album);
                    _ = dbContext.SaveChanges();

                    var result = RestApiQueries.GetArtistAsync(dbContext, user.UserId, artist.ArtistId, CancellationToken.None).GetAwaiter().GetResult();

                    var resultAlbum = Assert.Single(result.album);
                    Assert.False(resultAlbum.starredSpecified);
                    Assert.Equal(default, resultAlbum.starred);
                }
            }

            [Fact]
            public static void GetArtistAsync_AlbumHasStar_StarredHasExpectedValue()
            {
                var dbConnection = OpenSqliteDatabase();

                var dbContextOptionsBuilder = new DbContextOptionsBuilder<MediaInfoContext>()
                    .DisableClientSideEvaluation()
                    .UseSqlite(dbConnection);

                using (var dbContext = new MediaInfoContext(dbContextOptionsBuilder.Options))
                {
                    var random = new RandomPopulator(dbContext);
                    var user = random.AddUser();
                    var library = random.AddLibrary();
                    var artist = random.AddArtist();
                    var album = random.AddAlbum(artist);
                    var albumStar = random.AddAlbumStar(album, user);
                    var directory = random.AddDirectory(library);
                    var file = random.AddFile(directory);
                    var track = random.AddTrack(file, artist, album);
                    _ = dbContext.SaveChanges();

                    var result = RestApiQueries.GetArtistAsync(dbContext, user.UserId, artist.ArtistId, CancellationToken.None).GetAwaiter().GetResult();

                    var resultAlbum = Assert.Single(result.album);
                    Assert.True(resultAlbum.starredSpecified);
                    Assert.Equal(albumStar.Added, resultAlbum.starred);
                }
            }

            [Fact]
            public static void GetArtistAsync_AlbumHasNoDate_YearIsNotReturned()
            {
                var dbConnection = OpenSqliteDatabase();

                var dbContextOptionsBuilder = new DbContextOptionsBuilder<MediaInfoContext>()
                    .DisableClientSideEvaluation()
                    .UseSqlite(dbConnection);

                using (var dbContext = new MediaInfoContext(dbContextOptionsBuilder.Options))
                {
                    var random = new RandomPopulator(dbContext);
                    var user = random.AddUser();
                    var library = random.AddLibrary();
                    var artist = random.AddArtist();
                    var album = random.AddAlbum(artist);
                    album.Date = null;
                    var directory = random.AddDirectory(library);
                    var file = random.AddFile(directory);
                    var track = random.AddTrack(file, artist, album);
                    _ = dbContext.SaveChanges();

                    var result = RestApiQueries.GetArtistAsync(dbContext, user.UserId, artist.ArtistId, CancellationToken.None).GetAwaiter().GetResult();

                    var resultAlbum = Assert.Single(result.album);
                    Assert.False(resultAlbum.yearSpecified);
                    Assert.Equal(default, resultAlbum.year);
                }
            }

            [Fact]
            public static void GetArtistAsync_AlbumHasDate_YearIsReturned()
            {
                var dbConnection = OpenSqliteDatabase();

                var dbContextOptionsBuilder = new DbContextOptionsBuilder<MediaInfoContext>()
                    .DisableClientSideEvaluation()
                    .UseSqlite(dbConnection);

                using (var dbContext = new MediaInfoContext(dbContextOptionsBuilder.Options))
                {
                    var random = new RandomPopulator(dbContext);
                    var user = random.AddUser();
                    var library = random.AddLibrary();
                    var artist = random.AddArtist();
                    var album = random.AddAlbum(artist);
                    album.Date = 2000_01_02;
                    var directory = random.AddDirectory(library);
                    var file = random.AddFile(directory);
                    var track = random.AddTrack(file, artist, album);
                    _ = dbContext.SaveChanges();

                    var result = RestApiQueries.GetArtistAsync(dbContext, user.UserId, artist.ArtistId, CancellationToken.None).GetAwaiter().GetResult();

                    var resultAlbum = Assert.Single(result.album);
                    Assert.True(resultAlbum.yearSpecified);
                    Assert.Equal(album.Date / 1_00_00, resultAlbum.year);
                }
            }

            [Fact]
            public static void GetArtistAsync_AlbumHasNoGenre_GenreIsNotReturned()
            {
                var dbConnection = OpenSqliteDatabase();

                var dbContextOptionsBuilder = new DbContextOptionsBuilder<MediaInfoContext>()
                    .DisableClientSideEvaluation()
                    .UseSqlite(dbConnection);

                using (var dbContext = new MediaInfoContext(dbContextOptionsBuilder.Options))
                {
                    var random = new RandomPopulator(dbContext);
                    var user = random.AddUser();
                    var library = random.AddLibrary();
                    var artist = random.AddArtist();
                    var genre = random.AddGenre();
                    var album = random.AddAlbum(artist, genre: null);
                    var directory = random.AddDirectory(library);
                    var file = random.AddFile(directory);
                    var track = random.AddTrack(file, artist, album);
                    var trackGenre = random.AddTrackGenre(track, genre);
                    _ = dbContext.SaveChanges();

                    var result = RestApiQueries.GetArtistAsync(dbContext, user.UserId, artist.ArtistId, CancellationToken.None).GetAwaiter().GetResult();

                    var resultAlbum = Assert.Single(result.album);
                    Assert.Null(resultAlbum.genre);
                }
            }

            [Fact]
            public static void GetArtistAsync_AlbumHasGenre_GenreIsReturned()
            {
                var dbConnection = OpenSqliteDatabase();

                var dbContextOptionsBuilder = new DbContextOptionsBuilder<MediaInfoContext>()
                    .DisableClientSideEvaluation()
                    .UseSqlite(dbConnection);

                using (var dbContext = new MediaInfoContext(dbContextOptionsBuilder.Options))
                {
                    var random = new RandomPopulator(dbContext);
                    var user = random.AddUser();
                    var library = random.AddLibrary();
                    var artist = random.AddArtist();
                    var genre = random.AddGenre();
                    var album = random.AddAlbum(artist, genre: genre);
                    var directory = random.AddDirectory(library);
                    var file = random.AddFile(directory);
                    var track = random.AddTrack(file, artist, album);
                    var trackGenre = random.AddTrackGenre(track, genre);
                    _ = dbContext.SaveChanges();

                    var result = RestApiQueries.GetArtistAsync(dbContext, user.UserId, artist.ArtistId, CancellationToken.None).GetAwaiter().GetResult();

                    var resultAlbum = Assert.Single(result.album);
                    Assert.Equal(genre.Name, resultAlbum.genre);
                }
            }

            [Fact]
            public static void GetArtistAsync_Always_AlbumsAreInExpectedOrder()
            {
                var dbConnection = OpenSqliteDatabase();

                var dbContextOptionsBuilder = new DbContextOptionsBuilder<MediaInfoContext>()
                    .DisableClientSideEvaluation()
                    .UseSqlite(dbConnection);

                using (var dbContext = new MediaInfoContext(dbContextOptionsBuilder.Options))
                {
                    var random = new RandomPopulator(dbContext);
                    var user = random.AddUser();
                    var library = random.AddLibrary();
                    var artist = random.AddArtist();
                    var albums = new List<Album>();
                    for (int i = 0; i < 2; ++i)
                    {
                        foreach (string sortTitle in new[]
                            {
                                "A",
                                "a",
                                "C",
                                "𝓏",
                                "𓂀",
                                null,
                                "B",
                                "b",
                            })
                        {
                            foreach (int? date in new int?[]
                                {
                                    1999_99_99,
                                    2000_00_00,
                                    2000_03_04,
                                    null,
                                    2000_01_02,
                                })
                            {
                                var album = random.AddAlbum(artist);
                                album.Date = date;
                                album.SortTitle = sortTitle;
                                albums.Add(album);
                                var directory = random.AddDirectory(library);
                                var file = random.AddFile(directory);
                                var track = random.AddTrack(file, artist, album);
                            }
                        }
                    }
                    _ = dbContext.SaveChanges();

                    albums = albums
                        .OrderBy(a => a.Date)
                        .ThenBy(a => a.SortTitle ?? a.Title, _stringComparer)
                        .ThenBy(a => a.AlbumId)
                        .ToList();

                    var result = RestApiQueries.GetArtistAsync(dbContext, user.UserId, artist.ArtistId, CancellationToken.None).GetAwaiter().GetResult();

                    Assert.Equal(albums.Select(a => "a" + a.AlbumId).ToArray(),
                                 result.album.Select(a => a.id).ToArray());
                }
            }
        }
    }
}
