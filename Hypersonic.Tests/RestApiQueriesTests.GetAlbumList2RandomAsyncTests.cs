﻿//
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
using System.Linq;
using System.Threading;
using Xunit;
using static Hypersonic.Tests.Helpers;

namespace Hypersonic.Tests
{
    public static partial class RestApiQueriesTests
    {
        public static class GetAlbumList2RandomAsyncTests
        {
            [Fact]
            public static void GetAlbumList2RandomAsync_LibraryDoesNotExist_ThrowsDataNotFoundError()
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
                    dbContext.SaveChanges();

                    var ex = Assert.Throws<RestApiErrorException>(() => RestApiQueries.GetAlbumList2RandomAsync(dbContext, user.UserId, library.LibraryId + 1, 10, CancellationToken.None).GetAwaiter().GetResult());

                    var expectedException = RestApiErrorException.DataNotFoundError();
                    Assert.Equal(expectedException.Message, ex.Message);
                    Assert.Equal(expectedException.Code, ex.Code);
                }
            }

            [Fact]
            public static void GetAlbumList2RandomAsync_LibraryIsSpecified_ReturnsExpectedAlbums()
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
                    var directory = random.AddDirectory(library);
                    var file = random.AddFile(directory);
                    var track = random.AddTrack(file, artist, album);
                    var otherDirectory = random.AddDirectory(otherLibrary);
                    var otherFile = random.AddFile(otherDirectory);
                    var otherTrack = random.AddTrack(otherFile, artist, album);
                    dbContext.SaveChanges();

                    var result = RestApiQueries.GetAlbumList2RandomAsync(dbContext, user.UserId, library.LibraryId, 10, CancellationToken.None).GetAwaiter().GetResult();

