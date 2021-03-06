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
using System.Linq;
using System.Threading;
using Xunit;
using static Hypersonic.Tests.Helpers;

namespace Hypersonic.Tests
{
    public static partial class RestApiQueriesTests
    {
        public static class CreateUserAsyncTests
        {
            [Fact]
            public static void CreateUserAsync_UsernameAlreadyExists_ThrowsGenericError()
            {
                var dbConnection = OpenSqliteDatabase();

                var dbContextOptionsBuilder = new DbContextOptionsBuilder<MediaInfoContext>()
                    .DisableClientSideEvaluation()
                    .UseSqlite(dbConnection);

                using (var dbContext = new MediaInfoContext(dbContextOptionsBuilder.Options))
                {
                    var random = new RandomPopulator(dbContext);
                    var user = random.AddUser();
                    dbContext.SaveChanges();

                    var ex = Assert.Throws<RestApiErrorException>(() =>
                    {
                        return RestApiQueries.CreateUserAsync(dbContext, user.Name, "password", false, false, false, CancellationToken.None).GetAwaiter().GetResult();
                    });

                    var expectedException = RestApiErrorException.GenericError("User already exists.");
                    Assert.Equal(expectedException.Message, ex.Message);
                    Assert.Equal(expectedException.Code, ex.Code);
                }
            }

            [Theory]
            [InlineData(false, false, false)]
            [InlineData(false, false, true)]
            [InlineData(false, true, false)]
            [InlineData(false, true, true)]
            [InlineData(true, false, false)]
            [InlineData(true, false, true)]
            [InlineData(true, true, false)]
            [InlineData(true, true, true)]
            public static void CreateUserAsync_UsernameNotAlreadyExists_CreatesUser(bool isAdmin, bool isGuest, bool canJukebox)
            {
                var dbConnection = OpenSqliteDatabase();

                var dbContextOptionsBuilder = new DbContextOptionsBuilder<MediaInfoContext>()
                    .DisableClientSideEvaluation()
                    .UseSqlite(dbConnection);

                using (var dbContext = new MediaInfoContext(dbContextOptionsBuilder.Options))
                {
                    const string username = "username";
                    const string password = "password";

                    int userId = RestApiQueries.CreateUserAsync(dbContext, username, password, isAdmin, isGuest, canJukebox, CancellationToken.None).GetAwaiter().GetResult();
                    var users = dbContext.Users.Local.Where(u => u.UserId == userId).ToArray();
                    dbContext.SaveChanges();

                    var user = Assert.Single(users);
                    Assert.Equal(username, user.Name);
                    Assert.Equal(password, user.Password);
                    Assert.Equal(128_000, user.MaxBitRate);
                    Assert.Equal(isAdmin, user.IsAdmin);
                    Assert.Equal(isGuest, user.IsGuest);
                    Assert.Equal(canJukebox, user.CanJukebox);
                    Assert.Empty(dbContext.LibraryUsers.Where(lu => lu.UserId == user.UserId));
                    Assert.Empty(dbContext.Playlists.Where(p => p.UserId == user.UserId));
                }
            }
        }
    }
}
