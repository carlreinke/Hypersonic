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
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace Hypersonic.Ffmpeg
{
    internal static class FfmpegProber
    {
        private static readonly XmlSerializer _serializer = new XmlSerializer(typeof(ffprobeType));

        public static ffprobeType Probe(string path, Show show)
        {
            using (var probeStream = CreateProbeStream(path, show))
            {
                probeStream.InputStream.Close();

                return ReadProbeResult(probeStream);
            }
        }

        private static FfmpegStream CreateProbeStream(string path, Show show)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));

            if ((show & ~Show.All) != 0)
                throw new ArgumentException("Invalid flag.", nameof(show));

            var arguments = new ArgumentList()
                .Add("-v").Add("fatal");

            arguments
                .Add("-print_format").Add("xml=xsd_strict=1")
                .Add("-noshow_private_data");

            if (show.HasFlag(Show.Data))
                arguments.Add("-show_data");
            if (show.HasFlag(Show.Error))
                arguments.Add("-show_error");
            if (show.HasFlag(Show.Format))
                arguments.Add("-show_format");
            if (show.HasFlag(Show.Packets))
                arguments.Add("-show_packets");
            if (show.HasFlag(Show.Frames))
                arguments.Add("-show_frames");
            if (show.HasFlag(Show.Streams))
                arguments.Add("-show_streams");
            if (show.HasFlag(Show.Programs))
                arguments.Add("-show_programs");
            if (show.HasFlag(Show.Chapters))
                arguments.Add("-show_chapters");

            arguments
                .Add(path);

            var x = new FfmpegStream("ffprobe", arguments);
            return x;
        }

        private static ffprobeType ReadProbeResult(FfmpegStream probeStream)
        {
            using (var textReader = new StreamReader(probeStream, Encoding.UTF8))
            using (var reader = new XmlTextReader(textReader))
            {
                try
                {
                    return (ffprobeType)_serializer.Deserialize(reader);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Exception thrown while reading from ffprobe: {0}", ex);

                    return null;
                }
            }
        }

        [Flags]
        internal enum Show
        {
            None = 0,
            Data = 1,
            Error = 2,
            Format = 4,
            Packets = 8,
            Frames = 16,
            Streams = 32,
            Programs = 64,
            Chapters = 128,
            All = Data | Error | Format | Packets | Frames | Streams | Programs | Chapters,
        }
    }
}
