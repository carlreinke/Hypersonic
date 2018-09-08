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
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using System;
using System.Threading;

namespace Hypersonic
{
    internal static class IWebHostExtensions
    {
        public static void Run(this IWebHost host)
        {
            using (var cancellationTokenSource = new CancellationTokenSource())
            {
                var doneEvent = new ManualResetEventSlim();

                void Shutdown()
                {
                    try
                    {
                        cancellationTokenSource.Cancel();
                    }
                    catch (ObjectDisposedException) { }

                    doneEvent.Wait();
                }

                AppDomain.CurrentDomain.ProcessExit += (sender, eventArgs) => Shutdown();
                Console.CancelKeyPress += (sender, eventArgs) =>
                {
                    Shutdown();
                    eventArgs.Cancel = true;
                };

                try
                {
                    var cancellationToken = cancellationTokenSource.Token;

                    using (host)
                    {
                        host.StartAsync(cancellationToken).Wait();

                        var serverAddresses = host.ServerFeatures.Get<IServerAddressesFeature>()?.Addresses;
                        if (serverAddresses != null)
                            foreach (var address in serverAddresses)
                                Console.WriteLine($"Now listening on: {address}");

                        Console.WriteLine("Application started.");

                        cancellationToken.WaitHandle.WaitOne();

                        Console.WriteLine("Application is stopping...");

                        host.StopAsync().Wait();
                    }
                }
                finally
                {
                    doneEvent.Set();
                }
            }
        }
    }
}
