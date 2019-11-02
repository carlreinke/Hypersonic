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
using System.IO;
using System.Linq;
using System.Threading;
using Xunit;
using static Hypersonic.Tests.Helpers;

namespace Hypersonic.Tests
{
    public static partial class RestApiQueriesTests
    {
        public static class GetAlbumAsyncTests
        {
            private static readonly StringComparer _stringComparer = CultureInfo.CurrentCulture.CompareInfo.GetStringComparer(CompareOptions.IgnoreCase);

            [Fact]
            public static void GetAlbumAsync_AlbumIdDoesNotExist_ThrowsDataNotFoundError()
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
                    dbContext.SaveChanges();

                    string transcodedSuffix = "mp3";
                    var ex = Assert.Throws<RestApiErrorException>(() => RestApiQueries.GetAlbumAsync(dbContext, user.UserId, album.AlbumId + 1, transcodedSuffix, CancellationToken.None).GetAwaiter().GetResult());

                    var expectedException = RestApiErrorException.DataNotFoundError();
                    Assert.Equal(expectedException.Message, ex.Message);
                    Assert.Equal(expectedException.Code, ex.Code);
                }
            }

            [Fact]
            public static void GetAlbumAsync_NoAccessibleTrack_ThrowsDataNotFoundError()
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

                    string transcodedSuffix = "mp3";
                    var ex = Assert.Throws<RestApiErrorException>(() => RestApiQueries.GetAlbumAsync(dbContext, user.UserId, album.AlbumId, transcodedSuffix, CancellationToken.None).GetAwaiter().GetResult());

