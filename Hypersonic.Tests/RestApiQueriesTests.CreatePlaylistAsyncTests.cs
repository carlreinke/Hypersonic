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
        public static class CreatePlaylistAsyncTests
        {
            [Fact]
            public static void CreatePlaylistAsync_Always_CreatesPlaylist()
            {
                var dbConnection = OpenSqliteDatabase();

                var dbContextOptionsBuilder = new DbContextOptionsBuilder<MediaInfoContext>()
                    .DisableClientSideEvaluation()
                    .UseSqlite(dbConnection);

                using (var dbContext = new MediaInfoContext(dbContextOptionsBuilder.Options))
                {
                    var random = new RandomPopulator(dbContext);
                    var user = random.AddUser();
                    _ = dbContext.SaveChanges();

                    const string playlistName = "playlistName";

                    int playlistId = RestApiQueries.CreatePlaylistAsync(dbContext, user.UserId, playlistName, CancellationToken.None).GetAwaiter().GetResult();
                    var playlists = dbContext.Playlists.Local.Where(p => p.PlaylistId == playlistId).ToArray();
                    _ = dbContext.SaveChanges();

                    var playlist = Assert.Single(playlists);
                    Assert.Equal(user.UserId, playlist.UserId);
                    Assert.Equal(playlistName, playlist.Name);
                    Assert.Null(playlist.Description);
                    Assert.False(playlist.IsPublic);
                    Assert.InRange(playlist.Created, DateTime.UtcNow - TimeSpan.FromSeconds(30), DateTime.UtcNow);
                    Assert.InRange(playlist.Modified, playlist.Created, playlist.Created + TimeSpan.FromSeconds(1));
                    Assert.Empty(dbContext.PlaylistTracks.Where(pt => pt.PlaylistId == playlist.PlaylistId));
                }
            }
        }
    }
}
