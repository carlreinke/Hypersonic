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
namespace Hypersonic.Ffmpeg
{
    internal static class Helpers
    {
        private static readonly (int BitRate, int Quality)[] _libmp3lameQuality = new (int, int)[]
        {
            (245_000, 0),
            (225_000, 1),
            (190_000, 2),
            (175_000, 3),
            (165_000, 4),
            (130_000, 5),
            (115_000, 6),
            (100_000, 7),
            ( 85_000, 8),
            ( 65_000, 9),
        };

        private static readonly (int BitRate, int Quality)[] _libvorbisQuality =
        {
            (500_000, 10),
            (320_000,  9),
            (256_000,  8),
            (224_000,  7),
            (192_000,  6),
            (160_000,  5),
            (128_000,  4),
            (112_000,  3),
            ( 96_000,  2),
            ( 80_000,  1),
            ( 64_000,  0),
        };

        public static int GetLibmp3lameQuality(int bitRate)
        {
            foreach (var item in _libmp3lameQuality)
                if (bitRate >= item.BitRate)
                    return item.Quality;
            return 9;
        }

        public static float GetLibvorbisQuality(int bitRate)
        {
            foreach (var item in _libvorbisQuality)
                if (bitRate >= item.BitRate)
                    return item.Quality;
            return 0;
        }
    }
}
