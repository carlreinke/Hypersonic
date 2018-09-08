//
// Copyright (C) 2018  Carl Reinke
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
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Hypersonic.Data
{
    internal sealed class MediaInfoContext : DbContext
    {
        public MediaInfoContext(DbContextOptions options)
            : base(options)
        {
        }

        public DbSet<User> Users { get; set; }

        public DbSet<Library> Libraries { get; set; }

        public DbSet<LibraryUser> LibraryUsers { get; set; }

        public DbSet<Directory> Directories { get; set; }

        public DbSet<File> Files { get; set; }

        public DbSet<Artist> Artists { get; set; }

        public DbSet<Album> Albums { get; set; }

        public DbSet<Track> Tracks { get; set; }

        public DbSet<Picture> Pictures { get; set; }

        public DbSet<Genre> Genres { get; set; }

        public DbSet<TrackGenre> TrackGenres { get; set; }

        public DbSet<Playlist> Playlists { get; set; }

        public DbSet<PlaylistTrack> PlaylistTracks { get; set; }

        public DbSet<ArtistStar> ArtistStars { get; set; }

        public DbSet<AlbumStar> AlbumStars { get; set; }

        public DbSet<TrackStar> TrackStars { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>()
                .HasAlternateKey(u => u.Name);

            modelBuilder.Entity<Library>()
                .HasAlternateKey(l => l.Name);

            modelBuilder.Entity<LibraryUser>()
                .HasKey(lu => new { lu.LibraryId, lu.UserId });
            modelBuilder.Entity<LibraryUser>()
                .HasIndex(lu => lu.LibraryId);
            modelBuilder.Entity<LibraryUser>()
                .HasIndex(lu => lu.UserId);
            modelBuilder.Entity<LibraryUser>()
                .HasOne(lu => lu.Library)
                .WithMany(l => l.LibraryUsers)
                .HasForeignKey(lu => lu.LibraryId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<LibraryUser>()
                .HasOne(lu => lu.User)
                .WithMany(u => u.LibraryUsers)
                .HasForeignKey(lu => lu.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Directory>()
                .HasOne(t => t.Library)
                .WithMany(l => l.Directories)
                .HasForeignKey(t => t.LibraryId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<Directory>()
                .HasOne(d => d.ParentDirectory)
                .WithMany(p => p.Directories)
                .HasForeignKey(d => d.ParentDirectoryId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<File>()
                .HasOne(t => t.Directory)
                .WithMany(d => d.Files)
                .HasForeignKey(t => t.DirectoryId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<File>()
                .HasOne(t => t.Library)
                .WithMany(l => l.Files)
                .HasForeignKey(f => f.LibraryId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Album>()
                .HasOne(a => a.Artist)
                .WithMany()
                .HasForeignKey(a => a.ArtistId)
                .OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<Album>()
                .HasOne(a => a.CoverPicture)
                .WithMany()
                .HasForeignKey(a => a.CoverPictureId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<Track>()
                .HasOne(t => t.Library)
                .WithMany(l => l.Tracks)
                .HasForeignKey(t => t.LibraryId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<Track>()
                .HasOne(t => t.File)
                .WithMany(f => f.Tracks)
                .HasForeignKey(f => f.FileId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<Track>()
                .HasOne(t => t.Artist)
                .WithMany()
                .HasForeignKey(t => t.ArtistId)
                .OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<Track>()
                .HasOne(t => t.Album)
                .WithMany()
                .HasForeignKey(t => t.AlbumId)
                .OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<Track>()
                .HasOne(t => t.CoverPicture)
                .WithMany()
                .HasForeignKey(t => t.CoverPictureId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<Picture>()
                .HasIndex(a => a.StreamHash);
            modelBuilder.Entity<Picture>()
                .HasOne(a => a.File)
                .WithMany()
                .HasForeignKey(a => a.FileId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Genre>()
                .HasAlternateKey(g => g.Name);
            modelBuilder.Entity<Genre>()
                .HasMany<Album>()
                .WithOne(a => a.Genre)
                .HasForeignKey(a => a.GenreId)
                .OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<Genre>()
                .HasMany<Track>()
                .WithOne(t => t.Genre)
                .HasForeignKey(t => t.GenreId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TrackGenre>()
                .HasKey(tg => new { tg.TrackId, tg.GenreId });
            modelBuilder.Entity<TrackGenre>()
                .HasIndex(tg => tg.TrackId);
            modelBuilder.Entity<TrackGenre>()
                .HasIndex(tg => tg.GenreId);
            modelBuilder.Entity<TrackGenre>()
                .HasOne(tg => tg.Track)
                .WithMany(t => t.TrackGenres)
                .HasForeignKey(tg => tg.TrackId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<TrackGenre>()
                .HasOne(tg => tg.Genre)
                .WithMany(g => g.TrackGenres)
                .HasForeignKey(tg => tg.GenreId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Playlist>()
                .HasOne(p => p.User)
                .WithMany(u => u.Playlists)
                .HasForeignKey(u => u.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PlaylistTrack>()
                .HasKey(pt => new { pt.PlaylistId, pt.Index });
            modelBuilder.Entity<PlaylistTrack>()
                .HasIndex(pt => pt.PlaylistId);
            modelBuilder.Entity<PlaylistTrack>()
                .HasOne(pt => pt.Playlist)
                .WithMany(p => p.PlaylistTracks)
                .HasForeignKey(pt => pt.PlaylistId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<PlaylistTrack>()
                .HasOne(pt => pt.Track)
                .WithMany()
                .HasForeignKey(pt => pt.TrackId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ArtistStar>()
                .HasKey(aus => new { aus.ArtistId, aus.UserId });
            modelBuilder.Entity<ArtistStar>()
                .HasIndex(aus => aus.ArtistId);
            modelBuilder.Entity<ArtistStar>()
                .HasIndex(aus => aus.UserId);
            modelBuilder.Entity<ArtistStar>()
                .HasOne(aus => aus.Artist)
                .WithMany()
                .HasForeignKey(aus => aus.ArtistId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<ArtistStar>()
                .HasOne(aus => aus.User)
                .WithMany()
                .HasForeignKey(aus => aus.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<AlbumStar>()
                .HasKey(aus => new { aus.AlbumId, aus.UserId });
            modelBuilder.Entity<AlbumStar>()
                .HasIndex(aus => aus.AlbumId);
            modelBuilder.Entity<AlbumStar>()
                .HasIndex(aus => aus.UserId);
            modelBuilder.Entity<AlbumStar>()
                .HasOne(aus => aus.Album)
                .WithMany()
                .HasForeignKey(aus => aus.AlbumId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<AlbumStar>()
                .HasOne(aus => aus.User)
                .WithMany()
                .HasForeignKey(aus => aus.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<TrackStar>()
                .HasKey(tus => new { tus.TrackId, tus.UserId });
            modelBuilder.Entity<TrackStar>()
                .HasIndex(tus => tus.TrackId);
            modelBuilder.Entity<TrackStar>()
                .HasIndex(tus => tus.UserId);
            modelBuilder.Entity<TrackStar>()
                .HasOne(tus => tus.Track)
                .WithMany()
                .HasForeignKey(tus => tus.TrackId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<TrackStar>()
                .HasOne(tus => tus.User)
                .WithMany()
                .HasForeignKey(tus => tus.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        }

        [DbFunction("RANDOM")]
        public static int Random() => throw new NotImplementedException();

        [DbFunction("ROW_NUMBER")]
        public static int RowNumber() => throw new NotImplementedException();
    }

    internal sealed class User
    {
        [Key]
        public int UserId { get; set; }

        [Required]
        public string Name { get; set; }
        [Required]
        public string Password { get; set; }
        public int MaxBitRate { get; set; }
        public bool IsAdmin { get; set; }
        public bool IsGuest { get; set; }
        public bool CanJukebox { get; set; }

        public List<LibraryUser> LibraryUsers { get; set; }
        public List<Playlist> Playlists { get; set; }
    }

    internal sealed class Library
    {
        [Key]
        public int LibraryId { get; set; }

        [Required]
        public string Name { get; set; }
        public bool IsAccessControlled { get; set; }
        public DateTime ContentModified { get; set; }

        public List<LibraryUser> LibraryUsers { get; set; }
        public List<Directory> Directories { get; set; }
        public List<File> Files { get; set; }
        public List<Track> Tracks { get; set; }
    }

    internal sealed class LibraryUser
    {
        public int LibraryId { get; set; }
        public int UserId { get; set; }
        public Library Library { get; set; }
        public User User { get; set; }
    }

    internal sealed class Directory
    {
        [Key]
        public int DirectoryId { get; set; }

        public int LibraryId { get; set; }
        public int? ParentDirectoryId { get; set; }
        public Library Library { get; set; }
        public Directory ParentDirectory { get; set; }

        [Required]
        public string Path { get; set; }

        public DateTime Added { get; set; }

        public List<Directory> Directories { get; set; }
        public List<File> Files { get; set; }
    }

    internal sealed class File
    {
        [Key]
        public int FileId { get; set; }

        public int LibraryId { get; set; }
        public int DirectoryId { get; set; }
        public Library Library { get; set; }
        public Directory Directory { get; set; }

        [Required]
        public string Name { get; set; }
        public long Size { get; set; }
        public DateTime ModificationTime { get; set; }
        [Required]
        public string FormatName { get; set; }

        public DateTime Added { get; set; }

        public List<Track> Tracks { get; set; }
    }

    internal sealed class Artist
    {
        [Key]
        public int ArtistId { get; set; }

        public string Name { get; set; }
        public string SortName { get; set; }

        public DateTime Added { get; set; }
        public bool Dirty { get; set; }
    }

    internal sealed class Album
    {
        [Key]
        public int AlbumId { get; set; }

        public int? ArtistId { get; set; }
        public int? CoverPictureId { get; set; }
        public int? GenreId { get; set; }
        public Artist Artist { get; set; }
        public Picture CoverPicture { get; set; }
        public Genre Genre { get; set; }

        public int? Date { get; set; }
        public int? OriginalDate { get; set; }
        public string Title { get; set; }
        public string SortTitle { get; set; }

        public DateTime Added { get; set; }
        public bool Dirty { get; set; }
    }

    internal sealed class Track
    {
        [Key]
        public int TrackId { get; set; }

        public int LibraryId { get; set; }
        public int FileId { get; set; }
        public int ArtistId { get; set; }
        public int AlbumId { get; set; }
        public int? CoverPictureId { get; set; }
        public int? GenreId { get; set; }
        public Library Library { get; set; }
        public File File { get; set; }
        public Artist Artist { get; set; }
        public Album Album { get; set; }
        public Picture CoverPicture { get; set; }
        public Genre Genre { get; set; }

        public int StreamIndex { get; set; }
        [Required]
        public string CodecName { get; set; }
        public int? BitRate { get; set; }
        public float? Duration { get; set; }
        public string ArtistSortName { get; set; }
        public int? Date { get; set; }
        public int? OriginalDate { get; set; }
        public string AlbumSortTitle { get; set; }
        public int? DiscNumber { get; set; }
        public int? TrackNumber { get; set; }
        [Required]
        public string Title { get; set; }
        public string SortTitle { get; set; }
        public float? AlbumGain { get; set; }
        public float? TrackGain { get; set; }

        public DateTime Added { get; set; }

        public List<TrackGenre> TrackGenres { get; set; }
    }

    internal sealed class Picture
    {
        [Key]
        public int PictureId { get; set; }

        public int FileId { get; set; }
        public File File { get; set; }

        public int StreamIndex { get; set; }

        public long StreamHash { get; set; }
    }

    internal sealed class Genre
    {
        [Key]
        public int GenreId { get; set; }

        [Required]
        public string Name { get; set; }

        public List<TrackGenre> TrackGenres { get; set; }
    }

    internal sealed class TrackGenre
    {
        public int TrackId { get; set; }
        public int GenreId { get; set; }
        public Track Track { get; set; }
        public Genre Genre { get; set; }
    }

    internal sealed class Playlist
    {
        [Key]
        public int PlaylistId { get; set; }

        public int UserId { get; set; }
        public User User { get; set; }

        [Required]
        public string Name { get; set; }
        public string Description { get; set; }
        public bool IsPublic { get; set; }

        public DateTime Created { get; set; }
        public DateTime Modified { get; set; }

        public List<PlaylistTrack> PlaylistTracks { get; set; }
    }

    internal sealed class PlaylistTrack
    {
        public int PlaylistId { get; set; }
        public int TrackId { get; set; }
        public Playlist Playlist { get; set; }
        public Track Track { get; set; }

        public int Index { get; set; }
    }

    internal sealed class ArtistStar
    {
        public int ArtistId { get; set; }
        public int UserId { get; set; }
        public Artist Artist { get; set; }
        public User User { get; set; }

        public DateTime Added { get; set; }
    }

    internal sealed class AlbumStar
    {
        public int AlbumId { get; set; }
        public int UserId { get; set; }
        public Album Album { get; set; }
        public User User { get; set; }

        public DateTime Added { get; set; }
    }

    internal sealed class TrackStar
    {
        public int TrackId { get; set; }
        public int UserId { get; set; }
        public Track Track { get; set; }
        public User User { get; set; }

        public DateTime Added { get; set; }
    }
}
