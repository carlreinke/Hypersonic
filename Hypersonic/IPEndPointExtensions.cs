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
using System.Globalization;
using System.Net;

namespace Hypersonic
{
    internal static class IPEndPointExtensions
    {
        public static bool TryParse(string s, out IPEndPoint endPoint)
        {
            if (s == null)
                throw new ArgumentNullException(nameof(s));

            if (s.Length > "[ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff]:65535".Length)
                goto fail;

            if (s.StartsWith("[", StringComparison.Ordinal))
            {
                int splitIndex = s.LastIndexOf("]:", StringComparison.Ordinal);
                if (splitIndex == -1)
                    goto fail;
                IPAddress address;
                ushort port;
                if (ushort.TryParse(s.AsSpan(splitIndex + 2), NumberStyles.None, CultureInfo.InvariantCulture, out port) &&
                    IPAddress.TryParse(s.Substring(1, splitIndex - 1), out address))
                {
                    endPoint = new IPEndPoint(address, port);
                    return true;
                }
            }
            else
            {
                int splitIndex = s.IndexOf(':', StringComparison.Ordinal);
                IPAddress address;
                ushort port;
                if (ushort.TryParse(s.AsSpan(splitIndex + 1), NumberStyles.None, CultureInfo.InvariantCulture, out port) &&
                    IPAddress.TryParse(s.Substring(0, splitIndex), out address))
                {
                    endPoint = new IPEndPoint(address, port);
                    return true;
                }
            }

        fail:
            endPoint = default;
            return false;
        }

    }
}
