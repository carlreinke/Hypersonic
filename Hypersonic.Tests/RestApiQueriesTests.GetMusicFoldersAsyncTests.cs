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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Xunit;
using static Hypersonic.Tests.Helpers;

namespace Hypersonic.Tests
{
    public static partial class RestApiQueriesTests
    {
        public static class GetMusicFoldersAsyncTests
        {
            [Theory]
            [InlineData(0)]
            [InlineData(1)]
            [InlineData(2)]
            public static void GetMusicFoldersAsync_Always_ReturnsExpectedLibraryDetails(int libraryCount)
            {
                var dbConnection = OpenSqliteDatabase();

                var dbContextOptionsBuilder = new DbContextOptionsBuilder<MediaInfoContext>()
                    .DisableClientSideEvaluation()
                    .UseSqlite(dbConnection);

                using (var dbContext = new MediaInfoContext(dbContextOptionsBuilder.Options))
                {
                    var random = new RandomPopulator(dbContext);
                    var user = random.AddUser();
                    var libraries = new List<Library>();
                    for (int i = 0; i < libraryCount; ++i)
                    {
                        var library = random.AddLibrary();
                        libraries.Add(library);
                    }
                    _ = dbContext.SaveChanges();

                    var result = RestApiQueries.GetMusicFoldersAsync(dbContext, user.UserId, CancellationToken.None).GetAwaiter().GetResult();

                    Assert.Equal(libraries.Count, result.musicFolder.Length);
                    Assert.Equal(new SortedSet<int>(libraries.Select(l => l.LibraryId)),
                                 new SortedSet<int>(result.musicFolder.Select(l => l.id)));
                    foreach (var resultMusicFolder in result.musicFolder)
                    {
                        var library = libraries.Single(l => l.LibraryId == resultMusicFolder.id);

                        Assert.Equal(library.Name, resultMusicFolder.name);
                    }
                }
            }

            [Fact]
            public static void GetMusicFoldersAsync_LibraryIsInaccessible_LibraryIsNotReturned()
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
                    var accessibleLibrary = random.AddLibrary(accessControlled: false);
                    _ = dbContext.SaveChanges();

                    var result = RestApiQueries.GetMusicFoldersAsync(dbContext, user.UserId, CancellationToken.None).GetAwaiter().GetResult();

                    var resultMusicFolder = Assert.Single(result.musicFolder);
                    Assert.Equal(accessibleLibrary.LibraryId, resultMusicFolder.id);
                }
            }

            [Fact]
            public static void GetMusicFoldersAsync_LibraryIsAccessibleAccessControlled_LibraryIsReturned()
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
                    _ = dbContext.SaveChanges();

                    var result = RestApiQueries.GetMusicFoldersAsync(dbContext, user.UserId, CancellationToken.None).GetAwaiter().GetResult();

                    var resultMusicFolder = Assert.Single(result.musicFolder);
                    Assert.Equal(library.LibraryId, resultMusicFolder.id);
                }
            }

            [Fact]
            public static void GetMusicFoldersAsync_LibraryIsAccessibleNonAccessControlled_LibraryIsReturned()
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
                    _ = dbContext.SaveChanges();

                    var result = RestApiQueries.GetMusicFoldersAsync(dbContext, user.UserId, CancellationToken.None).GetAwaiter().GetResult();

                    var resultMusicFolder = Assert.Single(result.musicFolder);
                    Assert.Equal(library.LibraryId, resultMusicFolder.id);
                }
            }

            [Fact]
            public static void GetMusicFoldersAsync_Always_LibrariesAreInExpectedOrder()
            {
                var dbConnection = OpenSqliteDatabase();

                var dbContextOptionsBuilder = new DbContextOptionsBuilder<MediaInfoContext>()
                    .DisableClientSideEvaluation()
                    .UseSqlite(dbConnection);

                using (var dbContext = new MediaInfoContext(dbContextOptionsBuilder.Options))
                {
                    var random = new RandomPopulator(dbContext);
                    var libraries = new List<Library>();
                    var user = random.AddUser();
                    foreach (string genreName in new[]
                        {
                            "A",
                            "a",
                            "C",
                            "𝓏",
                            "𓂀",
                            "B",
                            "b",
                        })
                    {
                        var library = random.AddLibrary();
                        library.Name = genreName;
                        libraries.Add(library);
                    }
                    _ = dbContext.SaveChanges();

                    libraries = libraries
                        .OrderBy(g => g.Name, _stringComparer)
                        .ToList();

                    var result = RestApiQueries.GetMusicFoldersAsync(dbContext, user.UserId, CancellationToken.None).GetAwaiter().GetResult();

                    Assert.Equal(libraries.Select(g => g.Name).ToArray(),
                                 result.musicFolder.Select(g => g.name).ToArray());
                }
            }
        }
    }
}
