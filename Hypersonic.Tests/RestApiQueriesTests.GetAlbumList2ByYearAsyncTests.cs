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
        public static class GetAlbumList2ByYearAsyncTests
        {
            private static readonly StringComparer _stringComparer = CultureInfo.CurrentCulture.CompareInfo.GetStringComparer(CompareOptions.IgnoreCase);

            [Fact]
            public static void GetAlbumList2ByYearAsync_LibraryDoesNotExist_ThrowsDataNotFoundError()
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
                    album.Date = 2000_00_00;
                    var directory = random.AddDirectory(library);
                    var file = random.AddFile(directory);
                    var track = random.AddTrack(file, artist, album);
                    dbContext.SaveChanges();

                    var ex = Assert.Throws<RestApiErrorException>(() => RestApiQueries.GetAlbumList2ByYearAsync(dbContext, user.UserId, library.LibraryId + 1, 0, 10, 1999, 2001, CancellationToken.None).GetAwaiter().GetResult());

                    var expectedException = RestApiErrorException.DataNotFoundError();
                    Assert.Equal(expectedException.Message, ex.Message);
                    Assert.Equal(expectedException.Code, ex.Code);
                }
            }

            [Theory]
            [InlineData(1999, 2001)]
            [InlineData(2000, 2000)]
            [InlineData(2001, 1999)]
            public static void GetAlbumList2ByYearAsync_LibraryIsSpecified_ReturnsExpectedAlbums(int fromYear, int toYear)
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
                    var otherLibrary = random.AddLibrary();
                    var artist = random.AddArtist();
                    var album = random.AddAlbum(artist);
                    album.Date = 2000_00_00;
                    var directory = random.AddDirectory(library);
                    var file = random.AddFile(directory);
                    var track = random.AddTrack(file, artist, album);
                    var otherDirectory = random.AddDirectory(otherLibrary);
                    var otherFile = random.AddFile(otherDirectory);
                    var otherTrack = random.AddTrack(otherFile, artist, album);
                    dbContext.SaveChanges();

                    var result = RestApiQueries.GetAlbumList2ByYearAsync(dbContext, user.UserId, library.LibraryId, 0, 10, fromYear, toYear, CancellationToken.None).GetAwaiter().GetResult();

                    var resultAlbum = Assert.Single(result.album);
                    Assert.Equal("a" + album.AlbumId, resultAlbum.id);
                    Assert.Equal(1, resultAlbum.songCount);
                    Assert.Equal(Math.Round(track.Duration ?? 0), resultAlbum.duration);
                }
            }

            [Theory]
            [InlineData(5, 10, 1999, 2001)]
            [InlineData(10, 5, 1999, 2001)]
            [InlineData(5, 10, 2000, 2000)]
            [InlineData(10, 5, 2000, 2000)]
            [InlineData(5, 10, 2001, 1999)]
            [InlineData(10, 5, 2001, 1999)]
            public static void GetAlbumList2ByYearAsync_VariousOffsetAndCount_ReturnsExpectedAlbumDetails(int albumCount, int count, int fromYear, int toYear)
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
                    for (int i = 0; i < albumCount; ++i)
                    {
                        var album = random.AddAlbum(artist);
                        album.Date = 1999_01_01 + (i % 3 * 1_00_00) + (i % 12 * 1_00) + (i % 28);
                        albums.Add(album);
                        var directory = random.AddDirectory(library);
                        var file = random.AddFile(directory);
                        var track = random.AddTrack(file, artist, album);
                        tracks.Add(track);
                    }
                    dbContext.SaveChanges();

                    if (fromYear <= toYear)
                    {
                        albums = albums
                            .Where(a => a.Date >= fromYear * 1_00_00 && a.Date < (toYear + 1) * 1_00_00)
                            .OrderBy(a => a.Date)
                            .ThenBy(a => a.SortTitle ?? a.Title, _stringComparer)
                            .ThenBy(a => a.AlbumId)
                            .ToList();
                    }
                    else
                    {
                        albums = albums
                            .Where(a => a.Date >= toYear * 1_00_00 && a.Date < (fromYear + 1) * 1_00_00)
                            .OrderByDescending(a => a.Date)
                            .ThenByDescending(a => a.SortTitle ?? a.Title, _stringComparer)
                            .ThenByDescending(a => a.AlbumId)
                            .ToList();
                    }
                    Assert.NotEmpty(albums);

                    for (int i = 0; i <= albumCount; ++i)
                    {
                        var result = RestApiQueries.GetAlbumList2ByYearAsync(dbContext, user.UserId, null, i, count, fromYear, toYear, CancellationToken.None).GetAwaiter().GetResult();

                        Assert.Equal(albums.Skip(i).Take(count).Select(a => "a" + a.AlbumId).ToArray(),
                                     result.album.Select(a => a.id).ToArray());
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
            }

            [Theory]
            [InlineData(1999, 2001)]
            [InlineData(2000, 2000)]
            [InlineData(2001, 1999)]
            public static void GetAlbumList2ByYearAsync_NoTracksInYearRange_ReturnsNoAlbums(int fromYear, int toYear)
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
                    album.Date = 1998_00_00;
                    var directory = random.AddDirectory(library);
                    var file = random.AddFile(directory);
                    var track = random.AddTrack(file, artist, album);
                    dbContext.SaveChanges();

                    var result = RestApiQueries.GetAlbumList2ByYearAsync(dbContext, user.UserId, null, 0, 10, fromYear, toYear, CancellationToken.None).GetAwaiter().GetResult();

                    Assert.Empty(result.album);
                }
            }

            [Theory]
            [InlineData(1999, 2001)]
            [InlineData(2000, 2000)]
            [InlineData(2001, 1999)]
            public static void GetAlbumList2ByYearAsync_AlbumIsPlaceholder_AlbumIsNotReturned(int fromYear, int toYear)
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
                    album.Date = 2000_00_00;
                    var directory = random.AddDirectory(library);
                    var file = random.AddFile(directory);
                    var track = random.AddTrack(file, artist, album);
                    dbContext.SaveChanges();

                    var result = RestApiQueries.GetAlbumList2ByYearAsync(dbContext, user.UserId, null, 0, 10, fromYear, toYear, CancellationToken.None).GetAwaiter().GetResult();

                    Assert.Empty(result.album);
                }
            }

            [Theory]
            [InlineData(1999, 2001)]
            [InlineData(2000, 2000)]
            [InlineData(2001, 1999)]
            public static void GetAlbumList2ByYearAsync_AlbumArtistIsPlaceholder_ReturnsPlacholderName(int fromYear, int toYear)
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
                    var albumArtist = random.AddArtist();
                    albumArtist.Name = null;
                    albumArtist.SortName = null;
                    var genre = random.AddGenre();
                    var album = random.AddAlbum(albumArtist, genre: genre);
                    album.Date = 2000_00_00;
                    var directory = random.AddDirectory(library);
                    var file = random.AddFile(directory);
                    var trackArtist = random.AddArtist();
                    var track = random.AddTrack(file, trackArtist, album);
                    var trackGenre = random.AddTrackGenre(track, genre);
                    dbContext.SaveChanges();

                    var result = RestApiQueries.GetAlbumList2ByYearAsync(dbContext, user.UserId, null, 0, 10, fromYear, toYear, CancellationToken.None).GetAwaiter().GetResult();

                    var resultAlbum = Assert.Single(result.album);
                    Assert.Equal("[no artist]", resultAlbum.artist);
                }
            }

            [Theory]
            [InlineData(1999, 2001)]
            [InlineData(2000, 2000)]
            [InlineData(2001, 1999)]
            public static void GetAlbumList2ByYearAsync_NoAccessibleTrack_ReturnsNoAlbums(int fromYear, int toYear)
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
                    album.Date = 2000_00_00;
                    var directory = random.AddDirectory(library);
                    var file = random.AddFile(directory);
                    var track = random.AddTrack(file, artist, album);
                    dbContext.SaveChanges();

                    var result = RestApiQueries.GetAlbumList2ByYearAsync(dbContext, user.UserId, null, 0, 10, fromYear, toYear, CancellationToken.None).GetAwaiter().GetResult();

                    Assert.Empty(result.album);
                }
            }

            [Theory]
            [InlineData(1999, 2001)]
            [InlineData(2000, 2000)]
            [InlineData(2001, 1999)]
            public static void GetAlbumList2ByYearAsync_AlbumHasNoAccessibleTrack_AlbumIsNotReturned(int fromYear, int toYear)
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
                    inaccessibleAlbum.Date = 2000_00_00;
                    var inaccessibleDirectory = random.AddDirectory(inaccessibleLibrary);
                    var inaccessibleFile = random.AddFile(inaccessibleDirectory);
                    var inaccessibleTrack = random.AddTrack(inaccessibleFile, artist, inaccessibleAlbum);
                    var accessibleLibrary = random.AddLibrary(accessControlled: false);
                    var accessibleAlbum = random.AddAlbum(artist);
                    accessibleAlbum.Date = 2000_00_00;
                    var accessibleDirectory = random.AddDirectory(accessibleLibrary);
                    var accessibleFile = random.AddFile(accessibleDirectory);
                    var accessibleTrack = random.AddTrack(accessibleFile, artist, accessibleAlbum);
                    dbContext.SaveChanges();

                    var result = RestApiQueries.GetAlbumList2ByYearAsync(dbContext, user.UserId, null, 0, 10, fromYear, toYear, CancellationToken.None).GetAwaiter().GetResult();

                    var resultAlbum = Assert.Single(result.album);
                    Assert.Equal("a" + accessibleAlbum.AlbumId, resultAlbum.id);
                }
            }

            [Theory]
            [InlineData(1999, 2001)]
            [InlineData(2000, 2000)]
            [InlineData(2001, 1999)]
            public static void GetAlbumList2ByYearAsync_AlbumHasInaccessibleTrack_TrackIsNotCounted(int fromYear, int toYear)
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
                    album.Date = 2000_00_00;
                    var inaccessibleLibrary = random.AddLibrary(accessControlled: true);
                    var inaccessibleDirectory = random.AddDirectory(inaccessibleLibrary);
                    var inaccessibleFile = random.AddFile(inaccessibleDirectory);
                    var inaccessibleTrack = random.AddTrack(inaccessibleFile, artist, album);
                    var accessibleLibrary = random.AddLibrary(accessControlled: false);
                    var accessibleDirectory = random.AddDirectory(accessibleLibrary);
                    var accessibleFile = random.AddFile(accessibleDirectory);
                    var accessibleTrack = random.AddTrack(accessibleFile, artist, album);
                    dbContext.SaveChanges();

                    var result = RestApiQueries.GetAlbumList2ByYearAsync(dbContext, user.UserId, null, 0, 10, fromYear, toYear, CancellationToken.None).GetAwaiter().GetResult();

                    var resultAlbum = Assert.Single(result.album);
                    Assert.Equal(1, resultAlbum.songCount);
                    Assert.Equal(Math.Round(accessibleTrack.Duration ?? 0), resultAlbum.duration);
                }
            }

            [Theory]
            [InlineData(1999, 2001)]
            [InlineData(2000, 2000)]
            [InlineData(2001, 1999)]
            public static void GetAlbumList2ByYearAsync_AlbumHasAccessibleAccessControlledTrack_AlbumIsReturned(int fromYear, int toYear)
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
                    album.Date = 2000_00_00;
                    var directory = random.AddDirectory(library);
                    var file = random.AddFile(directory);
                    var track = random.AddTrack(file, artist, album);
                    dbContext.SaveChanges();

                    var result = RestApiQueries.GetAlbumList2ByYearAsync(dbContext, user.UserId, null, 0, 10, fromYear, toYear, CancellationToken.None).GetAwaiter().GetResult();

                    Assert.Single(result.album);
                }
            }

            [Theory]
            [InlineData(1999, 2001)]
            [InlineData(2000, 2000)]
            [InlineData(2001, 1999)]
            public static void GetAlbumList2ByYearAsync_AlbumHasAccessibleNonAccessControlledTrack_AlbumIsReturned(int fromYear, int toYear)
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
                    album.Date = 2000_00_00;
                    var directory = random.AddDirectory(library);
                    var file = random.AddFile(directory);
                    var track = random.AddTrack(file, artist, album);
                    dbContext.SaveChanges();

                    var result = RestApiQueries.GetAlbumList2ByYearAsync(dbContext, user.UserId, null, 0, 10, fromYear, toYear, CancellationToken.None).GetAwaiter().GetResult();

                    Assert.Single(result.album);
                }
            }

            [Theory]
            [InlineData(1998_99_99, 1999, 2000)]
            [InlineData(1998_99_99, 1999, 2001)]
            [InlineData(1998_99_99, 2000, 2001)]
            [InlineData(1998_99_99, 2000, 2000)]
            [InlineData(2002_00_00, 1999, 2000)]
            [InlineData(2002_00_00, 1999, 2001)]
            [InlineData(2002_00_00, 2000, 2001)]
            [InlineData(2002_00_00, 2000, 2000)]
            [InlineData(null, 1999, 2000)]
            [InlineData(null, 1999, 2001)]
            [InlineData(null, 2000, 2000)]
            [InlineData(null, 2000, 2001)]
            public static void GetAlbumList2ByYearAsync_AlbumDateNotInYearRange_AlbumIsNotReturned(int? date, int fromYear, int toYear)
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
                    album.Date = date;
                    var directory = random.AddDirectory(library);
                    var file = random.AddFile(directory);
                    var track = random.AddTrack(file, artist, album);
                    dbContext.SaveChanges();

                    var result = RestApiQueries.GetAlbumList2ByYearAsync(dbContext, user.UserId, null, 0, 10, fromYear, toYear, CancellationToken.None).GetAwaiter().GetResult();

                    Assert.Empty(result.album);
                }
            }

            [Theory]
            [InlineData(2000_00_00, 1999, 2000)]
            [InlineData(2000_00_00, 1999, 2001)]
            [InlineData(2000_00_00, 2000, 2001)]
            [InlineData(2000_00_00, 2000, 2000)]
            [InlineData(2000_99_99, 2000, 2000)]
            public static void GetAlbumList2ByYearAsync_AlbumDateInYearRange_AlbumIsNotReturned(int? date, int fromYear, int toYear)
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
                    album.Date = date;
                    var directory = random.AddDirectory(library);
                    var file = random.AddFile(directory);
                    var track = random.AddTrack(file, artist, album);
                    dbContext.SaveChanges();

                    var result = RestApiQueries.GetAlbumList2ByYearAsync(dbContext, user.UserId, null, 0, 10, fromYear, toYear, CancellationToken.None).GetAwaiter().GetResult();

                    var resultAlbum = Assert.Single(result.album);
                    Assert.Equal("a" + album.AlbumId, resultAlbum.id);
                }
            }

            [Theory]
            [InlineData(1999, 2001)]
            [InlineData(2000, 2000)]
            [InlineData(2001, 1999)]
            public static void GetAlbumList2ByYearAsync_AlbumHasNoStar_StarredHasNoValue(int fromYear, int toYear)
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
                    album.Date = 2000_00_00;
                    var directory = random.AddDirectory(library);
                    var file = random.AddFile(directory);
                    var track = random.AddTrack(file, artist, album);
                    dbContext.SaveChanges();

                    var result = RestApiQueries.GetAlbumList2ByYearAsync(dbContext, user.UserId, null, 0, 10, fromYear, toYear, CancellationToken.None).GetAwaiter().GetResult();

                    var resultAlbum = Assert.Single(result.album);
                    Assert.False(resultAlbum.starredSpecified);
                    Assert.Equal(default, resultAlbum.starred);
                }
            }

            [Theory]
            [InlineData(1999, 2001)]
            [InlineData(2000, 2000)]
            [InlineData(2001, 1999)]
            public static void GetAlbumList2ByYearAsync_AlbumHasStar_StarredHasExpectedValue(int fromYear, int toYear)
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
                    album.Date = 2000_00_00;
                    var albumStar = random.AddAlbumStar(album, user);
                    var directory = random.AddDirectory(library);
                    var file = random.AddFile(directory);
                    var track = random.AddTrack(file, artist, album);
                    dbContext.SaveChanges();

                    var result = RestApiQueries.GetAlbumList2ByYearAsync(dbContext, user.UserId, null, 0, 10, fromYear, toYear, CancellationToken.None).GetAwaiter().GetResult();

                    var resultAlbum = Assert.Single(result.album);
                    Assert.True(resultAlbum.starredSpecified);
                    Assert.Equal(albumStar.Added, resultAlbum.starred);
                }
            }

            [Theory]
            [InlineData(1999, 2001)]
            [InlineData(2000, 2000)]
            [InlineData(2001, 1999)]
            public static void GetAlbumList2ByYearAsync_AlbumHasDate_YearIsReturned(int fromYear, int toYear)
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
                    dbContext.SaveChanges();

                    var result = RestApiQueries.GetAlbumList2ByYearAsync(dbContext, user.UserId, null, 0, 10, fromYear, toYear, CancellationToken.None).GetAwaiter().GetResult();

                    var resultAlbum = Assert.Single(result.album);
                    Assert.True(resultAlbum.yearSpecified);
                    Assert.Equal(album.Date / 1_00_00, resultAlbum.year);
                }
            }

            [Theory]
            [InlineData(1999, 2001)]
            [InlineData(2000, 2000)]
            [InlineData(2001, 1999)]
            public static void GetAlbumList2ByYearAsync_AlbumHasNoGenre_GenreIsNotReturned(int fromYear, int toYear)
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
                    album.Date = 2000_00_00;
                    var directory = random.AddDirectory(library);
                    var file = random.AddFile(directory);
                    var track = random.AddTrack(file, artist, album);
                    var trackGenre = random.AddTrackGenre(track, genre);
                    dbContext.SaveChanges();

                    var result = RestApiQueries.GetAlbumList2ByYearAsync(dbContext, user.UserId, null, 0, 10, fromYear, toYear, CancellationToken.None).GetAwaiter().GetResult();

                    var resultAlbum = Assert.Single(result.album);
                    Assert.Null(resultAlbum.genre);
                }
            }

            [Theory]
            [InlineData(1999, 2001)]
            [InlineData(2000, 2000)]
            [InlineData(2001, 1999)]
            public static void GetAlbumList2ByYearAsync_AlbumHasGenre_GenreIsReturned(int fromYear, int toYear)
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
                    album.Date = 2000_00_00;
                    var directory = random.AddDirectory(library);
                    var file = random.AddFile(directory);
                    var track = random.AddTrack(file, artist, album);
                    var trackGenre = random.AddTrackGenre(track, genre);
                    dbContext.SaveChanges();

                    var result = RestApiQueries.GetAlbumList2ByYearAsync(dbContext, user.UserId, null, 0, 10, fromYear, toYear, CancellationToken.None).GetAwaiter().GetResult();

                    var resultAlbum = Assert.Single(result.album);
                    Assert.Equal(genre.Name, resultAlbum.genre);
                }
            }

            [Theory]
            [InlineData(1999, 2001)]
            [InlineData(2000, 2000)]
            [InlineData(2001, 1999)]
            public static void GetAlbumList2ByYearAsync_Always_AlbumsAreInExpectedOrder(int fromYear, int toYear)
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
                    dbContext.SaveChanges();

                    if (fromYear <= toYear)
                    {
                        albums = albums
                            .Where(a => a.Date >= fromYear * 1_00_00 && a.Date < (toYear + 1) * 1_00_00)
                            .OrderBy(a => a.Date)
                            .ThenBy(a => a.SortTitle ?? a.Title, _stringComparer)
                            .ThenBy(a => a.AlbumId)
                            .ToList();
                    }
                    else
                    {
                        albums = albums
                            .Where(a => a.Date >= toYear * 1_00_00 && a.Date < (fromYear + 1) * 1_00_00)
                            .OrderByDescending(a => a.Date)
                            .ThenByDescending(a => a.SortTitle ?? a.Title, _stringComparer)
                            .ThenByDescending(a => a.AlbumId)
                            .ToList();
                    }
                    Assert.NotEmpty(albums);

                    var result = RestApiQueries.GetAlbumList2ByYearAsync(dbContext, user.UserId, null, 0, albums.Count, fromYear, toYear, CancellationToken.None).GetAwaiter().GetResult();

                    Assert.Equal(albums.Select(a => "a" + a.AlbumId).ToArray(),
                                 result.album.Select(a => a.id).ToArray());
                }
            }
        }
    }
}
