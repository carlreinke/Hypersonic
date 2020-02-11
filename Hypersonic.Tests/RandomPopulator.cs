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
using System;
using IOPath = System.IO.Path;

namespace Hypersonic.Tests
{
    internal sealed class RandomPopulator
    {
        private readonly Random _random = new Random();

        private readonly MediaInfoContext _dbContext;

        internal RandomPopulator(MediaInfoContext dbContext)
        {
            if (dbContext == null)
                throw new ArgumentNullException(nameof(dbContext));

            _dbContext = dbContext;
        }

        internal User AddUser()
        {
            var user = new User
            {
                Name = RandomString(10),
                Password = RandomString(10),
                MaxBitRate = RandomInt32(1000, 320000 + 1),
                IsAdmin = false,
                IsGuest = false,
                CanJukebox = false,
            };
            _dbContext.Users.Add(user);
            return user;
        }

        internal Library AddLibrary(bool accessControlled = false)
        {
            var library = new Library
            {
                Name = RandomString(10),
                Path = RandomString(10),
                IsAccessControlled = accessControlled,
                ContentModified = RandomDateTime(),
            };
            _dbContext.Libraries.Add(library);
            return library;
        }

        internal LibraryUser AddLibraryUser(Library library, User user)
        {
            var libraryUser = new LibraryUser
            {
                Library = library,
                User = user,
            };
            _dbContext.LibraryUsers.Add(libraryUser);
            return libraryUser;
        }

        internal Directory AddDirectory(Library library)
        {
            var directory = new Directory
            {
                Library = library,
                ParentDirectory = null,
                Path = string.Empty,
                Added = RandomDateTime(),
            };
            _dbContext.Directories.Add(directory);
            return directory;
        }

        internal Directory AddDirectory(Directory directory)
        {
            var subdirectory = new Directory
            {
                Library = directory.Library,
                ParentDirectory = directory,
                Path = IOPath.Combine(directory.Path, RandomString(10)),
                Added = RandomDateTime(),
            };
            _dbContext.Directories.Add(subdirectory);
            return subdirectory;
        }

        internal File AddFile(Directory directory)
        {
            var file = new File
            {
                Library = directory.Library,
                Directory = directory,
                Name = RandomString(20) + "." + RandomString(3),
                Size = RandomInt32(0, 1000000 + 1),
                ModificationTime = RandomDateTime(),
                FormatName = RandomString(3),
                Added = RandomDateTime(),
            };
            _dbContext.Files.Add(file);
            return file;
        }

        internal Artist AddArtist()
        {
            var artist = new Artist
            {
                Name = RandomString(10),
                SortName = RandomString(10),
                Added = RandomDateTime(),
                Dirty = false,
            };
            _dbContext.Artists.Add(artist);
            return artist;
        }

        internal Album AddAlbum(Artist artist = null, Picture coverPicture = null, Genre genre = null)
        {
            var date = RandomDateTime();
            var originalDate = RandomDateTime();
            var album = new Album
            {
                Artist = artist,
                CoverPicture = coverPicture,
                Genre = genre,
                Date = date.Year * 10000 + date.Month * 100 + date.Day,
                OriginalDate = originalDate.Year * 10000 + originalDate.Month * 100 + originalDate.Day,
                Title = RandomString(10),
                SortTitle = RandomString(10),
                Added = RandomDateTime(),
                Dirty = false,
            };
            _dbContext.Albums.Add(album);
            return album;
        }

        internal Track AddTrack(File file, Artist artist, Album album, Picture coverPicture = null, Genre genre = null)
        {
            var track = new Track
            {
                Library = file.Library,
                File = file,
                Artist = artist,
                Album = album,
                CoverPicture = coverPicture,
                Genre = genre,
                StreamIndex = RandomInt32(0, 10),
                CodecName = RandomString(10),
                BitRate = RandomInt32(1000, 320000 + 1),
                Duration = RandomInt32(0, 10 * 60 * 1000 + 1) / 1000f,
                ArtistSortName = artist.SortName,
                Date = album.Date,
                OriginalDate = album.OriginalDate,
                AlbumSortTitle = album.SortTitle,
                DiscNumber = RandomInt32(1, 10),
                TrackNumber = RandomInt32(1, 10),
                Title = RandomString(10),
                SortTitle = RandomString(10),
                AlbumGain = RandomInt32(-60 * 100, 60 * 100 + 1) / 100f,
                TrackGain = RandomInt32(-60 * 100, 60 * 100 + 1) / 100f,
                Added = RandomDateTime(),
            };
            _dbContext.Tracks.Add(track);
            return track;
        }