                    var resultAlbum = Assert.Single(result.album);
                    Assert.Equal("a" + album.AlbumId, resultAlbum.id);
                    Assert.Equal(1, resultAlbum.songCount);
                    Assert.Equal(Math.Round(track.Duration ?? 0), resultAlbum.duration);
                }
            }

            [Theory]
            [InlineData(5, 10)]
            [InlineData(10, 5)]
            public static void GetAlbumList2RandomAsync_VariousCount_ReturnsExpectedAlbumDetails(int albumCount, int count)
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
                        albums.Add(album);
                        var directory = random.AddDirectory(library);
                        var file = random.AddFile(directory);
                        var track = random.AddTrack(file, artist, album);
                        tracks.Add(track);
                    }
                    dbContext.SaveChanges();

                    var result = RestApiQueries.GetAlbumList2RandomAsync(dbContext, user.UserId, null, count, CancellationToken.None).GetAwaiter().GetResult();

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
            public static void GetAlbumList2RandomAsync_AlbumIsPlaceholder_AlbumIsNotReturned()
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
                    dbContext.SaveChanges();

                    var result = RestApiQueries.GetAlbumList2RandomAsync(dbContext, user.UserId, null, 10, CancellationToken.None).GetAwaiter().GetResult();

                    Assert.Empty(result.album);
                }
            }

            [Fact]
            public static void GetAlbumList2RandomAsync_AlbumArtistIsPlaceholder_ReturnsPlacholderName()
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
                    var album = random.AddAlbum(albumArtist);
                    var directory = random.AddDirectory(library);
                    var file = random.AddFile(directory);
                    var trackArtist = random.AddArtist();
                    var track = random.AddTrack(file, trackArtist, album);
                    dbContext.SaveChanges();

                    var result = RestApiQueries.GetAlbumList2RandomAsync(dbContext, user.UserId, null, 10, CancellationToken.None).GetAwaiter().GetResult();

                    var resultAlbum = Assert.Single(result.album);
                    Assert.Equal("[no artist]", resultAlbum.artist);
                }
            }

            [Fact]
            public static void GetAlbumList2RandomAsync_NoAccessibleTrack_ReturnsNoAlbums()
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
                    dbContext.SaveChanges();

                    var result = RestApiQueries.GetAlbumList2RandomAsync(dbContext, user.UserId, null, 10, CancellationToken.None).GetAwaiter().GetResult();

                    Assert.Empty(result.album);
                }
            }

            [Fact]
            public static void GetAlbumList2RandomAsync_AlbumHasNoAccessibleTrack_AlbumIsNotReturned()
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
                    dbContext.SaveChanges();

                    var result = RestApiQueries.GetAlbumList2RandomAsync(dbContext, user.UserId, null, 10, CancellationToken.None).GetAwaiter().GetResult();

                    var resultAlbum = Assert.Single(result.album);
                    Assert.Equal("a" + accessibleAlbum.AlbumId, resultAlbum.id);
                }
            }

            [Fact]
            public static void GetAlbumList2RandomAsync_AlbumHasInaccessibleTrack_TrackIsNotCounted()
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
                    dbContext.SaveChanges();

                    var result = RestApiQueries.GetAlbumList2RandomAsync(dbContext, user.UserId, null, 10, CancellationToken.None).GetAwaiter().GetResult();

                    var resultAlbum = Assert.Single(result.album);
                    Assert.Equal(1, resultAlbum.songCount);
                    Assert.Equal(Math.Round(accessibleTrack.Duration ?? 0), resultAlbum.duration);
                }
            }

            [Fact]
            public static void GetAlbumList2RandomAsync_AlbumHasAccessibleAccessControlledTrack_AlbumIsReturned()
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
                    dbContext.SaveChanges();

                    var result = RestApiQueries.GetAlbumList2RandomAsync(dbContext, user.UserId, null, 10, CancellationToken.None).GetAwaiter().GetResult();

                    var resultAlbum = Assert.Single(result.album);
                    Assert.Equal("a" + album.AlbumId, resultAlbum.id);
                }
            }

            [Fact]
            public static void GetAlbumList2RandomAsync_AlbumHasAccessibleNonAccessControlledTrack_AlbumIsReturned()
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
                    dbContext.SaveChanges();

                    var result = RestApiQueries.GetAlbumList2RandomAsync(dbContext, user.UserId, null, 10, CancellationToken.None).GetAwaiter().GetResult();

                    var resultAlbum = Assert.Single(result.album);
                    Assert.Equal("a" + album.AlbumId, resultAlbum.id);
                }
            }

            [Fact]
            public static void GetAlbumList2RandomAsync_AlbumHasNoStar_StarredHasNoValue()
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
                    dbContext.SaveChanges();

                    var result = RestApiQueries.GetAlbumList2RandomAsync(dbContext, user.UserId, null, 10, CancellationToken.None).GetAwaiter().GetResult();

                    var resultAlbum = Assert.Single(result.album);
                    Assert.False(resultAlbum.starredSpecified);
                    Assert.Equal(default, resultAlbum.starred);
                }
            }

            [Fact]
            public static void GetAlbumList2RandomAsync_AlbumHasStar_StarredHasExpectedValue()
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
                    dbContext.SaveChanges();

                    var result = RestApiQueries.GetAlbumList2RandomAsync(dbContext, user.UserId, null, 10, CancellationToken.None).GetAwaiter().GetResult();

                    var resultAlbum = Assert.Single(result.album);
                    Assert.True(resultAlbum.starredSpecified);
                    Assert.Equal(albumStar.Added, resultAlbum.starred);
                }
            }

            [Fact]
            public static void GetAlbumList2RandomAsync_AlbumHasNoDate_YearIsNotReturned()
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
                    dbContext.SaveChanges();

                    var result = RestApiQueries.GetAlbumList2RandomAsync(dbContext, user.UserId, null, 10, CancellationToken.None).GetAwaiter().GetResult();

                    var resultAlbum = Assert.Single(result.album);
                    Assert.False(resultAlbum.yearSpecified);
                    Assert.Equal(default, resultAlbum.year);
                }
            }

            [Fact]
            public static void GetAlbumList2RandomAsync_AlbumHasDate_YearIsReturned()
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

                    var result = RestApiQueries.GetAlbumList2RandomAsync(dbContext, user.UserId, null, 10, CancellationToken.None).GetAwaiter().GetResult();

                    var resultAlbum = Assert.Single(result.album);
                    Assert.True(resultAlbum.yearSpecified);
                    Assert.Equal(album.Date / 1_00_00, resultAlbum.year);
                }
            }

            [Fact]
            public static void GetAlbumList2RandomAsync_AlbumHasNoGenre_GenreIsNotReturned()
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
                    album.Genre = null;
                    var directory = random.AddDirectory(library);
                    var file = random.AddFile(directory);
                    var track = random.AddTrack(file, artist, album);
                    var trackGenre = random.AddTrackGenre(track, genre);
                    dbContext.SaveChanges();

                    var result = RestApiQueries.GetAlbumList2RandomAsync(dbContext, user.UserId, null, 10, CancellationToken.None).GetAwaiter().GetResult();

                    var resultAlbum = Assert.Single(result.album);
                    Assert.Null(resultAlbum.genre);
                }
            }

            [Fact]
            public static void GetAlbumList2RandomAsync_AlbumHasGenre_GenreIsReturned()
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
                    dbContext.SaveChanges();

                    var result = RestApiQueries.GetAlbumList2RandomAsync(dbContext, user.UserId, null, 10, CancellationToken.None).GetAwaiter().GetResult();

                    var resultAlbum = Assert.Single(result.album);
                    Assert.Equal(genre.Name, resultAlbum.genre);
                }
            }

            [Fact]
            public static void GetAlbumList2RandomAsync_Always_AlbumsAreInRandomOrder()
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
                    var albums = new List<Album>();
                    for (int i = 0; i < 10; ++i)
                    {
                        var artist = random.AddArtist();
                        var album = random.AddAlbum(artist);
                        albums.Add(album);
                        var directory = random.AddDirectory(library);
                        var file = random.AddFile(directory);
                        var track = random.AddTrack(file, artist, album);
                    }
                    dbContext.SaveChanges();

                    var result1 = RestApiQueries.GetAlbumList2RandomAsync(dbContext, user.UserId, null, albums.Count, CancellationToken.None).GetAwaiter().GetResult();

                    Assert.Equal(new HashSet<string>(albums.Select(a => "a" + a.AlbumId)),
                                 new HashSet<string>(result1.album.Select(a => a.id)));

                    // Results are random, so assertions might fail, so retry a few times.
                    for (int i = 9; i >= 0; --i)
                    {
                        var result2 = RestApiQueries.GetAlbumList2RandomAsync(dbContext, user.UserId, null, albums.Count, CancellationToken.None).GetAwaiter().GetResult();

                        Assert.Equal(new HashSet<string>(albums.Select(a => "a" + a.AlbumId)),
                                     new HashSet<string>(result2.album.Select(a => a.id)));

                        try
                        {
                            Assert.NotEqual(result1.album.Select(a => a.id).ToArray(),
                                            result2.album.Select(a => a.id).ToArray());
                        }
                        catch
                        {
                            if (i > 0)
                                throw;
                        }
                    }
                }
            }
        }
    }
}
