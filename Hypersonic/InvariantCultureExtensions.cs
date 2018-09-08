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
using System.Globalization;

namespace Hypersonic
{
    internal static class InvariantCultureExtensions
    {
        public static string ToStringInvariant(this int value) => value.ToString(CultureInfo.InvariantCulture);

        public static string ToStringInvariant(this int value, string format) => value.ToString(format, CultureInfo.InvariantCulture);

        public static string ToStringInvariant(this long value) => value.ToString(CultureInfo.InvariantCulture);

        public static string ToStringInvariant(this long value, string format) => value.ToString(format, CultureInfo.InvariantCulture);

        public static string ToStringInvariant(this float value, string format) => value.ToString(format, CultureInfo.InvariantCulture);

        public static string ToStringInvariant(this float value) => value.ToString(CultureInfo.InvariantCulture);

        public static string ToStringInvariant(this double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);

        public static string ToStringInvariant(this double value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
