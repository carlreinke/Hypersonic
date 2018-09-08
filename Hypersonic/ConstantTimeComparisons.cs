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
using System.Runtime.CompilerServices;

namespace Hypersonic
{
    internal static class ConstantTimeComparisons
    {
        /// <summary>
        /// Performs a comparison with a time complexity that does not reveal information about a
        /// secret.
        /// </summary>
        /// <param name="untrusted">The untrusted data to compare against the secret.</param>
        /// <param name="secret">The secret.</param>
        /// <returns><see langword="true"/> if <paramref name="untrusted"/> and
        ///     <paramref name="secret"/> are equal; otherwise, <see langword="false"/>.</returns>
        /// <remarks>
        /// Timing analysis of this method reveals the length of <paramref name="untrusted"/>.  Do
        /// not provide a secret in <paramref name="untrusted"/>.
        /// </remarks>
        [MethodImpl(MethodImplOptions.NoOptimization)]
        public static bool ConstantTimeEquals(ReadOnlySpan<byte> untrusted, ReadOnlySpan<byte> secret) => ConstantTimeMismatch(untrusted, secret) == 0;

        /// <summary>
        /// Performs a comparison with a time complexity that does not reveal information about a
        /// secret.
        /// </summary>
        /// <param name="untrusted">The untrusted data to compare against the secret.</param>
        /// <param name="secret">The secret.</param>
        /// <returns><see langword="true"/> if <paramref name="untrusted"/> and
        ///     <paramref name="secret"/> are equal; otherwise, <see langword="false"/>.</returns>
        /// <remarks>
        /// Timing analysis of this method reveals the length of <paramref name="untrusted"/>.  Do
        /// not provide a secret in <paramref name="untrusted"/>.
        /// </remarks>
        [MethodImpl(MethodImplOptions.NoOptimization)]
        public static bool ConstantTimeEquals(ReadOnlySpan<char> untrusted, ReadOnlySpan<char> secret) => ConstantTimeMismatch(untrusted, secret) == 0;

        [MethodImpl(MethodImplOptions.NoOptimization)]
        private static byte ConstantTimeMismatch(ReadOnlySpan<byte> untrusted, ReadOnlySpan<byte> secret)
        {
            int minLength = untrusted.Length < secret.Length ? untrusted.Length : secret.Length;

            byte mismatch = untrusted.Length != secret.Length ? (byte)1 : (byte)0;

            ReadOnlySpan<byte> nonempty = stackalloc byte[1];
            ReadOnlySpan<byte> nonemptySecret = secret.Length > 0 ? secret : nonempty;

            int i = 0;
            int j = 0;

            for (; i < minLength; i += 1, j += 1)
                mismatch |= (byte)(untrusted[i] ^ nonemptySecret[j]);

            j = nonemptySecret.Length - 1;

            for (; i < untrusted.Length; i += 1, j += 0)
                mismatch |= (byte)(untrusted[i] ^ nonemptySecret[j]);

            return mismatch;
        }

        [MethodImpl(MethodImplOptions.NoOptimization)]
        private static char ConstantTimeMismatch(ReadOnlySpan<char> untrusted, ReadOnlySpan<char> secret)
        {
            int minLength = untrusted.Length < secret.Length ? untrusted.Length : secret.Length;

            char mismatch = untrusted.Length != secret.Length ? (char)1 : (char)0;

            ReadOnlySpan<char> nonempty = stackalloc char[1];
            ReadOnlySpan<char> nonemptySecret = secret.Length > 0 ? secret : nonempty;

            int i = 0;
            int j = 0;

            for (; i < minLength; i += 1, j += 1)
                mismatch |= (char)(untrusted[i] ^ nonemptySecret[j]);

            j = nonemptySecret.Length - 1;

            for (; i < untrusted.Length; i += 1, j += 0)
                mismatch |= (char)(untrusted[i] ^ nonemptySecret[j]);

            return mismatch;
        }
    }
}
