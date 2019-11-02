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
        public static class AddPlaylistSongsAsyncTests
        {
            // No need for playlist existence or ownership tests because this
            // method assumes they've already been checked.

            [Fact]
            public static void AddPlaylistSongsAsync_InvalidTrackId_ThrowsDataNotFoundError()
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
                    dbContext.SaveChanges();

                    int[] trackIds = new[] { track.TrackId, track.TrackId + 1 };
                    var ex = Assert.Throws<RestApiErrorException>(() => RestApiQueries.AddPlaylistSongsAsync(dbContext, user.UserId, playlist.PlaylistId, trackIds, CancellationToken.None).GetAwaiter().GetResult());

                    var expectedException = RestApiErrorException.DataNotFoundError();
                    Assert.Equal(expectedException.Message, ex.Message);
                    Assert.Equal(expectedException.Code, ex.Code);
                }
            }

            [Fact]
            public static void AddPlaylistSongsAsync_NonAccessibleTrack_ThrowsDataNotFoundError()
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
                    var playlist = random.AddPlaylist(user);
                    dbContext.SaveChanges();

                    int[] trackIds = new[] { track.TrackId };
                    var ex = Assert.Throws<RestApiErrorException>(() => RestApiQueries.AddPlaylistSongsAsync(dbContext, user.UserId, playlist.PlaylistId, trackIds, CancellationToken.None).GetAwaiter().GetResult());

                    var expectedException = RestApiErrorException.DataNotFoundError();
                    Assert.Equal(expectedException.Message, ex.Message);
                    Assert.Equal(expectedException.Code, ex.Code);
                }
            }

            [Fact]
            public static void AddPlaylistSongsAsync_AccessibleAccessControledTrack_AddsTracksToPlaylist()
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
                    var artist = random.AddArtist();
                    var album = random.AddAlbum(artist);
                    var directory = random.AddDirectory(library);
                    var file = random.AddFile(directory);
                    var track = random.AddTrack(file, artist, album);
                    var playlist = random.AddPlaylist(user);
                    dbContext.SaveChanges();

                    int[] trackIds = new[] { track.TrackId };
                    RestApiQueries.AddPlaylistSongsAsync(dbContext, user.UserId, playlist.PlaylistId, trackIds, CancellationToken.None).GetAwaiter().GetResult();
                    dbContext.SaveChanges();

                    var playlistTracks = dbContext.PlaylistTracks
                        .Where(pt => pt.PlaylistId == playlist.PlaylistId)
                        .OrderBy(pt => pt.Index);
                    Assert.Equal(new[] { track.TrackId }, playlistTracks.Select(pt => pt.TrackId).ToArray());
                    Assert.Equal(Enumerable.Range(0, 1).ToArray(), playlistTracks.Select(pt => pt.Index).ToArray());
                }
            }

            [Fact]
            public static void AddPlaylistSongsAsync_AccessibleNonAccessControledTrack_AddsTracksToPlaylist()
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
                    var artist = random.AddArtist();
                    var album = random.AddAlbum(artist);
                    var directory = random.AddDirectory(library);
                    var file = random.AddFile(directory);
                    var track = random.AddTrack(file, artist, album);
                    var playlist = random.AddPlaylist(user);
                    dbContext.SaveChanges();

                    int[] trackIds = new[] { track.TrackId };
                    RestApiQueries.AddPlaylistSongsAsync(dbContext, user.UserId, playlist.PlaylistId, trackIds, CancellationToken.None).GetAwaiter().GetResult();
                    dbContext.SaveChanges();

                    var playlistTracks = dbContext.PlaylistTracks
                        .Where(pt => pt.PlaylistId == playlist.PlaylistId)
                        .OrderBy(pt => pt.Index);
                    Assert.Equal(new[] { track.TrackId }, playlistTracks.Select(pt => pt.TrackId).ToArray());
                    Assert.Equal(Enumerable.Range(0, 1).ToArray(), playlistTracks.Select(pt => pt.Index).ToArray());
                }
            }

            [Theory]
            [InlineData(0, 0)]
            [InlineData(0, 1)]
            [InlineData(0, 2)]
            [InlineData(1, 0)]
            [InlineData(1, 1)]
            [InlineData(1, 2)]
            [InlineData(2, 0)]
            [InlineData(2, 1)]
            [InlineData(2, 2)]
            [InlineData(10, 10)]
            public static void AddPlaylistSongsAsync_VariousExistingAndAddedTrackCounts_AddsTracksToPlaylist(int tracksInPlaylistCount, int tracksToAddCount)
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
                    var playlist = random.AddPlaylist(user);
                    var tracks = new List<Track>();
                    for (int i = 0; i < tracksInPlaylistCount; ++i)
                    {
                        var track = random.AddTrack(file, artist, album);
                        var playlistTrack = random.AddPlaylistTrack(playlist, track, i);

                        tracks.Add(track);
                    }
                    var tracksToAdd = new List<Track>();
                    for (int i = 0; i < tracksToAddCount; ++i)
                    {
                        var track = random.AddTrack(file, artist, album);

                        tracksToAdd.Add(track);
                    }
                    dbContext.SaveChanges();

                    int[] trackIdsToAdd = tracksToAdd.Select(t => t.TrackId).ToArray();
                    RestApiQueries.AddPlaylistSongsAsync(dbContext, user.UserId, playlist.PlaylistId, trackIdsToAdd, CancellationToken.None).GetAwaiter().GetResult();
                    dbContext.SaveChanges();

                    int[] expectedTrackIds = tracks.Concat(tracksToAdd).Select(t => t.TrackId).ToArray();

                    var playlistTracks = dbContext.PlaylistTracks
                        .Where(pt => pt.PlaylistId == playlist.PlaylistId)
                        .OrderBy(pt => pt.Index);
                    Assert.Equal(expectedTrackIds, playlistTracks.Select(pt => pt.TrackId).ToArray());
                    Assert.Equal(Enumerable.Range(0, expectedTrackIds.Length).ToArray(), playlistTracks.Select(pt => pt.Index).ToArray());
                }
            }

            [Fact]
            public static void AddPlaylistSongsAsync_DuplicateTrack_AddsTracksToPlaylist()
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
                    dbContext.SaveChanges();

                    RestApiQueries.AddPlaylistSongsAsync(dbContext, user.UserId, playlist.PlaylistId, new[] { track.TrackId }, CancellationToken.None).GetAwaiter().GetResult();
                    dbContext.SaveChanges();

                    var playlistTracks = dbContext.PlaylistTracks
                        .Where(pt => pt.PlaylistId == playlist.PlaylistId)
                        .OrderBy(pt => pt.Index);
                    Assert.Equal(new[] { track.TrackId, track.TrackId }, playlistTracks.Select(pt => pt.TrackId).ToArray());
                    Assert.Equal(Enumerable.Range(0, 2).ToArray(), playlistTracks.Select(pt => pt.Index).ToArray());
                }
            }
        }
    }
}
