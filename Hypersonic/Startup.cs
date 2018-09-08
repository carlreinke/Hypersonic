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
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Hypersonic
{
    internal sealed class Startup : IStartup
    {
        IServiceProvider IStartup.ConfigureServices(IServiceCollection services)
        {
            services.AddResponseCompression(options =>
            {
                options.EnableForHttps = true;
            });

            return services.BuildServiceProvider();
        }

        void IStartup.Configure(IApplicationBuilder app)
        {
            app.UseResponseCompression();

#if DEBUG
            app.Use((context, next) =>
            {
                Console.WriteLine(new System.Text.StringBuilder()
                    .Append(new System.Net.IPEndPoint(context.Connection.RemoteIpAddress, context.Connection.RemotePort))
                    .Append(" ")
                    .Append(context.Request.PathBase)
                    .Append(context.Request.Path)
                    .Append(context.Request.QueryString)
                    .ToString());

                return next();
            });
#endif

            app.Map("/rest", a =>
            {
                a.UseStatusCodePages();

#if DEBUG
                a.UseDeveloperExceptionPage();
#else
                a.UseExceptionHandler(HandleException);
#endif

                a.UsePathBase("/rest");

                a.Run(RestApi.HandleRestApiRequestAsync);
            });

            app.UseStatusCodePages();

            var uiPath = Path.Combine(AppContext.BaseDirectory, "ui");
            if (Directory.Exists(uiPath))
            {
                var sharedOptions = new SharedOptions()
                {
                    FileProvider = new PhysicalFileProvider(uiPath),
                };
                app.UseDefaultFiles(new DefaultFilesOptions(sharedOptions)
                {
                    DefaultFileNames = new[] { "index.html" },
                });
                app.UseStaticFiles(new StaticFileOptions(sharedOptions));
            }
        }

        private static void HandleException(IApplicationBuilder app)
        {
            app.Run(context =>
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                return Task.CompletedTask;
            });
        }
    }
}
