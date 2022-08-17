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
using System.Linq;
using System.Threading;
using Xunit;
using static Hypersonic.Tests.Helpers;

namespace Hypersonic.Tests
{
    public static partial class RestApiQueriesTests
    {
        public static class GetPlaylistAsyncTests
        {
            [Fact]
            public static void GetPlaylistAsync_PlaylistDoesNotExist_ThrowsDataNotFoundError()
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
                    var playlist = random.AddPlaylist(user);
                    var playlistTrack = random.AddPlaylistTrack(playlist, track, 0);
                    _ = dbContext.SaveChanges();

                    string transcodedSuffix = "mp3";
                    var ex = Assert.Throws<RestApiErrorException>(() => RestApiQueries.GetPlaylistAsync(dbContext, user.UserId, playlist.PlaylistId + 1, transcodedSuffix, CancellationToken.None).GetAwaiter().GetResult());

                    var expectedException = RestApiErrorException.DataNotFoundError();
                    Assert.Equal(expectedException.Message, ex.Message);
                    Assert.Equal(expectedException.Code, ex.Code);
                }
            }

            [Fact]
            public static void GetPlaylistAsync_Always_ReturnsExpectedPlaylistDetails()
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
                    var playlist = random.AddPlaylist(user);
                    var playlistTrack = random.AddPlaylistTrack(playlist, track, 0);
                    _ = dbContext.SaveChanges();

                    string transcodedSuffix = "mp3";
                    var result = RestApiQueries.GetPlaylistAsync(dbContext, user.UserId, playlist.PlaylistId, transcodedSuffix, CancellationToken.None).GetAwaiter().GetResult();

                    var tracks = new[] { track };

                    Assert.Equal("p" + playlist.PlaylistId, result.id);
                    Assert.Equal(playlist.Name, result.name);
                    Assert.Equal(playlist.Description, result.comment);
                    Assert.Equal(playlist.User.Name, result.owner);
                    Assert.Equal(playlist.IsPublic, result.@public);
                    Assert.True(result.publicSpecified);
                    Assert.Equal(tracks.Length, result.songCount);
                    Assert.Equal(Math.Round(tracks.Sum(p => p.Duration) ?? 0), result.duration);
                    Assert.Equal(playlist.Created, result.created);
                    Assert.Equal(playlist.Modified, result.changed);
                    Assert.Null(result.coverArt);
                }
            }

            // TODO: GetPlaylistAsync_PrivatePlaylistOwnedByOtherUser_ThrowsDataNotFoundError

            // TODO: GetPlaylistAsync_PublicPlaylistOwnedByOtherUser_PlaylistIsReturned

            // TODO: GetPlaylistAsync_PlaylistOwnedByThisUser_PlaylistIsReturned

            // TODO: GetPlaylistAsync_Track...

            // TODO: GetPlaylistAsync_Always_TracksAreInExpectedOrder

            [Fact]
            public static void TestGetPlaylistAsync()
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
                    var directory = random.AddDirectory(library);
                    var trackFile = random.AddFile(directory);
                    var artist = random.AddArtist();
                    var album = random.AddAlbum(artist);
                    var track = random.AddTrack(trackFile, artist, album);
                    var playlist = random.AddPlaylist(user);
                    var playlistTrack = random.AddPlaylistTrack(playlist, track, 0);
                    _ = dbContext.SaveChanges();

                    var result = RestApiQueries.GetPlaylistAsync(dbContext, user.UserId, playlist.PlaylistId, "opus", CancellationToken.None).GetAwaiter().GetResult();

                    Assert.NotNull(result);
                    // TODO
                }
            }
        }
    }
}
