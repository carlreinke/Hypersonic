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
        public static class StarArtistAsyncTests
        {
            [Fact]
            public static void TestStarArtistAsync()
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
                    dbContext.SaveChanges();

                    for (int i = 0; i < 2; ++i)
                    {
                        RestApiQueries.StarArtistAsync(dbContext, user.UserId, artist.ArtistId, CancellationToken.None).GetAwaiter().GetResult();
                        dbContext.SaveChanges();

                        Assert.True(dbContext.ArtistStars.Any(s => s.ArtistId == artist.ArtistId && s.UserId == user.UserId));
                    }
                }
            }
        }
    }
}
