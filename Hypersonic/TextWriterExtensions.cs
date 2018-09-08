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
using System.IO;

namespace Hypersonic
{
    internal static class TextWriterExtensions
    {
        public static void WriteLeftPadded(this TextWriter writer, string s, int length)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length));

            var sLength = s?.Length ?? 0;
            if (length > sLength)
                writer.Write(new string(' ', length - sLength));
            writer.Write(s);
        }

        public static void WriteRightPadded(this TextWriter writer, string s, int length)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length));

            writer.Write(s);
            var sLength = s?.Length ?? 0;
            if (length > sLength)
                writer.Write(new string(' ', length - sLength));
        }
    }
}
