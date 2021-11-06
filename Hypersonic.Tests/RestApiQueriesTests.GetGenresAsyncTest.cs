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
        private static readonly StringComparer _stringComparer = CultureInfo.CurrentCulture.CompareInfo.GetStringComparer(CompareOptions.IgnoreCase);

        public static class GetGenresAsyncTest
        {
            [Theory]
            [InlineData("")]
            [InlineData("A")]
            [InlineData("A,")]
            [InlineData("A,A")]
            [InlineData("A,B")]
            [InlineData("A,A/B")]
            [InlineData(";")]
            [InlineData("A;")]
            [InlineData("A;A")]
            [InlineData("A;B")]
            [InlineData("A;A/B")]
            public static void GetGenresAsync_Always_ReturnsExpectedGenreDetails(string albumsTracksGenreNames)
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
                    var genres = new List<Genre>();
                    var trackGenres = new List<TrackGenre>();
                    foreach (string albumTracksGenreNames in albumsTracksGenreNames.Split(";"))
                    {
                        var directory = random.AddDirectory(library);
                        var album = random.AddAlbum(artist);
                        foreach (string trackGenreNames in albumTracksGenreNames.Split(","))
                        {
                            var file = random.AddFile(directory);
                            var track = random.AddTrack(file, artist, album);
                            foreach (string genreName in trackGenreNames.Split("/"))
                            {
                                var genre = genres.SingleOrDefault(g => g.Name == genreName);
                                if (genre == null)
                                {
                                    genre = random.AddGenre();
                                    genre.Name = genreName;
                                    genres.Add(genre);
                                }
                                var trackGenre = random.AddTrackGenre(track, genre);
                                trackGenres.Add(trackGenre);
                            }
                        }
                    }
                    _ = dbContext.SaveChanges();

                    var result = RestApiQueries.GetGenresAsync(dbContext, user.UserId, CancellationToken.None).GetAwaiter().GetResult();

                    Assert.Equal(genres.Count, result.genre.Length);
                    Assert.Equal(new SortedSet<string>(genres.Select(g => g.Name)),
                                 new SortedSet<string>(result.genre.Select(g => g.Text.Single())));
                    foreach (var resultGenre in result.genre)
                    {
                        string genreName = resultGenre.Text.Single();
                        var genreTracks = trackGenres.Where(rg => rg.Genre.Name == genreName).Select(tg => tg.Track);
                        var genreAlbums = genreTracks.Select(t => t.Album).Distinct();

                        Assert.Equal(genreAlbums.Count(), resultGenre.albumCount);
                        Assert.Equal(genreTracks.Count(), resultGenre.songCount);
                    }
                }
            }

            [Fact]
            public static void GetGenresAsync_NoAccessibleTrack_ReturnsNoGenres()
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
                    var genre = random.AddGenre();
                    var artist = random.AddArtist();
                    var album = random.AddAlbum(artist);
                    var directory = random.AddDirectory(library);
                    var file = random.AddFile(directory);
                    var track = random.AddTrack(file, artist, album);
                    var trackGenre = random.AddTrackGenre(track, genre);
                    _ = dbContext.SaveChanges();

                    var result = RestApiQueries.GetGenresAsync(dbContext, user.UserId, CancellationToken.None).GetAwaiter().GetResult();

                    Assert.Empty(result.genre);
                }
            }

            [Fact]
            public static void GetGenresAsync_GenreHasNoAccessibleTrack_GenreIsNotReturned()
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
                    var inaccessibleGenre = random.AddGenre();
                    var inaccessibleArtist = random.AddArtist();
                    var inaccessibleAlbum = random.AddAlbum(inaccessibleArtist);
                    var inaccessibleDirectory = random.AddDirectory(inaccessibleLibrary);
                    var inaccessibleFile = random.AddFile(inaccessibleDirectory);
                    var inaccessibleTrack = random.AddTrack(inaccessibleFile, inaccessibleArtist, inaccessibleAlbum);
                    var inaccessibleTrackGenre = random.AddTrackGenre(inaccessibleTrack, inaccessibleGenre);
                    var accessibleLibrary = random.AddLibrary(accessControlled: false);
                    var accessibleGenre = random.AddGenre();
                    var accessibleArtist = random.AddArtist();
                    var accessibleAlbum = random.AddAlbum(accessibleArtist);
                    var accessibleDirectory = random.AddDirectory(accessibleLibrary);
                    var accessibleFile = random.AddFile(accessibleDirectory);
                    var accessibleTrack = random.AddTrack(accessibleFile, accessibleArtist, accessibleAlbum);
                    var accessibleTrackGenre = random.AddTrackGenre(accessibleTrack, accessibleGenre);
                    _ = dbContext.SaveChanges();

                    var result = RestApiQueries.GetGenresAsync(dbContext, user.UserId, CancellationToken.None).GetAwaiter().GetResult();

                    var resultGenre = Assert.Single(result.genre);
                    Assert.Equal(accessibleGenre.Name, resultGenre.Text.Single());
                }
            }

            [Fact]
            public static void GetGenresAsync_GenreHasInaccessibleTrack_TrackIsNotCounted()
            {
                var dbConnection = OpenSqliteDatabase();

                var dbContextOptionsBuilder = new DbContextOptionsBuilder<MediaInfoContext>()
                    .DisableClientSideEvaluation()
                    .UseSqlite(dbConnection);

                using (var dbContext = new MediaInfoContext(dbContextOptionsBuilder.Options))
                {
                    var random = new RandomPopulator(dbContext);
                    var user = random.AddUser();
                    var genre = random.AddGenre();
                    var artist = random.AddArtist();
                    var album = random.AddAlbum(artist);
                    var inaccessibleLibrary = random.AddLibrary(accessControlled: true);
                    var inaccessibleDirectory = random.AddDirectory(inaccessibleLibrary);
                    var inaccessibleFile = random.AddFile(inaccessibleDirectory);
                    var inaccessibleTrack = random.AddTrack(inaccessibleFile, artist, album);
                    var inaccessibleTrackGenre = random.AddTrackGenre(inaccessibleTrack, genre);
                    var accessibleLibrary = random.AddLibrary(accessControlled: false);
                    var accessibleDirectory = random.AddDirectory(accessibleLibrary);
                    var accessibleFile = random.AddFile(accessibleDirectory);
                    var accessibleTrack = random.AddTrack(accessibleFile, artist, album);
                    var accessibleTrackGenre = random.AddTrackGenre(accessibleTrack, genre);
                    _ = dbContext.SaveChanges();

                    var result = RestApiQueries.GetGenresAsync(dbContext, user.UserId, CancellationToken.None).GetAwaiter().GetResult();

                    var resultGenre = Assert.Single(result.genre);
                    Assert.Equal(1, resultGenre.albumCount);
                    Assert.Equal(1, resultGenre.songCount);
                }
            }

            [Fact]
            public static void GetGenresAsync_GenreHasAlbumWithNoAccessibleTrack_AlbumIsNotCounted()
            {
                var dbConnection = OpenSqliteDatabase();

                var dbContextOptionsBuilder = new DbContextOptionsBuilder<MediaInfoContext>()
                    .DisableClientSideEvaluation()
                    .UseSqlite(dbConnection);

                using (var dbContext = new MediaInfoContext(dbContextOptionsBuilder.Options))
                {
                    var random = new RandomPopulator(dbContext);
                    var user = random.AddUser();
                    var genre = random.AddGenre();
                    var inaccessibleLibrary = random.AddLibrary(accessControlled: true);
                    var inaccessibleArtist = random.AddArtist();
                    var inaccessibleAlbum = random.AddAlbum(inaccessibleArtist);
                    var inaccessibleDirectory = random.AddDirectory(inaccessibleLibrary);
                    var inaccessibleFile = random.AddFile(inaccessibleDirectory);
                    var inaccessibleTrack = random.AddTrack(inaccessibleFile, inaccessibleArtist, inaccessibleAlbum);
                    var inaccessibleTrackGenre = random.AddTrackGenre(inaccessibleTrack, genre);
                    var accessibleArtist = random.AddArtist();
                    var accessibleAlbum = random.AddAlbum(inaccessibleArtist);
                    var accessibleLibrary = random.AddLibrary(accessControlled: false);
                    var accessibleDirectory = random.AddDirectory(accessibleLibrary);
                    var accessibleFile = random.AddFile(accessibleDirectory);
                    var accessibleTrack = random.AddTrack(accessibleFile, accessibleArtist, accessibleAlbum);
                    var accessibleTrackGenre = random.AddTrackGenre(accessibleTrack, genre);
                    _ = dbContext.SaveChanges();

                    var result = RestApiQueries.GetGenresAsync(dbContext, user.UserId, CancellationToken.None).GetAwaiter().GetResult();

                    var resultGenre = Assert.Single(result.genre);
                    Assert.Equal(1, resultGenre.albumCount);
                    Assert.Equal(1, resultGenre.songCount);
                }
            }

            [Fact]
            public static void GetGenresAsync_GenreHasAcessibleAccessControlledTrack_GenreIsReturned()
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
                    var genre = random.AddGenre();
                    var artist = random.AddArtist();
                    var album = random.AddAlbum(artist);
                    var directory = random.AddDirectory(library);
                    var file = random.AddFile(directory);
                    var track = random.AddTrack(file, artist, album);
                    var trackGenre = random.AddTrackGenre(track, genre);
                    _ = dbContext.SaveChanges();

                    var result = RestApiQueries.GetGenresAsync(dbContext, user.UserId, CancellationToken.None).GetAwaiter().GetResult();

                    var resultGenre = Assert.Single(result.genre);
                    Assert.Equal(genre.Name, resultGenre.Text.Single());
                }
            }

            [Fact]
            public static void GetGenresAsync_GenreHasAcessibleNonAccessControlledTrack_GenreIsReturned()
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
                    var genre = random.AddGenre();
                    var artist = random.AddArtist();
                    var album = random.AddAlbum(artist);
                    var directory = random.AddDirectory(library);
                    var file = random.AddFile(directory);
                    var track = random.AddTrack(file, artist, album);
                    var trackGenre = random.AddTrackGenre(track, genre);
                    _ = dbContext.SaveChanges();

                    var result = RestApiQueries.GetGenresAsync(dbContext, user.UserId, CancellationToken.None).GetAwaiter().GetResult();

                    var resultGenre = Assert.Single(result.genre);
                    Assert.Equal(genre.Name, resultGenre.Text.Single());
                }
            }

            [Fact]
            public static void GetGenresAsync_Always_GenresAreInExpectedOrder()
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
                    var genres = new List<Genre>();
                    var artist = random.AddArtist();
                    var album = random.AddAlbum();
                    var directory = random.AddDirectory(library);
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
                        var genre = random.AddGenre();
                        genre.Name = genreName;
                        genres.Add(genre);
                        var file = random.AddFile(directory);
                        var track = random.AddTrack(file, artist, album);
                        var trackGenre = random.AddTrackGenre(track, genre);
                    }
                    _ = dbContext.SaveChanges();

                    genres = genres
                        .OrderBy(g => g.Name, _stringComparer)
                        .ToList();

                    var result = RestApiQueries.GetGenresAsync(dbContext, user.UserId, CancellationToken.None).GetAwaiter().GetResult();

                    Assert.Equal(genres.Select(g => g.Name).ToArray(),
                                 result.genre.Select(g => g.Text.Single()).ToArray());
                }
            }
        }
    }
}