                    var expectedException = RestApiErrorException.DataNotFoundError();
                    Assert.Equal(expectedException.Message, ex.Message);
                    Assert.Equal(expectedException.Code, ex.Code);
                }
            }

            [Fact]
            public static void GetAlbumAsync_AccessibleTrack_ReturnsExpectedArtistDetails()
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

                    string transcodedSuffix = "mp3";
                    var result = RestApiQueries.GetAlbumAsync(dbContext, user.UserId, album.AlbumId, transcodedSuffix, CancellationToken.None).GetAwaiter().GetResult();

                    var tracks = new[] { track };

                    Assert.Equal("a" + album.AlbumId, result.id);
                    Assert.Equal(album.Title, result.name);
                    Assert.Equal(album.Artist.Name, result.artist);
                    Assert.Equal("r" + artist.ArtistId, result.artistId);
                    Assert.Equal(album.CoverPictureId?.ToString("X8"), result.coverArt);
                    Assert.Equal(tracks.Count(), result.songCount);
                    Assert.Equal(Math.Round(tracks.Sum(t => t.Duration) ?? 0), result.duration);
                    Assert.False(result.playCountSpecified);
                    Assert.Equal(default, result.playCount);
                    Assert.Equal(album.Added, result.created);
                    Assert.False(result.starredSpecified);
                    Assert.Equal(default, result.starred);
                    Assert.Equal(album.Date.HasValue, result.yearSpecified);
                    Assert.Equal(album.Date / 1_00_00 ?? 0, result.year);
                    Assert.Equal(album.Genre?.Name, result.genre);
                }
            }

            [Theory]
            [InlineData(1)]
            [InlineData(2)]
            public static void GetAlbumAsync_AccessibleTrack_ReturnsExpectedTrackDetails(int trackCount)
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
                    var tracks = new List<Track>();
                    for (int i = 0; i < trackCount; ++i)
                    {
                        var file = random.AddFile(directory);
                        var track = random.AddTrack(file, artist, album);
                        tracks.Add(track);
                    }
                    dbContext.SaveChanges();

                    string transcodedSuffix = "mp3";
                    string transcodedContentType = "audio/mpeg";
                    var result = RestApiQueries.GetAlbumAsync(dbContext, user.UserId, album.AlbumId, transcodedSuffix, CancellationToken.None).GetAwaiter().GetResult();

                    Assert.Equal(tracks.Count, result.songCount);
                    Assert.Equal(tracks.Count, result.song.Length);
                    Assert.Equal(new SortedSet<string>(tracks.Select(t => "t" + t.TrackId)),
                                 new SortedSet<string>(result.song.Select(t => t.id)));
                    foreach (var resultTrack in result.song)
                    {
                        var track = tracks.Single(t => "t" + t.TrackId == resultTrack.id);

                        Assert.Null(resultTrack.parent);
                        Assert.False(resultTrack.isDir);
                        Assert.Equal(track.Title, resultTrack.title);
                        Assert.Equal(track.Album.Title, resultTrack.album);
                        Assert.Equal(track.Artist.Name, resultTrack.artist);
                        Assert.Equal(track.TrackNumber.HasValue, resultTrack.trackSpecified);
                        Assert.Equal(track.TrackNumber ?? 0, resultTrack.track);
                        Assert.Equal(track.Date.HasValue, resultTrack.yearSpecified);
                        Assert.Equal(track.Date / 1_00_00 ?? 0, resultTrack.year);
                        Assert.Equal(track.Genre?.Name, resultTrack.genre);
                        Assert.Equal(track.CoverPictureId?.ToString("X8"), resultTrack.coverArt);
                        Assert.True(resultTrack.sizeSpecified);
                        Assert.Equal(track.File.Size, resultTrack.size);
                        Assert.Null(resultTrack.contentType);
                        Assert.Equal(Path.GetExtension(track.File.Name).TrimStart('.'), resultTrack.suffix);
                        Assert.Equal(transcodedContentType, resultTrack.transcodedContentType);
                        Assert.Equal(transcodedSuffix, resultTrack.transcodedSuffix);
                        Assert.Equal(track.Duration.HasValue, resultTrack.durationSpecified);
                        Assert.Equal(Math.Round(track.Duration ?? 0), resultTrack.duration);
                        Assert.Equal(track.BitRate.HasValue, resultTrack.bitRateSpecified);
                        Assert.Equal(Math.Round(track.BitRate / 1000.0 ?? 0), resultTrack.bitRate);
                        Assert.Null(resultTrack.path);
                        Assert.False(resultTrack.isVideoSpecified);
                        Assert.Equal(default, resultTrack.isVideo);
                        Assert.False(resultTrack.userRatingSpecified);
                        Assert.Equal(default, resultTrack.userRating);
                        Assert.False(resultTrack.averageRatingSpecified);
                        Assert.Equal(default, resultTrack.averageRating);
                        Assert.False(resultTrack.playCountSpecified);
                        Assert.Equal(default, resultTrack.playCount);
                        Assert.Equal(track.DiscNumber.HasValue, resultTrack.discNumberSpecified);
                        Assert.Equal(track.DiscNumber ?? 0, resultTrack.discNumber);
                        Assert.True(resultTrack.createdSpecified);
                        Assert.Equal(track.Added, resultTrack.created);
                        Assert.False(resultTrack.starredSpecified);
                        Assert.Equal(default, resultTrack.starred);
                        Assert.Equal("a" + track.AlbumId, resultTrack.albumId);
                        Assert.Equal("r" + track.ArtistId, resultTrack.artistId);
                        Assert.True(resultTrack.typeSpecified);
                        Assert.Equal(Subsonic.MediaType.music, resultTrack.type);
                        Assert.False(resultTrack.bookmarkPositionSpecified);
                        Assert.Equal(default, resultTrack.bookmarkPosition);
                        Assert.False(resultTrack.originalWidthSpecified);
                        Assert.Equal(default, resultTrack.originalWidth);
                        Assert.False(resultTrack.originalHeightSpecified);
                        Assert.Equal(default, resultTrack.originalHeight);
                    }
                }
            }

            [Fact]
            public static void GetAlbumAsync_TrackHasNoTrackNumber_TrackNumberHasNoValue()
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
                    track.TrackNumber = null;
                    dbContext.SaveChanges();

                    string transcodedSuffix = "mp3";
                    var result = RestApiQueries.GetAlbumAsync(dbContext, user.UserId, album.AlbumId, transcodedSuffix, CancellationToken.None).GetAwaiter().GetResult();

                    var resultTrack = Assert.Single(result.song);
                    Assert.False(resultTrack.trackSpecified);
                    Assert.Equal(default, resultTrack.track);
                }
            }

            [Fact]
            public static void GetAlbumAsync_TrackHasTrackNumber_TrackNumberHasExpectedValue()
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

                    string transcodedSuffix = "mp3";
                    var result = RestApiQueries.GetAlbumAsync(dbContext, user.UserId, album.AlbumId, transcodedSuffix, CancellationToken.None).GetAwaiter().GetResult();

                    var resultTrack = Assert.Single(result.song);
                    Assert.True(resultTrack.trackSpecified);
                    Assert.Equal(track.TrackNumber, resultTrack.track);
                }
            }

            [Fact]
            public static void GetAlbumAsync_TrackHasNoDate_YearHasNoValue()
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
                    track.Date = null;
                    dbContext.SaveChanges();

                    string transcodedSuffix = "mp3";
                    var result = RestApiQueries.GetAlbumAsync(dbContext, user.UserId, album.AlbumId, transcodedSuffix, CancellationToken.None).GetAwaiter().GetResult();

                    var resultTrack = Assert.Single(result.song);
                    Assert.False(resultTrack.yearSpecified);
                    Assert.Equal(default, resultTrack.year);
                }
            }

            [Fact]
            public static void GetAlbumAsync_TrackHasDate_YearHasExpectedValue()
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

                    string transcodedSuffix = "mp3";
                    var result = RestApiQueries.GetAlbumAsync(dbContext, user.UserId, album.AlbumId, transcodedSuffix, CancellationToken.None).GetAwaiter().GetResult();

                    var resultTrack = Assert.Single(result.song);
                    Assert.True(resultTrack.yearSpecified);
                    Assert.Equal(track.Date / 1_00_00, resultTrack.year);
                }
            }

            [Fact]
            public static void GetAlbumAsync_TrackHasNoGenre_GenreHasNoValue()
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
                    var track = random.AddTrack(file, artist, album, genre: null);
                    var trackGenre = random.AddTrackGenre(track, genre);
                    track.Genre = null;
                    dbContext.SaveChanges();

                    string transcodedSuffix = "mp3";
                    var result = RestApiQueries.GetAlbumAsync(dbContext, user.UserId, album.AlbumId, transcodedSuffix, CancellationToken.None).GetAwaiter().GetResult();

                    var resultTrack = Assert.Single(result.song);
                    Assert.Null(resultTrack.genre);
                }
            }

            [Fact]
            public static void GetAlbumAsync_TrackHasGenre_GenreHasExpectedValue()
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
                    var otherGenre = random.AddGenre();
                    var album = random.AddAlbum(artist, genre: otherGenre);
                    var directory = random.AddDirectory(library);
                    var file = random.AddFile(directory);
                    var track = random.AddTrack(file, artist, album, genre: genre);
                    var trackGenre = random.AddTrackGenre(track, genre);
                    var trackOtherGenre = random.AddTrackGenre(track, otherGenre);
                    dbContext.SaveChanges();

                    string transcodedSuffix = "mp3";
                    var result = RestApiQueries.GetAlbumAsync(dbContext, user.UserId, album.AlbumId, transcodedSuffix, CancellationToken.None).GetAwaiter().GetResult();

                    var resultTrack = Assert.Single(result.song);
                    Assert.Equal(genre.Name, resultTrack.genre);
                }
            }

            [Fact]
            public static void GetAlbumAsync_TrackHasNoDuration_DurationHasNoValue()
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
                    track.Duration = null;
                    dbContext.SaveChanges();

                    string transcodedSuffix = "mp3";
                    var result = RestApiQueries.GetAlbumAsync(dbContext, user.UserId, album.AlbumId, transcodedSuffix, CancellationToken.None).GetAwaiter().GetResult();

                    var resultTrack = Assert.Single(result.song);
                    Assert.False(resultTrack.durationSpecified);
                    Assert.Equal(default, resultTrack.duration);
                }
            }

            [Fact]
            public static void GetAlbumAsync_TrackHasDuration_DurationHasExpectedValue()
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

                    string transcodedSuffix = "mp3";
                    var result = RestApiQueries.GetAlbumAsync(dbContext, user.UserId, album.AlbumId, transcodedSuffix, CancellationToken.None).GetAwaiter().GetResult();

                    var resultTrack = Assert.Single(result.song);
                    Assert.True(resultTrack.durationSpecified);
                    Assert.Equal(Math.Round(track.Duration.Value), resultTrack.duration);
                }
            }

            [Fact]
            public static void GetAlbumAsync_TrackHasNoBitRate_BitRateHasNoValue()
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
                    track.BitRate = null;
                    dbContext.SaveChanges();

                    string transcodedSuffix = "mp3";
                    var result = RestApiQueries.GetAlbumAsync(dbContext, user.UserId, album.AlbumId, transcodedSuffix, CancellationToken.None).GetAwaiter().GetResult();

                    var resultTrack = Assert.Single(result.song);
                    Assert.False(resultTrack.bitRateSpecified);
                    Assert.Equal(default, resultTrack.bitRate);
                }
            }

            [Fact]
            public static void GetAlbumAsync_TrackHasBitRate_BitRateHasExpectedValue()
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

                    string transcodedSuffix = "mp3";
                    var result = RestApiQueries.GetAlbumAsync(dbContext, user.UserId, album.AlbumId, transcodedSuffix, CancellationToken.None).GetAwaiter().GetResult();

                    var resultTrack = Assert.Single(result.song);
                    Assert.True(resultTrack.bitRateSpecified);
                    Assert.Equal(Math.Round(track.BitRate.Value / 1000.0), resultTrack.bitRate);
                }
            }

            [Fact]
            public static void GetAlbumAsync_TrackHasNoDiscNumber_DiscNumberHasNoValue()
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
                    track.DiscNumber = null;
                    dbContext.SaveChanges();

                    string transcodedSuffix = "mp3";
                    var result = RestApiQueries.GetAlbumAsync(dbContext, user.UserId, album.AlbumId, transcodedSuffix, CancellationToken.None).GetAwaiter().GetResult();

                    var resultTrack = Assert.Single(result.song);
                    Assert.False(resultTrack.discNumberSpecified);
                    Assert.Equal(default, resultTrack.discNumber);
                }
            }

            [Fact]
            public static void GetAlbumAsync_TrackHasDiscNumber_DiscNumberHasExpectedValue()
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

                    string transcodedSuffix = "mp3";
                    var result = RestApiQueries.GetAlbumAsync(dbContext, user.UserId, album.AlbumId, transcodedSuffix, CancellationToken.None).GetAwaiter().GetResult();

                    var resultTrack = Assert.Single(result.song);
                    Assert.True(resultTrack.discNumberSpecified);
                    Assert.Equal(track.DiscNumber, resultTrack.discNumber);
                }
            }

            [Fact]
            public static void GetAlbumAsync_TrackHasNoStar_StarredHasNoValue()
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

                    string transcodedSuffix = "mp3";
                    var result = RestApiQueries.GetAlbumAsync(dbContext, user.UserId, album.AlbumId, transcodedSuffix, CancellationToken.None).GetAwaiter().GetResult();

                    var resultTrack = Assert.Single(result.song);
                    Assert.False(resultTrack.starredSpecified);
                    Assert.Equal(default, resultTrack.starred);
                }
            }

            [Fact]
            public static void GetAlbumAsync_TrackHasStar_StarredHasExpectedValue()
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
                    var trackStar = random.AddTrackStar(track, user);
                    dbContext.SaveChanges();

                    string transcodedSuffix = "mp3";
                    var result = RestApiQueries.GetAlbumAsync(dbContext, user.UserId, album.AlbumId, transcodedSuffix, CancellationToken.None).GetAwaiter().GetResult();

                    var resultTrack = Assert.Single(result.song);
                    Assert.True(resultTrack.starredSpecified);
                    Assert.Equal(trackStar.Added, resultTrack.starred);
                }
            }

            [Fact]
            public static void GetAlbumAsync_Always_TracksAreInExpectedOrder()
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
                    var tracks = new List<Track>();
                    for (int i = 0; i < 2; ++i)
                    {
                        foreach (int? trackNumber in new int?[]
                            {
                                1,
                                3,
                                null,
                                2,
                            })
                        {
                            foreach (int? discNumber in new int?[]
                                {
                                    1,
                                    3,
                                    null,
                                    2,
                                })
                            {
                                foreach (string sortTitle in new[]
                                    {
                                        "A",
                                        "a",
                                        "C",
                                        null,
                                        "B",
                                        "b",
                                    })
                                {
                                    var directory = random.AddDirectory(library);
                                    var file = random.AddFile(directory);
                                    var track = random.AddTrack(file, artist, album);
                                    track.SortTitle = sortTitle;
                                    track.DiscNumber = discNumber;
                                    track.TrackNumber = trackNumber;
                                    tracks.Add(track);
                                }
                            }
                        }
                    }
                    dbContext.SaveChanges();

                    tracks = tracks
                        .OrderBy(t => t.DiscNumber ?? int.MaxValue)
                        .ThenBy(t => t.TrackNumber ?? int.MaxValue)
                        .ThenBy(t => t.SortTitle ?? t.Title, _stringComparer)
                        .ThenBy(t => t.TrackId)
                        .ToList();

                    string transcodedSuffix = "mp3";
                    var result = RestApiQueries.GetAlbumAsync(dbContext, user.UserId, album.AlbumId, transcodedSuffix, CancellationToken.None).GetAwaiter().GetResult();

                    Assert.Equal(tracks.Select(t => "t" + t.TrackId).ToArray(),
                                 result.song.Select(t => t.id).ToArray());
                }
            }
        }
    }
}
