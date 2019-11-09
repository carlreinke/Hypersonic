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
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Hypersonic.Ffmpeg
{
    internal sealed class FfmpegProcess : IDisposable
    {
        private readonly Process _process;
        private readonly Stream _processInputStream;
        private readonly Stream _processOutputStream;

        private bool _disposed;
        private bool _outputStreamAccessed;
        private bool _inputStreamAccessed;

        public FfmpegProcess(string executable, IEnumerable<string> arguments)
        {
            if (executable == null)
                throw new ArgumentNullException(nameof(executable));
            if (arguments == null)
                throw new ArgumentNullException(nameof(arguments));

#if DEBUG
            var builder = new System.Text.StringBuilder();
            builder.Append('\'').Append(Escape(executable)).Append('\'');
            foreach (string argument in arguments)
                builder.Append(' ').Append('\'').Append(Escape(argument)).Append('\'');
            Debug.WriteLine("Starting {0}...", new[] { builder.ToString() });

            string Escape(string s)
            {
                return s
                    .Replace(@"\", @"\\", StringComparison.Ordinal)
                    .Replace(@"'", @"\'", StringComparison.Ordinal);
            }
#endif

            _process = new Process();
            _process.StartInfo.CreateNoWindow = true;
            _process.StartInfo.UseShellExecute = false;
            _process.StartInfo.FileName = executable;
            foreach (string argument in arguments)
                _process.StartInfo.ArgumentList.Add(argument);
            _process.StartInfo.RedirectStandardInput = true;
            _process.StartInfo.RedirectStandardOutput = true;
            _process.Start();

            _processInputStream = _process.StandardInput.BaseStream;
            _processOutputStream = _process.StandardOutput.BaseStream;
        }

        ~FfmpegProcess()
        {
            Dispose(false);
        }

        public Stream InputStream
        {
            get
            {
                _inputStreamAccessed = true;

                return _processInputStream;
            }
        }

        public Stream OutputStream
        {
            get
            {
                _outputStreamAccessed = true;

                return _processOutputStream;
            }
        }

        public void Dispose()
        {
            Dispose(true);

            GC.SuppressFinalize(this);
        }

        public void Abort()
        {
            try
            {
                try
                {
                    if (!_process.HasExited)
                        _process.CloseMainWindow();
                }
                catch (PlatformNotSupportedException)
                {
                    // Don't care.
                }
                try
                {
                    _process.WaitForExit(1000);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Exception thrown while waiting for {0}: {1}", _process.StartInfo.FileName, ex);
                }
                try
                {
                    if (!_process.HasExited)
                        _process.Kill();
                }
                catch (NotSupportedException)
                {
                    // Don't care.
                }
            }
            catch (InvalidOperationException)
            {
                // Process is already dead.
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Exception thrown while killing {0}: {1}", _process.StartInfo.FileName, ex);
            }
        }

        private void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (_process != null)
                Abort();

            if (disposing)
            {
                if (!_outputStreamAccessed)
                    _processOutputStream.Dispose();

                if (!_inputStreamAccessed)
                    _processInputStream.Dispose();

                try
                {
                    _process.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Exception thrown while disposing process: {0}", ex);
                }
            }

            _disposed = true;
        }
    }
}
