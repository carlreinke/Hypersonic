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
using System.Threading;
using Xunit;
using static Hypersonic.Tests.Helpers;

namespace Hypersonic.Tests
{
    public static partial class RestApiQueriesTests
    {
        public static class GetSongsByGenreAsyncTests
        {
            [Fact]
            public static void TestGetSongsByGenreAsync()
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
                    var genre = random.AddGenre();
                    var artist = random.AddArtist();
                    var album = random.AddAlbum(artist);
                    var track = random.AddTrack(trackFile, artist, album, genre: genre);
                    _ = dbContext.SaveChanges();

                    var result = RestApiQueries.GetSongsByGenreAsync(dbContext, user.UserId, null, track.Genre.Name, 0, 10, "opus", CancellationToken.None).GetAwaiter().GetResult();

                    Assert.NotNull(result);
                    // TODO
                }
            }
        }
    }
}
