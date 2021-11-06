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
        public static class GetArtistsAsyncTests
        {
            private static readonly StringComparer _stringComparer = CultureInfo.CurrentCulture.CompareInfo.GetStringComparer(CompareOptions.IgnoreCase);

            [Fact]
            public static void GetArtistsAsync_LibraryDoesNotExist_ThrowsDataNotFoundError()
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

                    var ex = Assert.Throws<RestApiErrorException>(() => RestApiQueries.GetArtistsAsync(dbContext, user.UserId, library.LibraryId + 1, CancellationToken.None).GetAwaiter().GetResult());

                    var expectedException = RestApiErrorException.DataNotFoundError();
                    Assert.Equal(expectedException.Message, ex.Message);
                    Assert.Equal(expectedException.Code, ex.Code);
                }
            }

            [Fact]
            public static void GetArtistsAsync_LibraryIsSpecified_ReturnsExpectedArtists()
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
                    _ = dbContext.SaveChanges();

                    var result = RestApiQueries.GetArtistsAsync(dbContext, user.UserId, library.LibraryId, CancellationToken.None).GetAwaiter().GetResult();

                    var resultArtist = Assert.Single(result.index.SelectMany(x => x.artist));
                    Assert.Equal("r" + artist.ArtistId, resultArtist.id);
                    Assert.Equal(artist.Name, resultArtist.name);
                    Assert.Equal(1, resultArtist.albumCount);
                }
            }

            [Fact]
            public static void GetArtistsAsync_ArtistIsPlaceholder_ReturnsPlaceholderName()
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

                    var result = RestApiQueries.GetArtistsAsync(dbContext, user.UserId, null, CancellationToken.None).GetAwaiter().GetResult();

                    var resultArtist = Assert.Single(result.index.SelectMany(i => i.artist));
                    Assert.Equal("[no artist]", resultArtist.name);
                }
            }

            [Theory]
            [InlineData(new int[] { 2 })]
            [InlineData(new int[] { 1, 3 })]
            public static void GetArtistsAsync_Always_ReturnsExpectedArtistDetails(int[] albumCounts)
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
                    var artists = new List<Artist>();
                    var albums = new List<Album>();
                    var tracks = new List<Track>();
                    for (int i = 0; i < albumCounts.Length; ++i)
                    {
                        var artist = random.AddArtist();
                        artists.Add(artist);
                        for (int j = 0; j < albumCounts[i]; ++j)
                        {
                            var directory = random.AddDirectory(library);
                            var album = random.AddAlbum(artist);
                            albums.Add(album);
                            var file = random.AddFile(directory);
                            var track = random.AddTrack(file, artist, album);
                            tracks.Add(track);
                        }
                    }
                    _ = dbContext.SaveChanges();

                    var result = RestApiQueries.GetArtistsAsync(dbContext, user.UserId, null, CancellationToken.None).GetAwaiter().GetResult();

                    Assert.Equal(string.Empty, result.ignoredArticles);
                    Assert.Equal(artists.Count, result.index.Sum(i => i.artist.Length));
                    Assert.Equal(new SortedSet<string>(artists.Select(r => "r" + r.ArtistId)),
                                 new SortedSet<string>(result.index.SelectMany(i => i.artist).Select(a => a.id)));
                    foreach (var resultArtist in result.index.SelectMany(i => i.artist))
                    {
                        var artist = artists.Single(r => "r" + r.ArtistId == resultArtist.id);
                        var artistAlbums = albums.Where(a => a.Artist == artist);

                        Assert.Equal(artist.Name, resultArtist.name);
                        Assert.Null(resultArtist.coverArt);
                        Assert.Equal(artistAlbums.Count(), resultArtist.albumCount);
                        Assert.False(resultArtist.starredSpecified);
                        Assert.Equal(default, resultArtist.starred);
                    }
                }
            }

            [Fact]
            public static void GetArtistsAsync_AlbumIsPlaceholder_AlbumIsCounted()
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

                    var result = RestApiQueries.GetArtistsAsync(dbContext, user.UserId, null, CancellationToken.None).GetAwaiter().GetResult();

                    var resultArtist = Assert.Single(result.index.SelectMany(i => i.artist));
                    Assert.Equal(1, resultArtist.albumCount);
                }
            }

            [Fact]
            public static void GetArtistsAsync_NoAccessibleTrack_ReturnsNoArtists()
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

                    var result = RestApiQueries.GetArtistsAsync(dbContext, user.UserId, null, CancellationToken.None).GetAwaiter().GetResult();

                    Assert.Empty(result.index.SelectMany(i => i.artist));
                }
            }

            [Fact]
            public static void GetArtistsAsync_ArtistHasNoAccessibleTrack_ArtistIsNotReturned()
            {
                var dbConnection = OpenSqliteDatabase();

                var dbContextOptionsBuilder = new DbContextOptionsBuilder<MediaInfoContext>()
                    .DisableClientSideEvaluation()
                    .UseSqlite(dbConnection);

                using (var dbContext = new MediaInfoContext(dbContextOptionsBuilder.Options))
                {
                    var random = new RandomPopulator(dbContext);
                    var user = random.AddUser();
                    var inaccessibleLibrary = random.AddLibrary(accessControlled: true);
                    var inaccessibleArtist = random.AddArtist();
                    var inaccessibleAlbum = random.AddAlbum(inaccessibleArtist);
                    var inaccessibleDirectory = random.AddDirectory(inaccessibleLibrary);
                    var inaccessibleFile = random.AddFile(inaccessibleDirectory);
                    var inaccessibleTrack = random.AddTrack(inaccessibleFile, inaccessibleArtist, inaccessibleAlbum);
                    var accessibleLibrary = random.AddLibrary(accessControlled: false);
                    var accessibleArtist = random.AddArtist();
                    var accessibleAlbum = random.AddAlbum(accessibleArtist);
                    var accessibleDirectory = random.AddDirectory(accessibleLibrary);
                    var accessibleFile = random.AddFile(accessibleDirectory);
                    var accessibleTrack = random.AddTrack(accessibleFile, accessibleArtist, accessibleAlbum);
                    _ = dbContext.SaveChanges();

                    var result = RestApiQueries.GetArtistsAsync(dbContext, user.UserId, null, CancellationToken.None).GetAwaiter().GetResult();

                    var resultArtist = Assert.Single(result.index.SelectMany(i => i.artist));
                    Assert.Equal("r" + accessibleArtist.ArtistId, resultArtist.id);
                }
            }

            [Fact]
            public static void GetArtistsAsync_AlbumHasNoAccessibleTrack_AlbumIsNotCounted()
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

                    var result = RestApiQueries.GetArtistsAsync(dbContext, user.UserId, null, CancellationToken.None).GetAwaiter().GetResult();

                    var resultArtist = Assert.Single(result.index.SelectMany(i => i.artist));
                    Assert.Equal(1, resultArtist.albumCount);
                }
            }

            [Fact]
            public static void GetArtistsAsync_ArtistHasAccessibleAccessControlledTrack_ArtistIsReturned()
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

                    var result = RestApiQueries.GetArtistsAsync(dbContext, user.UserId, null, CancellationToken.None).GetAwaiter().GetResult();

                    var resultArtist = Assert.Single(result.index.SelectMany(i => i.artist));
                    Assert.Equal("r" + artist.ArtistId, resultArtist.id);
                }
            }

            [Fact]
            public static void GetArtistsAsync_ArtistHasAccessibleNonAccessControlledTrack_ArtistIsReturned()
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

                    var result = RestApiQueries.GetArtistsAsync(dbContext, user.UserId, null, CancellationToken.None).GetAwaiter().GetResult();

                    var resultArtist = Assert.Single(result.index.SelectMany(i => i.artist));
                    Assert.Equal("r" + artist.ArtistId, resultArtist.id);
                }
            }

            [Fact]
            public static void GetArtistsAsync_ArtistHasNoStar_StarredHasNoValue()
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

                    var result = RestApiQueries.GetArtistsAsync(dbContext, user.UserId, null, CancellationToken.None).GetAwaiter().GetResult();

                    var resultArtist = Assert.Single(result.index.SelectMany(i => i.artist));
                    Assert.False(resultArtist.starredSpecified);
                    Assert.Equal(default, resultArtist.starred);
                }
            }

            [Fact]
            public static void GetArtistsAsync_ArtistHasStar_StarredHasExpectedValue()
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

                    var result = RestApiQueries.GetArtistsAsync(dbContext, user.UserId, null, CancellationToken.None).GetAwaiter().GetResult();

                    var resultArtist = Assert.Single(result.index.SelectMany(i => i.artist));
                    Assert.True(resultArtist.starredSpecified);
                    Assert.Equal(artistStar.Added, resultArtist.starred);
                }
            }

            [Theory]
            [InlineData("#", new[] { "", "0", "🕴" })]
            [InlineData("A", new[] { "A", "a" })]
            [InlineData("ǲ", new[] { "Ǳ", "ǲ", "ǳ" })]
            [InlineData("𓂀", new[] { "𓂀" })]
            [InlineData("\uD801\uDC00", new[] { "\uD801\uDC00",
                                                "\uD801\uDC28", })]
            [InlineData("\u1EAE", new[] { "\u1EAE", "\u0102\u0301", "\u0041\u0306\u0301",
                                          "\u1EAF", "\u0103\u0301", "\u0061\u0306\u0301", })]
            public static void GetArtistsAsync_Always_ArtistsAreInExpectedGroup(string name, string[] sortNames)
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
                    var artists = new List<Artist>();
                    foreach (string sortName in sortNames)
                    {
                        var artist = random.AddArtist();
                        artist.SortName = sortName;
                        artists.Add(artist);
                        var album = random.AddAlbum(artist);
                        var directory = random.AddDirectory(library);
                        var file = random.AddFile(directory);
                        var track = random.AddTrack(file, artist, album);
                    }
                    _ = dbContext.SaveChanges();

                    artists = artists
                        .OrderBy(r => r.SortName ?? r.Name, _stringComparer)
                        .ThenBy(r => r.ArtistId)
                        .ToList();

                    var result = RestApiQueries.GetArtistsAsync(dbContext, user.UserId, null, CancellationToken.None).GetAwaiter().GetResult();

                    var resultIndex = Assert.Single(result.index);
                    Assert.Equal(name, resultIndex.name, StringComparer.Ordinal);
                }
            }

            [Fact]
            public static void GetArtistsAsync_Always_ArtistsAreInExpectedOrder()
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
                    var artists = new List<Artist>();
                    for (int i = 0; i < 2; ++i)
                    {
                        foreach (string sortName in new[]
                            {
                                "A",
                                "a",
                                "C",
                                "🕴",
                                "𓂀",
                                null,
                                "B",
                                "b",
                            })
                        {
                            var artist = random.AddArtist();
                            artist.SortName = sortName;
                            if (artist.SortName != null)
                                artist.SortName = "Z" + artist.SortName;
                            else
                                artist.Name = "Z" + artist.Name;
                            artists.Add(artist);
                            var album = random.AddAlbum(artist);
                            var directory = random.AddDirectory(library);
                            var file = random.AddFile(directory);
                            var track = random.AddTrack(file, artist, album);
                        }
                    }
                    _ = dbContext.SaveChanges();

                    artists = artists
                        .OrderBy(r => r.SortName ?? r.Name, _stringComparer)
                        .ThenBy(r => r.ArtistId)
                        .ToList();

                    var result = RestApiQueries.GetArtistsAsync(dbContext, user.UserId, null, CancellationToken.None).GetAwaiter().GetResult();

                    Assert.Equal(artists.Select(r => "r" + r.ArtistId).ToArray(),
                                 result.index.SelectMany(i => i.artist).Select(a => a.id).ToArray());
                }
            }
        }
    }
}
