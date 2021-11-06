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
        public static class GetCoverArtStreamInfoAsyncTests
        {
            [Fact]
            public static void GetCoverArtStreamInfoAsync_PictureDoesNotExist_ThrowsDataNotFoundError()
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
                    var picture = random.AddPicture(file);
                    _ = dbContext.SaveChanges();

                    var ex = Assert.Throws<RestApiErrorException>(() => RestApiQueries.GetCoverArtStreamInfoAsync(dbContext, user.UserId, picture.StreamHash + 1, CancellationToken.None).GetAwaiter().GetResult());
                    Assert.Equal(RestApiErrorException.DataNotFoundError().Message, ex.Message);
                }
            }

            [Fact]
            public static void GetCoverArtStreamInfoAsync_Always_ReturnsExpectedPictureDetails()
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
                    var picture = random.AddPicture(file);
                    _ = dbContext.SaveChanges();

                    var result = RestApiQueries.GetCoverArtStreamInfoAsync(dbContext, user.UserId, picture.StreamHash, CancellationToken.None).GetAwaiter().GetResult();

                    Assert.Equal(picture.File.Directory.Path, result.DirectoryPath);
                    Assert.Equal(picture.File.Name, result.FileName);
                    Assert.Equal(picture.StreamIndex, result.StreamIndex);
                }
            }

            [Fact]
            public static void GetCoverArtStreamInfoAsync_PictureHasInaccessibleFile_ThrowsDataNotFoundError()
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
                    var directory = random.AddDirectory(library);
                    var file = random.AddFile(directory);
                    var picture = random.AddPicture(file);
                    _ = dbContext.SaveChanges();

                    var ex = Assert.Throws<RestApiErrorException>(() => RestApiQueries.GetCoverArtStreamInfoAsync(dbContext, user.UserId, picture.StreamHash, CancellationToken.None).GetAwaiter().GetResult());
                    Assert.Equal(RestApiErrorException.DataNotFoundError().Message, ex.Message);
                }
            }

            [Fact]
            public static void GetCoverArtStreamInfoAsync_PictureHasAccessibleAccessControlledFile_ReturnsPictureDetails()
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
                    var directory = random.AddDirectory(library);
                    var file = random.AddFile(directory);
                    var picture = random.AddPicture(file);
                    _ = dbContext.SaveChanges();

                    var result = RestApiQueries.GetCoverArtStreamInfoAsync(dbContext, user.UserId, picture.StreamHash, CancellationToken.None).GetAwaiter().GetResult();

                    Assert.Equal(picture.File.Directory.Path, result.DirectoryPath);
                    Assert.Equal(picture.File.Name, result.FileName);
                    Assert.Equal(picture.StreamIndex, result.StreamIndex);
                }
            }

            [Fact]
            public static void GetCoverArtStreamInfoAsync_PictureHasAccessibleNonAccessControlledFile_ReturnsPictureDetails()
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
                    var directory = random.AddDirectory(library);
                    var file = random.AddFile(directory);
                    var picture = random.AddPicture(file);
                    _ = dbContext.SaveChanges();

                    var result = RestApiQueries.GetCoverArtStreamInfoAsync(dbContext, user.UserId, picture.StreamHash, CancellationToken.None).GetAwaiter().GetResult();

                    Assert.Equal(picture.File.Directory.Path, result.DirectoryPath);
                    Assert.Equal(picture.File.Name, result.FileName);
                    Assert.Equal(picture.StreamIndex, result.StreamIndex);
                }
            }

            [Fact]
            public static void GetCoverArtStreamInfoAsync_PictureHasMultipleAccessibleFiles_ReturnsPictureDetails()
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
                    var picture = random.AddPicture(file);
                    var otherFile = random.AddFile(directory);
                    var otherPicture = random.AddPicture(otherFile);
                    otherPicture.StreamHash = picture.StreamHash;
                    _ = dbContext.SaveChanges();

                    var result = RestApiQueries.GetCoverArtStreamInfoAsync(dbContext, user.UserId, picture.StreamHash, CancellationToken.None).GetAwaiter().GetResult();

                    Assert.Equal(picture.File.Directory.Path, result.DirectoryPath);
                    Assert.Contains(new[] { picture.File.Name, otherPicture.File.Name }, x => x == result.FileName);
                    Assert.Equal(picture.StreamIndex, result.StreamIndex);
                }
            }

            [Fact]
            public static void GetCoverArtStreamInfoAsync_PictureHasAccessibleAndInaccessibleFiles_ReturnsPictureDetails()
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
                    var inaccessibleDirectory = random.AddDirectory(inaccessibleLibrary);
                    var inaccessibleFile = random.AddFile(inaccessibleDirectory);
                    var inaccessiblePicture = random.AddPicture(inaccessibleFile);
                    var accessibleLibrary = random.AddLibrary(accessControlled: false);
                    var accessibleDirectory = random.AddDirectory(accessibleLibrary);
                    var accessibleFile = random.AddFile(accessibleDirectory);
                    var accessiblePicture = random.AddPicture(accessibleFile);
                    _ = dbContext.SaveChanges();

                    var result = RestApiQueries.GetCoverArtStreamInfoAsync(dbContext, user.UserId, accessiblePicture.StreamHash, CancellationToken.None).GetAwaiter().GetResult();

                    Assert.Equal(accessiblePicture.File.Directory.Path, result.DirectoryPath);
                    Assert.Equal(accessiblePicture.File.Name, result.FileName);
                    Assert.Equal(accessiblePicture.StreamIndex, result.StreamIndex);
                }
            }
        }
    }
}