        internal Picture AddPicture(File file)
        {
            var picture = new Picture()
            {
                File = file,
                StreamIndex = RandomInt32(0, 10),
                StreamHash = RandomInt64(),
            };
            _dbContext.Pictures.Add(picture);
            return picture;
        }

        internal Genre AddGenre()
        {
            var genre = new Genre
            {
                Name = RandomString(10),
            };
            _dbContext.Genres.Add(genre);
            return genre;
        }

        internal TrackGenre AddTrackGenre(Track track, Genre genre)
        {
            var trackGenre = new TrackGenre
            {
                Track = track,
                Genre = genre,
            };
            _dbContext.TrackGenres.Add(trackGenre);
            return trackGenre;
        }

        internal Playlist AddPlaylist(User user, bool @public = false)
        {
            var playlist = new Playlist
            {
                User = user,
                Name = RandomString(10),
                Description = RandomString(10),
                IsPublic = @public,
                Created = RandomDateTime(),
                Modified = RandomDateTime(),
            };
            _dbContext.Playlists.Add(playlist);
            return playlist;
        }

        internal PlaylistTrack AddPlaylistTrack(Playlist playlist, Track track, int index)
        {
            var playlistTrack = new PlaylistTrack
            {
                Playlist = playlist,
                Track = track,
                Index = index,
            };
            _dbContext.PlaylistTracks.Add(playlistTrack);
            return playlistTrack;
        }

        internal ArtistStar AddArtistStar(Artist artist, User user)
        {
            var artistStar = new ArtistStar
            {
                Artist = artist,
                User = user,
                Added = RandomDateTime(),
            };
            _dbContext.ArtistStars.Add(artistStar);
            return artistStar;
        }

        internal AlbumStar AddAlbumStar(Album album, User user)
        {
            var albumStar = new AlbumStar
            {
                Album = album,
                User = user,
                Added = RandomDateTime(),
            };
            _dbContext.AlbumStars.Add(albumStar);
            return albumStar;
        }

        internal TrackStar AddTrackStar(Track track, User user)
        {
            var trackStar = new TrackStar
            {
                Track = track,
                User = user,
                Added = RandomDateTime(),
            };
            _dbContext.TrackStars.Add(trackStar);
            return trackStar;
        }

        private int RandomInt32(int min, int max)
        {
            lock (_random)
                return _random.Next(min, max);
        }

        private long RandomInt64()
        {
            lock (_random)
                return (long)(((ulong)_random.Next() << 32) | (uint)_random.Next());
        }

        private long RandomInt64(long min, long max)
        {
            if (max - min < int.MaxValue)
                lock (_random)
                    return _random.Next((int)(max - min)) + min;

            ulong mask = (ulong)(max - min);
            mask |= mask >> 1;
            mask |= mask >> 2;
            mask |= mask >> 4;
            mask |= mask >> 8;
            mask |= mask >> 16;
            mask |= mask >> 32;
            lock (_random)
            {
                for (; ; )
                {
                    long value = (long)((((ulong)_random.Next() << 32) | (uint)_random.Next()) & mask) + min;
                    if (value >= min && value < max)
                        return value;
                }
            }
        }

        private DateTime RandomDateTime()
        {
            return new DateTime(RandomInt64(DateTime.MinValue.Ticks, DateTime.MaxValue.Ticks + 1));
        }

        private string RandomString(int length)
        {
            const int log2 = 5;
            const int mask = (1 << log2) - 1;
            var symbols = new char[]
            {
                '2', '3', '4', '5', '6', '7', '8', '9',
                'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h',
                'i', 'j', 'k', 'm', 'n', 'p', 'q', 'r',
                's', 't', 'u', 'v', 'w', 'x', 'y', 'z',
            };

            var chars = new char[length];
            var bytes = new byte[(chars.Length * log2 - 1) / 8 + 1];
            lock (_random)
                _random.NextBytes(bytes);
            int byteIndex = 0;
            int bitBuffer = 0;
            int bitCount = 0;
            for (int i = 0; i < chars.Length; ++i)
            {
                if (bitCount < log2)
                {
                    bitBuffer |= bytes[byteIndex] << bitCount;
                    bitCount += 8;
                    byteIndex += 1;
                }
                chars[i] = symbols[bitBuffer & mask];
                bitBuffer >>= log2;
                bitCount -= log2;
            }
            return new string(chars);
        }
    }
}
