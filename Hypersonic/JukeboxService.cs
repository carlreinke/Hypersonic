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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace Hypersonic
{
    internal class JukeboxService : IDisposable
    {
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        private readonly AutoResetEvent _playEvent = new AutoResetEvent(false);

        private readonly object _lock = new object();

        private readonly IServiceScopeFactory _serviceScopeFactory;

        private Thread _thread;

        private bool _disposed;

        private bool _playing;

        private readonly List<int> _playlist = new List<int>();

        private int _playlistIndex = -1;

        private int _trackPosition = 0;

        private float _gain = 0.5f;

        private float _gainScale = CalculateGainScale(0.5f);

        public JukeboxService(IServiceScopeFactory serviceScopeFactory, IApplicationLifetime lifetime)
        {
            if (serviceScopeFactory == null)
                throw new ArgumentNullException(nameof(serviceScopeFactory));

            _serviceScopeFactory = serviceScopeFactory;

            lifetime.ApplicationStarted.Register(OnStarted);
            lifetime.ApplicationStopping.Register(OnStopping);
        }

        ~JukeboxService()
        {
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            Dispose(disposing: true);

            GC.SuppressFinalize(this);
        }

        public void StartPlayback()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(JukeboxService));

            lock (_lock)
            {
                _playing = true;

                _playEvent.Set();
            }
        }

        public void PausePlayback()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(JukeboxService));

            lock (_lock)
            {
                _playing = false;
            }
        }

        public void StopPlayback()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(JukeboxService));

            lock (_lock)
            {
                _playing = false;
                _playlistIndex = -1;
                _trackPosition = 0;
            }
        }

        public void SkipToTrack(int playlistIndex, int trackPosition)
        {
            if (playlistIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(playlistIndex));
            if (trackPosition < 0)
                throw new ArgumentOutOfRangeException(nameof(trackPosition));

            lock (_lock)
            {
                if (playlistIndex >= _playlist.Count)
                    return;

                _playlistIndex = playlistIndex;
                _trackPosition = trackPosition;
            }
        }

        public void SetTracks(IEnumerable<int> trackIds)
        {
            if (trackIds == null)
                throw new ArgumentNullException(nameof(trackIds));

            lock (_lock)
            {
                if (_playlistIndex != -1)
                {
                    // Attempt to preserve the current track.
                    int trackId = _playlist[_playlistIndex];

                    _playlist.Clear();
                    _playlist.AddRange(trackIds);

                    _playlistIndex = NearestIndexOf(_playlist, trackId, _playlistIndex);

                    // If the current track was removed then stop playing.
                    if (_playlistIndex == -1)
                    {
                        _playing = false;
                        _trackPosition = 0;
                    }
                }
                else
                {
                    _playlist.Clear();
                    _playlist.AddRange(trackIds);
                }
            }
        }

        public void AddTracks(IEnumerable<int> trackIds)
        {
            if (trackIds == null)
                throw new ArgumentNullException(nameof(trackIds));

            lock (_lock)
            {
                _playlist.AddRange(trackIds);
            }
        }

        public void RemoveTrack(int playlistIndex)
        {
            if (playlistIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(playlistIndex));

            lock (_lock)
            {
                if (playlistIndex >= _playlist.Count)
                    return;

                _playlist.RemoveAt(_playlistIndex);

                if (_playlistIndex == playlistIndex)
                {
                    // Removed the current track.
                    _playing = false;
                    _playlistIndex = -1;
                    _trackPosition = 0;
                }
                else if (_playlistIndex > playlistIndex)
                {
                    // Removed a track before the current track.
                    _playlistIndex -= 1;
                }
            }
        }

        public void ClearTracks()
        {
            lock (_lock)
            {
                _playlist.Clear();

                _playing = false;
                _playlistIndex = -1;
                _trackPosition = 0;
            }
        }

        public void ShuffleTracks()
        {
            var random = new Random();

            lock (_lock)
            {
                for (int i = _playlist.Count - 1; i > 0; --i)
                {
                    int j = random.Next(i + 1);

                    int temp = _playlist[i];
                    _playlist[i] = _playlist[j];
                    _playlist[j] = temp;

                    if (_playlistIndex == i)
                        _playlistIndex = j;
                    else if (_playlistIndex == j)
                        _playlistIndex = i;
                }
            }
        }

        public void SetGain(float gain)
        {
            lock (_lock)
            {
                _gain = gain;
                _gainScale = CalculateGainScale(gain);
            }
        }

        public JukeboxState GetState()
        {
            lock (_lock)
            {
                return new JukeboxState(
                    playing: _playing,
                    playlistIndex: _playlistIndex,
                    trackPosition: _trackPosition,
                    gain: _gain);
            }
        }

        public JukeboxState GetState(out int[] trackIds)
        {
            lock (_lock)
            {
                trackIds = _playlist.ToArray();

                return GetState();
            }
        }

        private static float CalculateGainScale(float gain)
        {
            if (!(gain >= 0 && gain <= 1))
                throw new ArgumentOutOfRangeException(nameof(gain));

            const float dynamicRange = 40;  // dB
            return gain == 0 ? 0 : (float)Math.Exp((gain - 1) * Math.Log(Math.Pow(10, dynamicRange / 20)));
        }

        private static int NearestIndexOf<T>(List<T> list, T item, int index)
        {
            if (index >= list.Count)
                return list.LastIndexOf(item);

            int after = list.IndexOf(item, index);
            int before = list.LastIndexOf(item, index);

            if (after == -1)
                return before;
            if (before == -1)
                return after;

            return after - index < index - before ? after : before;
        }

        private void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (_cancellationTokenSource != null)
                _cancellationTokenSource.Cancel();

            if (disposing)
            {
                _cancellationTokenSource.Dispose();
                _playEvent.Dispose();
            }

            _disposed = true;
        }

        private void OnStarted()
        {
            _thread = new Thread(PlaybackThreadStart)
            {
                Name = "Jukebox playback",
                Priority = ThreadPriority.AboveNormal,
            };
            _thread.Start(new object[] { _cancellationTokenSource.Token });
        }

        private void OnStopping()
        {
            _cancellationTokenSource.Cancel();

            if (_thread != null)
            {
                _thread.Join();
                _thread = null;
            }
        }

        private void PlaybackThreadStart(object obj)
        {
            var objs = (object[])obj;
            var cancellationToken = (CancellationToken)objs[0];

            int sampleRate = 48000;
            int channelCount = 2;

            string format = BitConverter.IsLittleEndian ? "f32le" : "f32be";
            string codec = "pcm_" + format;

            var outArguments = new ArgumentList()
                .Add("-v").Add("fatal")
                .Add("-autoexit")
                .Add("-nodisp")
                .Add("-ar").Add(sampleRate.ToStringInvariant())
                .Add("-ac").Add(channelCount.ToStringInvariant())
                .Add("-f").Add(format)
                .Add("-");

            using (var serviceScope = _serviceScopeFactory.CreateScope())
            {
                var dbContext = serviceScope.ServiceProvider.GetRequiredService<MediaInfoContext>();

            restartPlayer:
                // Wait for playing.
                for (; ; )
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;

                    lock (_lock)
                    {
                        if (_playing)
                            break;
                    }

                    WaitHandle.WaitAny(new[] { cancellationToken.WaitHandle, _playEvent });
                }

                using (var ffplayStream = new FfmpegStream("ffplay", outArguments))
                using (var outStream = ffplayStream.InputStream)
                {
                    var sampleBuffer = new float[sampleRate / 20 * channelCount];
                    var buffer = MemoryMarshal.AsBytes(sampleBuffer.AsSpan());

                restartTrack:
                    for (; ; )
                    {
                        if (cancellationToken.IsCancellationRequested)
                            return;

                        int playerTrackId;
                        int playerTrackPositionSamples;
                        int playerTrackPosition;

                        lock (_lock)
                        {
                            if (!_playing)
                                goto restartPlayer;

                            int trackId = _playlistIndex != -1 ? _playlist[_playlistIndex] : -1;
                            if (trackId == -1)
                                goto nextTrack;

                            playerTrackId = trackId;
                            playerTrackPositionSamples = _trackPosition * sampleRate * channelCount;
                            playerTrackPosition = _trackPosition;
                        }

                        var track = dbContext.Tracks
                            // where track is current jukebox track
                            .Where(t => t.TrackId == playerTrackId)
                            // find files for tracks
                            .Join(dbContext.Files, t => t.FileId, f => f.FileId, (t, f) => new
                            {
                                DirectoryPath = f.Directory.Path,
                                FileName = f.Name,
                                t.StreamIndex,
                                t.AlbumGain,
                                t.TrackGain,
                            })
                            .FirstOrDefault();
                        if (track == null)
                            goto nextTrack;

                        string filePath = Path.Combine(track.DirectoryPath, track.FileName);

                        var inArguments = new ArgumentList()
                            .Add("-i").Add(filePath)
                            .Add("-ss").Add(playerTrackPosition.ToStringInvariant())
                            .Add("-map").Add(b => b.Append("0:").Append(track.StreamIndex.ToStringInvariant()))
                            .Add("-c:a:0").Add(codec)
                            .Add("-ar:0").Add(sampleRate.ToStringInvariant())
                            .Add("-ac:0").Add(channelCount.ToStringInvariant())
                            .Add("-f").Add(format)
                            .Add("-");

                        using (var inStream = FfmpegTranscoder.Transcode(inArguments))
                        {
                            inStream.InputStream.Close();

                            float replayGainScale = MathF.Pow(10, (track.TrackGain ?? 0) / 20);

                            int bufferOffset = 0;
                            int bufferCount = 0;

                            for (; ; )
                            {
                                if (cancellationToken.IsCancellationRequested)
                                    return;

                                lock (_lock)
                                {
                                    if (!_playing)
                                        goto restartPlayer;

                                    int trackId = _playlistIndex != -1 ? _playlist[_playlistIndex] : -1;
                                    if (playerTrackId != trackId || playerTrackPosition != _trackPosition)
                                        goto restartTrack;

                                    playerTrackPosition = playerTrackPositionSamples / (sampleRate * channelCount);
                                    _trackPosition = playerTrackPosition;
                                }

                                try
                                {
                                    int readCount = inStream.Read(buffer.Slice(bufferOffset + bufferCount));
                                    if (readCount == 0)
                                        break;
                                    bufferCount += readCount;
                                }
                                catch (IOException ex)
                                {
                                    Debug.WriteLine("Exception thrown while reading from ffmpeg: {0}", ex);
                                    break;
                                }

                                var samplesSpan = sampleBuffer.AsSpan(bufferOffset / sizeof(float), bufferCount / sizeof(float));

                                float scale = replayGainScale * _gainScale;
                                for (int i = 0; i < samplesSpan.Length; ++i)
                                    samplesSpan[i] *= scale;

                                try
                                {
                                    int writeCount = samplesSpan.Length * sizeof(float);
                                    outStream.Write(buffer.Slice(bufferOffset, writeCount));
                                    bufferOffset += writeCount;
                                    bufferCount -= writeCount;
                                }
                                catch (IOException ex)
                                {
                                    Debug.WriteLine("Exception thrown while writing to ffplay: {0}", ex);
                                    goto restartPlayer;
                                }

                                if (bufferCount == 0)
                                    bufferOffset = 0;

                                playerTrackPositionSamples += samplesSpan.Length;
                            }
                        }

                    nextTrack:
                        lock (_lock)
                        {
                            if (_playlistIndex + 1 < _playlist.Count)
                            {
                                _playlistIndex += 1;
                                _trackPosition = 0;
                            }
                            else
                            {
                                _playing = false;
                                _playlistIndex = -1;
                                _trackPosition = 0;

                                goto restartPlayer;
                            }
                        }
                    }
                }
            }
        }

        public class JukeboxState
        {
            public JukeboxState(bool playing, int playlistIndex, int trackPosition, float gain)
            {
                Playing = playing;
                PlaylistIndex = playlistIndex;
                TrackPosition = trackPosition;
                Gain = gain;
            }

            public bool Playing { get; }

            public int PlaylistIndex { get; }

            public int TrackPosition { get; }

            public float Gain { get; }
        }
    }
}
