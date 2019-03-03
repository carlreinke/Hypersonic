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
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Hypersonic.Ffmpeg
{
    internal static class FfmpegStreamHasher
    {
        public static byte[] Hash(string filePath, int streamIndex, string hashAlgorithm = "sha256")
        {
            using (var hashStream = CreateHashStream(filePath, streamIndex, hashAlgorithm))
            {
                hashStream.InputStream.Close();

                try
                {
                    return ReadHash(hashStream);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Exception thrown while reading from ffmpeg: {0}", ex);

                    return null;
                }
            }
        }

        public static Task<byte[]> HashAsync(string filePath, int streamIndex, string hashAlgorithm = "sha256", CancellationToken cancellationToken = default)
        {
            using (var hashStream = CreateHashStream(filePath, streamIndex, hashAlgorithm))
            {
                hashStream.InputStream.Close();

                try
                {
                    return ReadHashAsync(hashStream, cancellationToken);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Exception thrown while reading from ffmpeg: {0}", ex);

                    return null;
                }
            }
        }

        private static FfmpegStream CreateHashStream(string filePath, int streamIndex, string hashAlgorithm)
        {
            if (hashAlgorithm == null)
                throw new ArgumentNullException(nameof(hashAlgorithm));

            var arguments = new ArgumentList()
                .Add("-i").Add(filePath)
                .Add("-map").Add(b => b.Append("0:").Append(streamIndex.ToStringInvariant()))
                .Add("-c").Add("copy")
                .Add("-f").Add("hash")
                .Add("-hash").Add(hashAlgorithm)
                .Add("-");

            return FfmpegTranscoder.Transcode(arguments);
        }

        private static byte[] ReadHash(Stream hashStream)
        {
            using (var reader = new BufferedByteReader(hashStream))
            {
                for (; ; )
                {
                    int b = reader.ReadByte();
                    if (b == -1 || b == '\r' || b == '\n')
                        throw new InvalidDataException("Hash value is missing.");

                    if (b == '=')
                        break;
                }

                byte[] bytes = ArrayPool<byte>.Shared.Rent(1024 / 8);
                try
                {
                    for (int i = 0; ; ++i)
                    {
                        int hexDigit = reader.ReadByte();
                        if (hexDigit == -1 || hexDigit == '\r' || hexDigit == '\n')
                            return bytes.AsSpan(0, i).ToArray();

                        byte upperNibble = HexDigitToNibble(hexDigit);

                        if (i == bytes.Length)
                            throw new InvalidDataException("Hash value is too long.");

                        hexDigit = reader.ReadByte();
                        if (hexDigit == -1 || hexDigit == '\r' || hexDigit == '\n')
                            throw new InvalidDataException("Hash value has an odd number of hexadecimal digits.");

                        byte lowerNibble = HexDigitToNibble(hexDigit);

                        bytes[i] = (byte)((upperNibble << 4) | lowerNibble);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(bytes);
                }
            }
        }

        private static async Task<byte[]> ReadHashAsync(Stream hashStream, CancellationToken cancellationToken)
        {
            using (var reader = new BufferedByteReader(hashStream))
            {
                for (; ; )
                {
                    int b = await reader.ReadByteAsync(cancellationToken).ConfigureAwait(false);
                    if (b == -1 || b == '\r' || b == '\n')
                        throw new InvalidDataException("Hash value is missing.");

                    if (b == '=')
                        break;
                }

                byte[] bytes = ArrayPool<byte>.Shared.Rent(1024 / 8);
                try
                {
                    for (int i = 0; ; ++i)
                    {
                        int hexDigit = await reader.ReadByteAsync(cancellationToken).ConfigureAwait(false);
                        if (hexDigit == -1 || hexDigit == '\r' || hexDigit == '\n')
                            return bytes.AsSpan(0, i).ToArray();

                        byte upperNibble = HexDigitToNibble(hexDigit);

                        if (i == bytes.Length)
                            throw new InvalidDataException("Hash value is too long.");

                        hexDigit = await reader.ReadByteAsync(cancellationToken).ConfigureAwait(false);
                        if (hexDigit == -1 || hexDigit == '\r' || hexDigit == '\n')
                            throw new InvalidDataException("Hash value has an odd number of hexadecimal digits.");

                        byte lowerNibble = HexDigitToNibble(hexDigit);

                        bytes[i] = (byte)((upperNibble << 4) | lowerNibble);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(bytes);
                }
            }
        }

        private static byte HexDigitToNibble(int digit)
        {
            if (digit >= '0')
            {
                if (digit <= '9')
                    return (byte)(digit - '0');

                if (digit >= 'A')
                {
                    if (digit <= 'F')
                        return (byte)(digit - 'A' + 10);

                    if (digit >= 'a')
                    {
                        if (digit <= 'f')
                            return (byte)(digit - 'a' + 10);
                    }
                }
            }

            throw new InvalidDataException("Hash value has invalid hexadecimal digit.");
        }

        private sealed class BufferedByteReader : IDisposable
        {
            private readonly Stream _stream;
            private byte[] _buffer;
            private int _front;
            private int _count;

            public BufferedByteReader(Stream stream)
            {
                _stream = stream;
                _buffer = ArrayPool<byte>.Shared.Rent(128);
            }

            public void Dispose()
            {
                _stream.Dispose();

                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = null;
                _count = 0;
            }

            public int ReadByte()
            {
                if (_count == 0)
                {
                    _front = 0;
                    _count = _stream.Read(_buffer.AsSpan());

                    if (_count == 0)
                        return -1;
                }

                byte b = _buffer[_front];
                _front += 1;
                _count -= 1;
                return b;
            }

            public async Task<int> ReadByteAsync(CancellationToken cancellationToken)
            {
                if (_count == 0)
                {
                    _front = 0;
                    _count = await _stream.ReadAsync(_buffer, cancellationToken);

                    if (_count == 0)
                        return -1;
                }

                byte b = _buffer[_front];
                _front += 1;
                _count -= 1;
                return b;
            }
        }
    }
}
