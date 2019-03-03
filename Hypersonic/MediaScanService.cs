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
using Hypersonic.Data;
using Hypersonic.Ffmpeg;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Hypersonic
{
    internal sealed class MediaScanService : IDisposable
    {
        private readonly CancellationTokenSource _serviceCancellationTokenSource = new CancellationTokenSource();

        private readonly TimeSpan _rescanInterval = TimeSpan.FromHours(24);

        private readonly object _scanTaskLock = new object();

        private readonly IServiceScopeFactory _serviceScopeFactory;

        private CancellationTokenSource _scanCancellationTokenSource;

        private Task _scanTask;

        public MediaScanService(IServiceScopeFactory serviceScopeFactory)
        {
            if (serviceScopeFactory == null)
                throw new ArgumentNullException(nameof(serviceScopeFactory));

            _serviceScopeFactory = serviceScopeFactory;

            using (var scope = serviceScopeFactory.CreateScope())
            {
                var lifetime = scope.ServiceProvider.GetService<IApplicationLifetime>();
                if (lifetime != null)
                {
                    lifetime.ApplicationStarted.Register(HandleApplicationStarted);
                    lifetime.ApplicationStopping.Register(HandleApplicationStopping);
                }
            }
        }

        public bool IsScanning
        {
            get
            {
                lock (_scanTaskLock)
                    return _scanTask != null && !_scanTask.IsCompleted;
            }
        }

        public void Dispose()
        {
            _serviceCancellationTokenSource.Cancel();
            _serviceCancellationTokenSource.Dispose();

            StopScanAsync().Wait();
        }

        public async Task ScanAsync(bool force = false)
        {
        again:
            await StopScanAsync().ConfigureAwait(false);

            Task scanTask;

            lock (_scanTaskLock)
            {
                if (_scanTask != null && !_scanTask.IsCompleted)
                    goto again;

                Debug.Assert(_scanCancellationTokenSource == null);
                _scanCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_serviceCancellationTokenSource.Token);

                var cancellationToken = _scanCancellationTokenSource.Token;

                _scanTask = Task.Run(async () =>
                {
                    try
                    {
                        await ScanLibrariesAsync(force, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine("Exception occurred during media scan:");
                        Console.Error.WriteLine(ex.ToString());
                    }
                }, cancellationToken);

                scanTask = _scanTask;
            }

            await scanTask.ConfigureAwait(false);
        }

        public async Task StopScanAsync()
        {
            Task scanTask;

            lock (_scanTaskLock)
            {
                if (_scanCancellationTokenSource != null)
                {
                    _scanCancellationTokenSource.Cancel();
                    _scanCancellationTokenSource.Dispose();
                    _scanCancellationTokenSource = null;
                }

                scanTask = _scanTask;
            }

            if (scanTask != null)
                await scanTask.ConfigureAwait(false);
        }

        private void HandleApplicationStarted()
        {
            var cancellationToken = _serviceCancellationTokenSource.Token;

            Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (!IsScanning)
                        await ScanAsync().ConfigureAwait(false);

                    await Task.Delay(_rescanInterval, cancellationToken).ConfigureAwait(false);
                }
            }, cancellationToken);
        }

        private void HandleApplicationStopping()
        {
            _serviceCancellationTokenSource.Cancel();
        }

        private async Task ScanLibrariesAsync(bool force, CancellationToken cancellationToken)
        {
            await Console.Out.WriteLineAsync("Media scan started.".AsMemory(), cancellationToken).ConfigureAwait(false);

            using (var serviceScope = _serviceScopeFactory.CreateScope())
            {
                var dbContext = serviceScope.ServiceProvider.GetRequiredService<MediaInfoContext>();

                await dbContext.Libraries
                    .ForEachAwaitAsync(library =>
                    {
                        return ScanLibraryAsync(dbContext, library, force, cancellationToken);
                    }, cancellationToken).ConfigureAwait(false);

                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            await Console.Out.WriteLineAsync("Media scan finished.".AsMemory(), cancellationToken).ConfigureAwait(false);
        }

        private static async Task ScanLibraryAsync(MediaInfoContext dbContext, Library library, bool force, CancellationToken cancellationToken)
        {
            await Console.Out.WriteLineAsync($"Scanning '{library.Name}'...".AsMemory(), cancellationToken).ConfigureAwait(false);

            await dbContext.QueryCollection(library, l => l.Directories)
                .Where(d => d.ParentDirectory == null)
                .ForEachAwaitAsync(async directory =>
                {
                    var directoryInfo = new DirectoryInfo(directory.Path);

                    bool modified = await ScanDirectoryAsync(dbContext, directory, directoryInfo, force, cancellationToken).ConfigureAwait(false);
                    if (modified)
                        library.ContentModified = DateTime.UtcNow;

                    await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }, cancellationToken).ConfigureAwait(false);

            await Console.Out.WriteLineAsync("Fixing up artists...".AsMemory(), cancellationToken).ConfigureAwait(false);

            await dbContext.Artists
                .Where(a => a.Dirty)
                .ForEachAwaitAsync(async artist =>
                {
                    artist.SortName = await dbContext.Tracks
                        .Where(t => t.Artist == artist)
                        .Where(t => t.ArtistSortName != null)
                        .GroupBy(t => t.ArtistSortName, (a, t) => new { ArtistSortName = a, Count = t.Count() })
                        .OrderByDescending(e => e.Count)
                        .Select(e => e.ArtistSortName)
                        .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

                    artist.Dirty = false;

                    await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }, cancellationToken).ConfigureAwait(false);

            await Console.Out.WriteLineAsync("Fixing up albums...".AsMemory(), cancellationToken).ConfigureAwait(false);

            await dbContext.Albums
                .Where(a => a.Dirty)
                .ForEachAwaitAsync(async album =>
                {
                    album.CoverPictureId = await dbContext.Tracks
                        .Where(t => t.Album == album)
                        .Where(t => t.CoverPictureId != null)
                        .OrderBy(t => t.DiscNumber ?? int.MaxValue)
                        .ThenBy(t => t.TrackNumber ?? int.MaxValue)
                        .Select(t => t.CoverPictureId)
                        .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

                    album.GenreId = await dbContext.Tracks
                        .Where(t => t.Album == album)
                        .Where(t => t.GenreId != null)
                        .GroupBy(t => t.GenreId, (g, t) => new { GenreId = g, Count = t.Count() })
                        .OrderByDescending(e => e.Count)
                        .Select(e => e.GenreId)
                        .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

                    album.SortTitle = await dbContext.Tracks
                        .Where(t => t.Album == album)
                        .Where(t => t.AlbumSortTitle != null)
                        .GroupBy(t => t.AlbumSortTitle, (a, t) => new { AlbumSortTitle = a, Count = t.Count() })
                        .OrderByDescending(e => e.Count)
                        .Select(e => e.AlbumSortTitle)
                        .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

                    album.OriginalDate = await dbContext.Tracks
                        .Where(t => t.Album == album)
                        .Select(t => t.OriginalDate)
                        .MaxAsync(cancellationToken).ConfigureAwait(false);

                    album.Dirty = false;

                    await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }, cancellationToken).ConfigureAwait(false);

            // TODO: Delete unused artist, album, genre?
        }

        private static async Task<bool> ScanDirectoryAsync(MediaInfoContext dbContext, Data.Directory directory, DirectoryInfo directoryInfo, bool force, CancellationToken cancellationToken)
        {
            Debug.WriteLine("Scanning '{0}'...", new[] { directoryInfo.FullName });

            bool modified = false;

            if (!directoryInfo.Exists)
                goto delete;

            try
            {
                modified |= await ScanSubdirectoriesAsync(dbContext, directory, directoryInfo, force, cancellationToken).ConfigureAwait(false);

                modified |= await ScanFilesAsync(dbContext, directory, directoryInfo, force, cancellationToken).ConfigureAwait(false);

                // Set missing cover pictures from "cover.jpg" or "cover.png".

                int? coverPictureId = await dbContext.QueryCollection(directory, d => d.Files)
#pragma warning disable CA1304 // Specify CultureInfo
                    .Where(f => f.Name.ToUpper() == "COVER.JPG" ||
                                f.Name.ToUpper() == "COVER.PNG")
#pragma warning restore CA1304 // Specify CultureInfo
                    .Join(dbContext.Pictures, f => f.FileId, p => p.FileId, (f, p) => p.PictureId as int?)
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                if (coverPictureId != null)
                {
                    await dbContext.QueryCollection(directory, d => d.Files)
                        .SelectMany(f => f.Tracks)
                        .Where(t => t.CoverPictureId == null)
                        .ForEachAsync(track =>
                        {
                            track.CoverPictureId = coverPictureId;
                        }, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Exception thrown on '{0}': {1}", directoryInfo.FullName, ex);
                Debugger.Break();

                goto delete;
            }

            modified |= await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false) > 0;
            return modified;

        delete:
            if (directory.ParentDirectoryId.HasValue)
                dbContext.Remove(directory);

            modified |= await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false) > 0;
            return modified;
        }

        private static async Task<bool> ScanSubdirectoriesAsync(MediaInfoContext dbContext, Data.Directory directory, DirectoryInfo directoryInfo, bool force, CancellationToken cancellationToken)
        {
            bool modified = false;

            var newSubdirectoryInfos = directoryInfo.EnumerateDirectories()
                .ToDictionary(d => d.FullName);

            var staleSubdirectories = new List<Data.Directory>();

            await dbContext.QueryCollection(directory, d => d.Directories)
                .ForEachAwaitAsync(async subdirectory =>
                {
                    if (newSubdirectoryInfos.TryGetValue(subdirectory.Path, out DirectoryInfo subdirectoryInfo))
                    {
                        newSubdirectoryInfos.Remove(subdirectory.Path);

                        modified |= await ScanDirectoryAsync(dbContext, subdirectory, subdirectoryInfo, force, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        staleSubdirectories.Add(subdirectory);
                    }
                }, cancellationToken).ConfigureAwait(false);

            dbContext.Directories.RemoveRange(staleSubdirectories);
            staleSubdirectories.Clear();

            foreach (var subdirectoryInfo in newSubdirectoryInfos.Values)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var subdirectory = new Data.Directory()
                {
                    Library = directory.Library,
                    ParentDirectory = directory,
                    Path = subdirectoryInfo.FullName,
                    Added = DateTime.UtcNow,
                };
                await dbContext.Directories.AddAsync(subdirectory, cancellationToken).ConfigureAwait(false);

                modified |= await ScanDirectoryAsync(dbContext, subdirectory, subdirectoryInfo, force, cancellationToken).ConfigureAwait(false);
            }

            return modified;
        }

        private static async Task<bool> ScanFilesAsync(MediaInfoContext dbContext, Data.Directory directory, DirectoryInfo directoryInfo, bool force, CancellationToken cancellationToken)
        {
            bool modified = false;

            var fileInfos = directoryInfo.EnumerateFiles()
                .ToDictionary(f => f.Name);

            var staleFiles = new List<Data.File>();

            await dbContext.QueryCollection(directory, d => d.Files)
                .ForEachAwaitAsync(async file =>
                {
                    if (fileInfos.Remove(file.Name, out FileInfo fileInfo))
                    {
                        modified |= await ScanFileAsync(dbContext, file, fileInfo, force, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        staleFiles.Add(file);
                    }
                }, cancellationToken).ConfigureAwait(false);

            dbContext.Files.RemoveRange(staleFiles);
            staleFiles.Clear();

            foreach (var fileInfo in fileInfos.Values)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var file = new Data.File()
                {
                    Library = directory.Library,
                    Directory = directory,
                    Name = fileInfo.Name,
                    Size = 0,
                    ModificationTime = default,
                    FormatName = string.Empty,
                    Added = DateTime.UtcNow,
                };
                await dbContext.Files.AddAsync(file, cancellationToken).ConfigureAwait(false);

                modified |= await ScanFileAsync(dbContext, file, fileInfo, force, cancellationToken).ConfigureAwait(false);
            }

            return modified;
        }

        private static async Task<bool> ScanFileAsync(MediaInfoContext dbContext, Data.File file, FileInfo fileInfo, bool force, CancellationToken cancellationToken)
        {
            Debug.WriteLine("Scanning '{0}'...", new[] { fileInfo.FullName });

            bool modified = false;

            if (!fileInfo.Exists)
                goto delete;

            if (!force &&
                file.Size == fileInfo.Length &&
                file.ModificationTime == fileInfo.LastWriteTimeUtc)
            {
                // File is already up-to-date.
                return modified;
            }

            try
            {
                FfmpegProber.Show probeShow = FfmpegProber.Show.Error | FfmpegProber.Show.Format | FfmpegProber.Show.Streams;
                ffprobeType result;

            probeAgain:
                result = FfmpegProber.Probe(fileInfo.FullName, probeShow);  // TODO: async

                if (result == null)
                {
                    Debug.WriteLine("ffprobe failed on '{0}'", new[] { fileInfo.FullName });
                    goto delete;
                }

                if (result.error != null)
                {
                    Debug.WriteLine("ffprobe returned an error for '{0}': {1} {2}", fileInfo.FullName, result.error.code, result.error.@string);
                    goto delete;
                }

                if (result.format != null && result.streams != null)
                {
                    var format = result.format;

                    if (!probeShow.HasFlag(FfmpegProber.Show.Packets))
                    {
                        foreach (var stream in result.streams)
                        {
                            if (stream.codec_type == "audio" && ((!format.durationSpecified && !stream.durationSpecified) || !stream.bit_rateSpecified))
                            {
                                probeShow |= FfmpegProber.Show.Packets;
                                goto probeAgain;
                            }
                        }
                    }

                    string formatName = GetFormatName(result);

                    file.FormatName = formatName;

                    int? coverPictureId = await ScanPictureAsync(dbContext, file, fileInfo, result, cancellationToken).ConfigureAwait(false);

                    await ScanTracksAsync(dbContext, file, fileInfo, result, coverPictureId, cancellationToken).ConfigureAwait(false);
                }

                file.Size = fileInfo.Length;
                file.ModificationTime = fileInfo.LastWriteTimeUtc;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Exception thrown on '{0}': {1}", fileInfo.FullName, ex);
                Debugger.Break();

                goto delete;
            }

            modified |= await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false) > 0;
            return modified;

        delete:
            dbContext.Remove(file);

            modified |= await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false) > 0;
            return modified;
        }

        private static async Task<int?> ScanPictureAsync(MediaInfoContext dbContext, Data.File file, FileInfo fileInfo, ffprobeType result, CancellationToken cancellationToken)
        {
            int? coverPictureId = null;

            var stalePictures = await dbContext.Pictures
                .Where(p => p.FileId == file.FileId)
                .ToDictionaryAsync(p => p.StreamIndex, cancellationToken).ConfigureAwait(false);

            streamType newCoverStream = GetCoverStream();
            long? newCoverStreamHash = null;
            if (newCoverStream != null)
            {
                byte[] streamHashBytes = await FfmpegStreamHasher.HashAsync(fileInfo.FullName, newCoverStream.index, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (streamHashBytes != null)
                    newCoverStreamHash = BinaryPrimitives.ReadInt64BigEndian(streamHashBytes);
                else
                    newCoverStream = null;
            }

            if (newCoverStream != null)
            {
                if (stalePictures.TryGetValue(newCoverStream.index, out Picture picture) &&
                    picture.StreamHash == newCoverStreamHash)
                {
                    stalePictures.Remove(picture.StreamIndex);

                    newCoverStream = null;

                    coverPictureId = picture.PictureId;
                }
            }

            foreach (var picture in stalePictures.Values)
            {
                dbContext.Pictures.Remove(picture);
            }

            if (newCoverStream != null)
            {
                var picture = new Picture()
                {
                    File = file,
                    StreamIndex = newCoverStream.index,
                    StreamHash = newCoverStreamHash.Value,
                };
                await dbContext.Pictures.AddAsync(picture, cancellationToken).ConfigureAwait(false);

                coverPictureId = picture.PictureId;
            }

            return coverPictureId;

            streamType GetCoverStream()
            {
                // MP3
                {
                    var coverStream = result.streams
                        .FirstOrDefault(s => s.codec_type == "video" &&
                            s.disposition.attached_pic == 1 &&
                            (s.tag != null && s.tag.Any(t => t.key == "comment" && t.value == "Cover (front)")));
                    if (coverStream != null)
                        return coverStream;
                }

                // Vorbis
                {
                    var coverStream = result.streams
                        .FirstOrDefault(s => s.codec_type == "video" &&
                            s.disposition.attached_pic == 1 &&
                            (s.tag == null || !s.tag.Any(t => t.key == "comment")));
                    if (coverStream != null)
                        return coverStream;
                }

                // "cover.jpg"
                if (result.format.format_name == "image2")
                {
                    var coverStream = result.streams
                        .FirstOrDefault(s => s.codec_type == "video");
                    if (coverStream != null)
                        return coverStream;
                }

                return null;
            }
        }

        private static async Task ScanTracksAsync(MediaInfoContext dbContext, Data.File file, FileInfo fileInfo, ffprobeType result, int? coverPictureId, CancellationToken cancellationToken)
        {
            var trackStreams = result.streams
                .Where(s => s.codec_type == "audio")
                .ToDictionary(s => s.index);

            var staleTracks = new List<Track>();

            await dbContext.QueryCollection(file, f => f.Tracks)
                .ForEachAwaitAsync(async track =>
                {
                    if (trackStreams.Remove(track.StreamIndex, out streamType trackStream))
                    {
                        await ScanTrackAsync(dbContext, fileInfo, result, trackStream, track, coverPictureId, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        staleTracks.Add(track);
                    }
                }, cancellationToken).ConfigureAwait(false);

            dbContext.Tracks.RemoveRange(staleTracks);
            staleTracks.Clear();

            foreach (var stream in trackStreams.Values)
            {
                var track = new Track()
                {
                    Library = file.Library,
                    File = file,
                    Added = DateTime.UtcNow,
                };
                await dbContext.Tracks.AddAsync(track, cancellationToken).ConfigureAwait(false);

                await ScanTrackAsync(dbContext, fileInfo, result, stream, track, coverPictureId, cancellationToken).ConfigureAwait(false);
            }
        }

        private static async Task ScanTrackAsync(MediaInfoContext dbContext, FileInfo fileInfo, ffprobeType result, streamType stream, Track track, int? coverPictureId, CancellationToken cancellationToken)
        {
            string codecName = GetCodecName(result, stream);
            int? bitRate = GetBitRate(result, stream);
            float? duration = GetDuration(result, stream);
            if (!duration.HasValue && bitRate > 0 && result.format.sizeSpecified)
                duration = (float)((double)result.format.size * 8 / bitRate);

            var tags = GetTags(result, stream);

            string artistName = GetArtistName(tags);
            string artistSortName = GetArtistSortName(tags);
            string albumArtistName = GetAlbumArtistName(tags) ?? artistName;
            string albumArtistSortName = GetAlbumArtistSortName(tags) ?? artistSortName;
            string albumTitle = GetAlbumTitle(tags);
            string albumSortTitle = GetAlbumSortTitle(tags);
            int? discNumber = GetDiscNumber(tags);
            int? trackNumber = GetTrackNumber(tags);
            string trackTitle = GetTrackTitle(tags);
            string trackSortTitle = GetTrackSortTitle(tags);
            int? date = GetDate(tags);
            int? originalDate = GetOriginalDate(tags);
            string[] genreNames = GetGenreNames(tags);
            float? albumGain = GetAlbumGain(tags);
            float? trackGain = GetTrackGain(tags);

            if (artistSortName == artistName)
                artistSortName = null;
            if (albumSortTitle == albumTitle)
                albumSortTitle = null;
            if (trackSortTitle == trackTitle)
                trackSortTitle = null;

            int artistId = await GetOrAddArtistAsync(dbContext, artistName, cancellationToken).ConfigureAwait(false);
            int albumArtistId = albumArtistName == null ? artistId : await GetOrAddArtistAsync(dbContext, albumArtistName, cancellationToken).ConfigureAwait(false);
            int albumId = await GetOrAddAlbumAsync(dbContext, albumArtistId, albumTitle, date, cancellationToken).ConfigureAwait(false);
            int[] genreIds = await genreNames
                .Where(g => g.Length != 0)
                .ToAsyncEnumerable()
                .SelectAwaitAsync(g => GetOrAddGenreAsync(dbContext, g, cancellationToken))
                .ToArray(cancellationToken).ConfigureAwait(false);

            var staleTrackGenres = await dbContext.QueryCollection(track, t => t.TrackGenres)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            dbContext.TrackGenres.RemoveRange(staleTrackGenres);
            staleTrackGenres.Clear();

            track.ArtistId = artistId;
            track.AlbumId = albumId;
            track.CoverPictureId = coverPictureId;
            track.GenreId = genreIds.Length == 0 ? (int?)null : genreIds[0];
            track.StreamIndex = stream.index;
            track.CodecName = codecName;
            track.BitRate = bitRate;
            track.Duration = duration;
            track.ArtistSortName = artistSortName;
            track.AlbumSortTitle = albumSortTitle;
            track.Date = date;
            track.OriginalDate = date;
            track.DiscNumber = discNumber;
            track.TrackNumber = trackNumber;
            track.Title = trackTitle ?? Path.GetFileNameWithoutExtension(fileInfo.Name);
            track.SortTitle = trackSortTitle;
            track.AlbumGain = albumGain;
            track.TrackGain = trackGain;
            track.TrackGenres = genreIds
                .OfType<int>()
                .Distinct()
                .Select(g => new TrackGenre()
                {
                    GenreId = g,
                })
                .ToList();

            if (artistName != null)
                InvalidateArtist(dbContext, artistId);
            if (albumArtistName != null)
                InvalidateArtist(dbContext, albumArtistId);
            if (albumTitle != null)
                InvalidateAlbum(dbContext, albumId);
        }

        private static string GetFormatName(ffprobeType result)
        {
            Debug.Assert(result.format.format_name != null);

            if (result.format.format_name.Contains(',', StringComparison.Ordinal))
            {
                string[] names = result.format.format_name.Split(',');

                if (names.Contains("mp4"))
                    return "mp4";

                Debug.WriteLine($"No preferred name for format '{result.format.format_name}'.");
            }

            return result.format.format_name;
        }

        private static string GetCodecName(ffprobeType result, streamType stream)
        {
            Debug.Assert(stream.codec_name != null);

            if (stream.codec_name.Contains(',', StringComparison.Ordinal))
            {
                string[] names = stream.codec_name.Split(',');

                _ = result;
                _ = names;

                Debug.WriteLine($"No preferred name for codec '{stream.codec_name}'.");
            }

            return stream.codec_name;
        }

        private static int? GetBitRate(ffprobeType result, streamType stream)
        {
            if (stream.bit_rateSpecified)
                return stream.bit_rate;

            if (result.packets != null)
            {
                float size = 0;
                float duration = 0;
                foreach (var packet in result.packets)
                {
                    if (packet.stream_index == stream.index && packet.duration_timeSpecified)
                    {
                        size += packet.size;
                        duration += packet.duration_time;
                    }
                }
                if (duration > 0)
                    return (int)Math.Round(size * 8 / duration);
            }

            return null;
        }

        private static float? GetDuration(ffprobeType result, streamType stream)
        {
            if (stream.durationSpecified)
                return stream.duration;

            if (result.format.durationSpecified)
                return result.format.duration;

            if (result.packets != null)
            {
                float duration = 0;
                foreach (var packet in result.packets)
                    if (packet.stream_index == stream.index && packet.duration_timeSpecified)
                        duration += packet.duration_time;
                if (duration > 0)
                    return duration;
            }

            return null;
        }

        private static Dictionary<string, string> GetTags(ffprobeType result, streamType stream)
        {
            var tags = new Dictionary<string, string>();

            if (result.format.tag != null)
                foreach (var tag in result.format.tag)
                    tags.Add(tag.key, tag.value);

            if (stream.tag != null)
                foreach (var tag in stream.tag)
                    tags.Add(tag.key, tag.value);

            return tags;
        }

        private static string GetArtistName(IReadOnlyDictionary<string, string> tags)
        {
            string value;

            if (tags.TryGetValue("ARTIST", out value) ||  // Vorbis
                tags.TryGetValue("artist", out value))    // ffmpeg (ID3, MP4, WMA)
            {
                return value;
            }

            return null;
        }

        private static string GetArtistSortName(IReadOnlyDictionary<string, string> tags)
        {
            string value;

            if (tags.TryGetValue("ARTISTSORT", out value) ||        // Vorbis
                tags.TryGetValue("sort_artist", out value) ||       // ffmpeg (MP4)
                tags.TryGetValue("artist-sort", out value) ||       // ffmpeg (ID3)
                tags.TryGetValue("WM/ArtistSortOrder", out value))  // WMA
            {
                return value;
            }

            return null;
        }

        private static string GetAlbumArtistName(IReadOnlyDictionary<string, string> tags)
        {
            string value;

            if (tags.TryGetValue("album_artist", out value))  // ffmpeg (Vorbis, ID3, MP4, WMA)
                return value;

            return null;
        }

        private static string GetAlbumArtistSortName(IReadOnlyDictionary<string, string> tags)
        {
            string value;

            if (tags.TryGetValue("ALBUMARTISTSORT", out value) ||          // Vorbis
                tags.TryGetValue("sort_album_artist", out value) ||        // ffmpeg (MP4)
                tags.TryGetValue("WM/AlbumArtistSortOrder", out value) ||  // WMA
                tags.TryGetValue("TSO2", out value))                       // ID3
            {
                return value;
            }

            return null;
        }

        private static string GetAlbumTitle(IReadOnlyDictionary<string, string> tags)
        {
            string value;

            if (tags.TryGetValue("ALBUM", out value) ||  // Vorbis
                tags.TryGetValue("album", out value))    // ffmpeg (ID3, MP4, WMA)
            {
                return value;
            }

            return null;
        }

        private static string GetAlbumSortTitle(IReadOnlyDictionary<string, string> tags)
        {
            string value;

            if (tags.TryGetValue("ALBUMSORT", out value) ||        // Vorbis
                tags.TryGetValue("sort_album", out value) ||       // ffmpeg (MP4)
                tags.TryGetValue("album-sort", out value) ||       // ffmpeg (ID3)
                tags.TryGetValue("WM/AlbumSortOrder", out value))  // WMA
            {
                return value;
            }

            return null;
        }

        private static int? GetDiscNumber(IReadOnlyDictionary<string, string> tags)
        {
            string value;

            if (tags.TryGetValue("disc", out value))  // ffmpeg (Vorbis, ID3, MP4, WMA)
            {
                if (TryParseIntFraction(value, out int numerator, out int? _) && numerator > 0)
                    return numerator;

                Debug.WriteLine("Failed to parse disc number '{0}'.", new[] { value });
            }

            return null;
        }

        private static int? GetTrackNumber(IReadOnlyDictionary<string, string> tags)
        {
            string value;

            if (tags.TryGetValue("track", out value))  // ffmpeg (Vorbis, ID3, MP4, WMA)
            {
                if (TryParseIntFraction(value, out int numerator, out int? _) && numerator > 0)
                    return numerator;

                Debug.WriteLine("Failed to parse track number '{0}'.", new[] { value });
            }

            return null;
        }

        private static string GetTrackTitle(IReadOnlyDictionary<string, string> tags)
        {
            string value;

            if (tags.TryGetValue("TITLE", out value) ||  // Vorbis
                tags.TryGetValue("title", out value))    // ffmpeg (ID3, MP4, WMA)
            {
                return value;
            }

            return null;
        }

        private static string GetTrackSortTitle(IReadOnlyDictionary<string, string> tags)
        {
            string value;

            if (tags.TryGetValue("TITLESORT", out value) ||        // Vorbis
                tags.TryGetValue("sort_name", out value) ||        // ffmpeg (MP4)
                tags.TryGetValue("title-sort", out value) ||       // ffmpeg (ID3)
                tags.TryGetValue("WM/TitleSortOrder", out value))  // WMA
            {
                return value;
            }

            return null;
        }

        private static int? GetDate(IReadOnlyDictionary<string, string> tags)
        {
            string value;

            if (tags.TryGetValue("DATE", out value) ||   // Vorbis
                tags.TryGetValue("date", out value) ||   // ffmepg (ID3, MP4)
                tags.TryGetValue("WM/Year", out value))  // WMA
            {
                if (TryParseDate(value, out int year, out int month, out int day))
                    return (year * 10000) + (month * 100) + day;

                Debug.WriteLine("Failed to parse date '{0}'.", new[] { value });
            }

            return null;
        }

        private static int? GetOriginalDate(IReadOnlyDictionary<string, string> tags)
        {
            string value;

            if (tags.TryGetValue("ORIGINALDATE", out value) ||            // Vorbis
                tags.TryGetValue("ORIGINALYEAR", out value) ||            // Vorbis
                tags.TryGetValue("originalyear", out value) ||            // ffmepg (ID3)
                tags.TryGetValue("WM/OriginalReleaseTime", out value) ||  // WMA
                tags.TryGetValue("WM/OriginalReleaseYear", out value) ||  // WMA
                tags.TryGetValue("TDOR", out value) ||                    // ID3v2.4
                tags.TryGetValue("TORY", out value))                      // ID3v2.3
            {
                if (TryParseDate(value, out int year, out int month, out int day))
                    return (year * 10000) + (month * 100) + day;

                Debug.WriteLine("Failed to parse date '{0}'.", new[] { value });
            }

            return null;
        }

        private static string[] GetGenreNames(IReadOnlyDictionary<string, string> tags)
        {
            string value;

            if (tags.TryGetValue("GENRE", out value) ||  // Vorbis
                tags.TryGetValue("genre", out value))    // ffmpeg (ID3, MP4, WMA)
            {
                return value.Split(';', StringSplitOptions.RemoveEmptyEntries);
            }

            return Array.Empty<string>();
        }

        private static float? GetAlbumGain(IReadOnlyDictionary<string, string> tags)
        {
            string value;

            if (tags.TryGetValue("R128_ALBUM_GAIN", out value))  // Opus
            {
                if (TryParseShort(value, out short gain))
                    return gain / 256.0f;

                Debug.WriteLine("Failed to parse album R128 gain '{0}'.", new[] { value });
            }
            if (tags.TryGetValue("REPLAYGAIN_ALBUM_GAIN", out value) ||  // Vorbis
                tags.TryGetValue("replaygain_album_gain", out value))    // ID3, MP4, WMA
            {
                if (TryParseDecibelValue(value, out float gain))
                    return gain;

                Debug.WriteLine("Failed to parse album ReplayGain '{0}'.", new[] { value });
            }

            return null;
        }

        private static float? GetTrackGain(IReadOnlyDictionary<string, string> tags)
        {
            string value;

            if (tags.TryGetValue("R128_TRACK_GAIN", out value))  // Opus
            {
                if (TryParseShort(value, out short gain))
                    return gain / 256.0f + 5;  // R128 reference level is 5 dB lower than ReplayGain.

                Debug.WriteLine("Failed to parse track R128 gain '{0}'.", new[] { value });
            }
            if (tags.TryGetValue("REPLAYGAIN_TRACK_GAIN", out value) ||  // Vorbis
                tags.TryGetValue("replaygain_track_gain", out value))    // ID3, MP4, WMA
            {
                if (TryParseDecibelValue(value, out float gain))
                    return gain;

                Debug.WriteLine("Failed to parse track ReplayGain '{0}'.", new[] { value });
            }

            return null;
        }

        private static bool TryParseShort(string s, out short value)
        {
            return short.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryParseInt(string s, out int value)
        {
            return int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryParseInt(string s, out int? value)
        {
            if (int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int tempValue))
            {
                value = tempValue;
                return true;
            }

            value = default;
            return false;
        }

        private static bool TryParseIntFraction(string s, out int numerator, out int? denominator)
        {
            if (s.Contains('/', StringComparison.Ordinal))
            {
                string[] fields = s.Split('/', 2);
                if (TryParseInt(fields[0], out numerator) &&
                    TryParseInt(fields[1], out denominator))
                {
                    return true;
                }
            }
            else if (TryParseInt(s, out numerator))
            {
                denominator = null;
                return true;
            }

            numerator = default;
            denominator = default;
            return false;
        }

        private static bool TryParseDate(string s, out int year, out int month, out int day)
        {
            if (s.Contains('-', StringComparison.Ordinal))
            {
                // Strip time if specified.
                int indexOfT = s.IndexOf('T', StringComparison.Ordinal);
                if (indexOfT != -1)
                    s = s.Substring(0, indexOfT);

                string[] fields = s.Split('-', 3);

                if (TryParseInt(fields[0], out year))
                {
                    if (TryParseInt(fields[1], out month) && month >= 1 && month <= 12)
                    {
                        if (fields.Length == 2)
                        {
                            day = 0;
                            return true;
                        }

                        if (TryParseInt(fields[2], out day) && day >= 1 && month <= 31)
                            return true;
                    }
                }
            }
            else
            {
                if (TryParseInt(s, out year))
                {
                    month = 0;
                    day = 0;
                    return true;
                }
            }

            year = default;
            month = default;
            day = default;
            return false;
        }

        private static bool TryParseDecibelValue(ReadOnlySpan<char> span, out float value)
        {
            if (span.EndsWith(" dB", StringComparison.Ordinal))
                return float.TryParse(span.Slice(0, span.Length - 3), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);

            value = default;
            return false;
        }

        private static async Task<int> GetOrAddArtistAsync(MediaInfoContext dbContext, string artistName, CancellationToken cancellationToken)
        {
            int? artistId = await dbContext.Artists
                .Where(a => (a.Name == null && artistName == null) || a.Name == artistName)
                .Select(a => a.ArtistId as int?)
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false) ??
                    dbContext.ChangeTracker.Entries<Artist>()
                        .Where(e => e.State == EntityState.Added)
                        .Select(e => e.Entity)
                        .Where(a => (a.Name == null && artistName == null) || a.Name == artistName)
                        .Select(a => a.ArtistId as int?)
                        .SingleOrDefault();
            if (artistId == null)
            {
                var artist = new Artist()
                {
                    Name = artistName,
                    Added = DateTime.UtcNow,
                };
                await dbContext.Artists.AddAsync(artist, cancellationToken).ConfigureAwait(false);
                artistId = artist.ArtistId;
            }
            return artistId.Value;
        }

        private static async Task<int> GetOrAddAlbumAsync(MediaInfoContext dbContext, int? artistId, string albumTitle, int? date, CancellationToken cancellationToken)
        {
            if (albumTitle == null)
                date = null;

            int? albumId = await dbContext.Albums
                .Where(a => (!a.ArtistId.HasValue && !artistId.HasValue) || a.ArtistId == artistId)
                .Where(a => (a.Title == null && albumTitle == null) || a.Title == albumTitle)
                .Where(a => (!a.Date.HasValue && !date.HasValue) || a.Date / 10000 == date / 10000)
                .Select(a => a.AlbumId as int?)
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false) ??
                    dbContext.ChangeTracker.Entries<Album>()
                        .Where(e => e.State == EntityState.Added)
                        .Select(e => e.Entity)
                        .Where(a => (!a.ArtistId.HasValue && !artistId.HasValue) || a.ArtistId == artistId)
                        .Where(a => (a.Title == null && albumTitle == null) || a.Title == albumTitle)
                        .Where(a => (!a.Date.HasValue && !date.HasValue) || a.Date / 10000 == date / 10000)
                        .Select(a => a.AlbumId as int?)
                        .SingleOrDefault();
            if (albumId == null)
            {
                var album = new Album()
                {
                    ArtistId = artistId,
                    Title = albumTitle,
                    Date = date,
                    Added = DateTime.UtcNow,
                };
                await dbContext.Albums.AddAsync(album, cancellationToken).ConfigureAwait(false);
                albumId = album.AlbumId;
            }
            return albumId.Value;
        }

        private static async Task<int> GetOrAddGenreAsync(MediaInfoContext dbContext, string genreName, CancellationToken cancellationToken)
        {
            int? genreId = await dbContext.Genres
                .Where(g => g.Name == genreName)
                .Select(g => g.GenreId as int?)
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false) ??
                    dbContext.ChangeTracker.Entries<Genre>()
                        .Where(e => e.State == EntityState.Added)
                        .Select(e => e.Entity)
                        .Where(g => g.Name == genreName)
                        .Select(g => g.GenreId as int?)
                        .SingleOrDefault();
            if (genreId == null)
            {
                var genre = new Genre()
                {
                    Name = genreName,
                };
                await dbContext.Genres.AddAsync(genre, cancellationToken).ConfigureAwait(false);
                genreId = genre.GenreId;
            }
            return genreId.Value;
        }

        private static void InvalidateArtist(MediaInfoContext dbContext, int artistId)
        {
            var artist = dbContext.ChangeTracker.Entries<Artist>()
                .Select(e => e.Entity)
                .Where(a => a.ArtistId == artistId)
                .SingleOrDefault();
            if (artist == null)
            {
                artist = new Artist
                {
                    ArtistId = artistId,
                };
                dbContext.Artists.Attach(artist);
            }
            artist.Dirty = true;
        }

        private static void InvalidateAlbum(MediaInfoContext dbContext, int albumId)
        {
            var album = dbContext.ChangeTracker.Entries<Album>()
                .Select(e => e.Entity)
                .Where(a => a.AlbumId == albumId)
                .SingleOrDefault();
            if (album == null)
            {
                album = new Album
                {
                    AlbumId = albumId,
                };
                dbContext.Albums.Attach(album);
            }
            album.Dirty = true;
        }
    }
}
