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
using System.Linq;
using System.Threading;
using Xunit;
using static Hypersonic.Tests.Helpers;

namespace Hypersonic.Tests
{
    public static partial class RestApiQueriesTests
    {
        public static class DeletePlaylistAsyncTests
        {
            [Theory]
            [InlineData(false)]
            [InlineData(true)]
            public static void DeletePlaylistAsync_PlaylistDoesNotExist_ThrowsDataNotFoundError(bool canDeleteAllPublicPlaylists)
            {
                var dbConnection = OpenSqliteDatabase();

                var dbContextOptionsBuilder = new DbContextOptionsBuilder<MediaInfoContext>()
                    .DisableClientSideEvaluation()
                    .UseSqlite(dbConnection);

                using (var dbContext = new MediaInfoContext(dbContextOptionsBuilder.Options))
                {
                    var random = new RandomPopulator(dbContext);
                    var user = random.AddUser();
                    var playlist = random.AddPlaylist(user);
                    _ = dbContext.SaveChanges();

                    var ex = Assert.Throws<RestApiErrorException>(() => RestApiQueries.DeletePlaylistAsync(dbContext, user.UserId, canDeleteAllPublicPlaylists, playlist.PlaylistId + 1, CancellationToken.None).GetAwaiter().GetResult());

                    var expectedException = RestApiErrorException.DataNotFoundError();
                    Assert.Equal(expectedException.Message, ex.Message);
                    Assert.Equal(expectedException.Code, ex.Code);
                }
            }

            [Theory]
            [InlineData(false)]
            [InlineData(true)]
            public static void DeletePlaylistAsync_PrivatePlaylistOwnedByOtherUser_ThrowsDataNotFoundError(bool canDeleteAllPublicPlaylists)
            {
                var dbConnection = OpenSqliteDatabase();

                var dbContextOptionsBuilder = new DbContextOptionsBuilder<MediaInfoContext>()
                    .DisableClientSideEvaluation()
                    .UseSqlite(dbConnection);

                using (var dbContext = new MediaInfoContext(dbContextOptionsBuilder.Options))
                {
                    var random = new RandomPopulator(dbContext);
                    var user1 = random.AddUser();
                    var user2 = random.AddUser();
                    var playlist = random.AddPlaylist(user1, @public: false);
                    _ = dbContext.SaveChanges();

                    var ex = Assert.Throws<RestApiErrorException>(() => RestApiQueries.DeletePlaylistAsync(dbContext, user2.UserId, canDeleteAllPublicPlaylists, playlist.PlaylistId, CancellationToken.None).GetAwaiter().GetResult());

                    var expectedException = RestApiErrorException.DataNotFoundError();
                    Assert.Equal(expectedException.Message, ex.Message);
                    Assert.Equal(expectedException.Code, ex.Code);
                }
            }

            [Fact]
            public static void DeletePlaylistAsync_PublicPlaylistOwnedByOtherUserAndNotCanDeleteAllPublicPlaylists_ThrowsUserNotAuthorizedError()
            {
                var dbConnection = OpenSqliteDatabase();

                var dbContextOptionsBuilder = new DbContextOptionsBuilder<MediaInfoContext>()
                    .DisableClientSideEvaluation()
                    .UseSqlite(dbConnection);

                using (var dbContext = new MediaInfoContext(dbContextOptionsBuilder.Options))
                {
                    var random = new RandomPopulator(dbContext);
                    var user1 = random.AddUser();
                    var user2 = random.AddUser();
                    var playlist = random.AddPlaylist(user1, @public: true);
                    _ = dbContext.SaveChanges();

                    bool canDeleteAllPublicPlaylists = false;
                    var ex = Assert.Throws<RestApiErrorException>(() => RestApiQueries.DeletePlaylistAsync(dbContext, user2.UserId, canDeleteAllPublicPlaylists, playlist.PlaylistId, CancellationToken.None).GetAwaiter().GetResult());

                    var expectedException = RestApiErrorException.UserNotAuthorizedError();
                    Assert.Equal(expectedException.Message, ex.Message);
                    Assert.Equal(expectedException.Code, ex.Code);
                }
            }

            [Fact]
            public static void DeletePlaylistAsync_PublicPlaylistOwnedByOtherUserAndCanDeleteAllPublicPlaylists_DeletesPlaylist()
            {
                var dbConnection = OpenSqliteDatabase();

                var dbContextOptionsBuilder = new DbContextOptionsBuilder<MediaInfoContext>()
                    .DisableClientSideEvaluation()
                    .UseSqlite(dbConnection);

                using (var dbContext = new MediaInfoContext(dbContextOptionsBuilder.Options))
                {
                    var random = new RandomPopulator(dbContext);
                    var user1 = random.AddUser();
                    var user2 = random.AddUser();
                    var library = random.AddLibrary();
                    var directory = random.AddDirectory(library);
                    var file = random.AddFile(directory);
                    var artist = random.AddArtist();
                    var album = random.AddAlbum(artist);
                    var track = random.AddTrack(file, artist, album);
                    var playlist = random.AddPlaylist(user1, @public: true);
                    var playlistTrack = random.AddPlaylistTrack(playlist, track, 0);
                    _ = dbContext.SaveChanges();

                    bool canDeleteAllPublicPlaylists = true;
                    RestApiQueries.DeletePlaylistAsync(dbContext, user2.UserId, canDeleteAllPublicPlaylists, playlist.PlaylistId, CancellationToken.None).GetAwaiter().GetResult();
                    _ = dbContext.SaveChanges();

                    Assert.False(dbContext.Playlists.Any(p => p.PlaylistId == playlist.PlaylistId));
                }
            }

            [Theory]
            [InlineData(false)]
            [InlineData(true)]
            public static void DeletePlaylistAsync_PlaylistOwnedByUser_DeletesPlaylist(bool playlistIsPublic)
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
                    var file = random.AddFile(directory);
                    var artist = random.AddArtist();
                    var album = random.AddAlbum(artist);
                    var track = random.AddTrack(file, artist, album);
                    var playlist = random.AddPlaylist(user, playlistIsPublic);
                    var playlistTrack = random.AddPlaylistTrack(playlist, track, 0);
                    _ = dbContext.SaveChanges();

                    bool canDeleteAllPublicPlaylists = true;
                    RestApiQueries.DeletePlaylistAsync(dbContext, user.UserId, canDeleteAllPublicPlaylists, playlist.PlaylistId, CancellationToken.None).GetAwaiter().GetResult();
                    _ = dbContext.SaveChanges();

                    Assert.False(dbContext.Playlists.Any(p => p.PlaylistId == playlist.PlaylistId));
                }
            }
        }
    }
}
