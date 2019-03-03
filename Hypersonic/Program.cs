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
using Hypersonic.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.CommandLineUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Debug;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using IODirectory = System.IO.Directory;
using IOPath = System.IO.Path;

namespace Hypersonic
{
    internal class Program
    {
        private const string _defaultDatabaseFileName = "hypersonic.sqlite3";

#if DEBUG
        private static readonly LoggerFactory _loggerFactory = new LoggerFactory(
            providers: new[] { new DebugLoggerProvider() },
            filterOptions: new LoggerFilterOptions() { MinLevel = LogLevel.Information, });
#endif

        private static string DefaultDatabasePath
        {
            get
            {
                string localAppDataPath;
                try
                {
                    localAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                }
                catch (PlatformNotSupportedException)
                {
                    localAppDataPath = ".";
                }
                return IOPath.Combine(localAppDataPath, _defaultDatabaseFileName);
            }
        }

        public static int Main(string[] args)
        {
            var app = new CommandLineApplication
            {
                Name = "hypersonic",
                FullName = "Hypersonic"
            };

            app.HelpOption("-h|--help");
            app.OptionHelp.Description = "Show help.";

            var databaseOption = app.Option(
                template: "--database <file>",
                description: $"Use the specified SQLite database (default: ...{IOPath.DirectorySeparatorChar}{_defaultDatabaseFileName}).",
                optionType: CommandOptionType.SingleValue,
                inherited: true);

            app.Command("scan", scanCommand =>
            {
                scanCommand.FullName = scanCommand.Parent.FullName;
                scanCommand.Description = "Scan music libraries.";

                scanCommand.HelpOption("-h|--help");
                scanCommand.OptionHelp.Description = "Show help.";

                var forceOption = scanCommand.Option(
                    template: "-f|--force",
                    description: "Also rescan files that have not changed.",
                    optionType: CommandOptionType.NoValue);

                scanCommand.OnExecute(() => Scan(
                    command: scanCommand,
                    databaseOption: databaseOption,
                    forceOption: forceOption) ?? 0);
            });

            app.Command("serve", serveCommand =>
            {
                serveCommand.FullName = serveCommand.Parent.FullName;
                serveCommand.Description = "Start web server.";

                serveCommand.HelpOption("-h|--help");
                serveCommand.OptionHelp.Description = "Show help.";

                var bindOption = serveCommand.Option(
                    template: "--bind <endpoint>",
                    description: "IP endpoint to listen on (default: [::]:4040).",
                    optionType: CommandOptionType.MultipleValue);

                var certificateOption = serveCommand.Option(
                    template: "--certificate <file>",
                    description: "Serve HTTPS using the specified certificate.",
                    optionType: CommandOptionType.SingleValue);

                serveCommand.OnExecute(() => Serve(
                    command: serveCommand,
                    databaseOption: databaseOption,
                    bindOption: bindOption,
                    certificateOption: certificateOption) ?? 0);
            });

            app.Command("library", libraryCommand =>
            {
                libraryCommand.FullName = libraryCommand.Parent.FullName;
                libraryCommand.Description = "Provides music library management.";

                libraryCommand.HelpOption("-h|--help");
                libraryCommand.OptionHelp.Description = "Show help.";

                libraryCommand.Command("list", listCommand =>
                {
                    listCommand.FullName = listCommand.Parent.FullName;
                    listCommand.Description = "List music libraries.";

                    listCommand.HelpOption("-h|--help");
                    listCommand.OptionHelp.Description = "Show help.";

                    listCommand.OnExecute(() => ListLibraries(
                        command: listCommand,
                        databaseOption: databaseOption) ?? 0);
                });

                libraryCommand.Command("add", addCommand =>
                {
                    addCommand.FullName = addCommand.Parent.FullName;
                    addCommand.Description = "Add a music library.";

                    addCommand.HelpOption("-h|--help");
                    addCommand.OptionHelp.Description = "Show help.";

                    var nameArgument = addCommand.Argument(
                        name: "name",
                        description: "Name of the music library to add.");

                    var pathArgument = addCommand.Argument(
                        name: "path",
                        description: "Path of the music library.");

                    var accessControlOption = addCommand.Option(
                        template: "--access-control",
                        description: "Enable access control on the music library.",
                        optionType: CommandOptionType.NoValue);

                    addCommand.OnExecute(() => AddLibrary(
                        command: addCommand,
                        databaseOption: databaseOption,
                        nameArgument: nameArgument,
                        pathArgument: pathArgument,
                        accessControlOption: accessControlOption) ?? 0);
                });

                libraryCommand.Command("modify", modifyCommand =>
                {
                    modifyCommand.FullName = modifyCommand.Parent.FullName;
                    modifyCommand.Description = "Modify a music library.";

                    modifyCommand.HelpOption("-h|--help");
                    modifyCommand.OptionHelp.Description = "Show help.";

                    var nameArgument = modifyCommand.Argument(
                        name: "name",
                        description: "Name of the music library to add.");

                    var accessControlOption = modifyCommand.Option(
                        template: "--access-control <yes/no>",
                        description: "Set whether to enable access control on the music library.",
                        optionType: CommandOptionType.SingleValue);

                    var renameOption = modifyCommand.Option(
                        template: "--rename <name>",
                        description: "Rename the music library.",
                        optionType: CommandOptionType.SingleValue);

                    modifyCommand.OnExecute(() => ModifyLibrary(
                        command: modifyCommand,
                        databaseOption: databaseOption,
                        nameArgument: nameArgument,
                        accessControlOption: accessControlOption,
                        renameOption: renameOption) ?? 0);
                });

                libraryCommand.Command("delete", deleteCommand =>
                {
                    deleteCommand.FullName = deleteCommand.Parent.FullName;
                    deleteCommand.Description = "Delete a music library.";

                    deleteCommand.HelpOption("-h|--help");
                    deleteCommand.OptionHelp.Description = "Show help.";

                    var nameArgument = deleteCommand.Argument(
                        name: "name",
                        description: "Name of the music library to delete.");

                    deleteCommand.OnExecute(() => DeleteLibrary(
                        command: deleteCommand,
                        databaseOption: databaseOption,
                        nameArgument: nameArgument) ?? 0);
                });

                libraryCommand.Command("grant", grantCommand =>
                {
                    grantCommand.FullName = grantCommand.Parent.FullName;
                    grantCommand.Description = "Grant a user access to a music library.";

                    grantCommand.HelpOption("-h|--help");
                    grantCommand.OptionHelp.Description = "Show help.";

                    var nameArgument = grantCommand.Argument(
                        name: "name",
                        description: "Name of the music library to grant a user access to.");

                    var userArgument = grantCommand.Argument(
                        name: "user",
                        description: "Name of the user to grant access to.");

                    grantCommand.OnExecute(() => GrantLibraryUser(
                        command: grantCommand,
                        databaseOption: databaseOption,
                        libraryNameArgument: nameArgument,
                        userNameArgument: userArgument) ?? 0);
                });

                libraryCommand.Command("revoke", revokeCommand =>
                {
                    revokeCommand.FullName = revokeCommand.Parent.FullName;
                    revokeCommand.Description = "Revoke a user's access to a music library.";

                    revokeCommand.HelpOption("-h|--help");
                    revokeCommand.OptionHelp.Description = "Show help.";

                    var nameArgument = revokeCommand.Argument(
                        name: "name",
                        description: "Name of the music library to revoke a user's access to.");

                    var userArgument = revokeCommand.Argument(
                        name: "user",
                        description: "Name of the user to revoke access of.");

                    revokeCommand.OnExecute(() => RevokeLibraryUser(
                        command: revokeCommand,
                        databaseOption: databaseOption,
                        libraryNameArgument: nameArgument,
                        userNameArgument: userArgument) ?? 0);
                });

                libraryCommand.OnExecute(() => ShowHelp(libraryCommand) ?? 0);
            });

            app.Command("libraries", librariesCommand =>
            {
                librariesCommand.FullName = librariesCommand.Parent.FullName;
                librariesCommand.Description = "List music libraries.";
                librariesCommand.ShowInHelpText = false;

                librariesCommand.HelpOption("-h|--help");
                librariesCommand.OptionHelp.Description = "Show help.";

                librariesCommand.OnExecute(() => ListLibraries(
                    command: librariesCommand,
                    databaseOption: databaseOption) ?? 0);
            });

            app.Command("user", userCommand =>
            {
                userCommand.FullName = userCommand.Parent.FullName;
                userCommand.Description = "Provides user account management.";

                userCommand.HelpOption("-h|--help");
                userCommand.OptionHelp.Description = "Show help.";

                userCommand.Command("list", listCommand =>
                {
                    listCommand.FullName = listCommand.Parent.FullName;
                    listCommand.Description = "List user accounts.";

                    listCommand.HelpOption("-h|--help");
                    listCommand.OptionHelp.Description = "Show help.";

                    listCommand.OnExecute(() => ListUsers(
                        command: listCommand,
                        databaseOption: databaseOption) ?? 0);
                });

                userCommand.Command("add", addCommand =>
                {
                    addCommand.FullName = addCommand.Parent.FullName;
                    addCommand.Description = "Add a user account.";

                    addCommand.HelpOption("-h|--help");
                    addCommand.OptionHelp.Description = "Show help.";

                    var nameArgument = addCommand.Argument(
                        name: "name",
                        description: "Name of user to add.");

                    var maxBitRateOption = addCommand.Option(
                        template: "--max-bit-rate <bit-rate>",
                        description: "Set the user's streaming bit rate limit (default: 128000).",
                        optionType: CommandOptionType.SingleValue);

                    var adminOption = addCommand.Option(
                        template: "--admin",
                        description: "Allow the user to manage users.",
                        optionType: CommandOptionType.NoValue);

                    var guestOption = addCommand.Option(
                        template: "--guest",
                        description: "Allow the user access using any password.",
                        optionType: CommandOptionType.NoValue);

                    var jukeboxOption = addCommand.Option(
                        template: "--jukebox",
                        description: "Allow the user to operate the jukebox.",
                        optionType: CommandOptionType.NoValue);

                    addCommand.OnExecute(() => AddUser(
                        command: addCommand,
                        databaseOption: databaseOption,
                        nameArgument: nameArgument,
                        maxBitRateOption: maxBitRateOption,
                        adminOption: adminOption,
                        guestOption: guestOption,
                        jukeboxOption: jukeboxOption) ?? 0);
                });

                userCommand.Command("modify", modifyCommand =>
                {
                    modifyCommand.FullName = modifyCommand.Parent.FullName;
                    modifyCommand.Description = "Modify a user account.";

                    modifyCommand.HelpOption("-h|--help");
                    modifyCommand.OptionHelp.Description = "Show help.";

                    var nameArgument = modifyCommand.Argument(
                        name: "name",
                        description: "Name of user to modify.");

                    var maxBitRateOption = modifyCommand.Option(
                        template: "--max-bit-rate <bit-rate>",
                        description: "Set the user's streaming bit rate limit.",
                        optionType: CommandOptionType.SingleValue);

                    var adminOption = modifyCommand.Option(
                        template: "--admin <yes/no>",
                        description: "Set whether to allow the user to manage users.",
                        optionType: CommandOptionType.SingleValue);

                    var guestOption = modifyCommand.Option(
                        template: "--guest <yes/no>",
                        description: "Set whether to allow the user access using any password.",
                        optionType: CommandOptionType.SingleValue);

                    var jukeboxOption = modifyCommand.Option(
                        template: "--jukebox <yes/no>",
                        description: "Set the user's ability to operate the jukebox.",
                        optionType: CommandOptionType.SingleValue);

                    var renameOption = modifyCommand.Option(
                        template: "--rename <name>",
                        description: "Rename the user.",
                        optionType: CommandOptionType.SingleValue);

                    var resetPasswordOption = modifyCommand.Option(
                        template: "--reset-password",
                        description: "Reset the user's password.",
                        optionType: CommandOptionType.NoValue);

                    modifyCommand.OnExecute(() => ModifyUser(
                        command: modifyCommand,
                        databaseOption: databaseOption,
                        nameArgument: nameArgument,
                        maxBitRateOption: maxBitRateOption,
                        adminOption: adminOption,
                        guestOption: guestOption,
                        jukeboxOption: jukeboxOption,
                        renameOption: renameOption,
                        resetPasswordOption: resetPasswordOption) ?? 0);
                });

                userCommand.Command("delete", deleteCommand =>
                {
                    deleteCommand.FullName = deleteCommand.Parent.FullName;
                    deleteCommand.Description = "Delete a user account.";

                    deleteCommand.HelpOption("-h|--help");
                    deleteCommand.OptionHelp.Description = "Show help.";

                    var nameArgument = deleteCommand.Argument(
                        name: "name",
                        description: "Name of user to delete.");

                    deleteCommand.OnExecute(() => DeleteUser(
                        command: deleteCommand,
                        databaseOption: databaseOption,
                        nameArgument: nameArgument) ?? 0);
                });

                userCommand.OnExecute(() => ShowHelp(userCommand) ?? 0);
            });

            app.Command("users", usersCommand =>
            {
                usersCommand.FullName = usersCommand.Parent.FullName;
                usersCommand.Description = "List users.";
                usersCommand.ShowInHelpText = false;

                usersCommand.HelpOption("-h|--help");
                usersCommand.OptionHelp.Description = "Show help.";

                usersCommand.OnExecute(() => ListUsers(
                    command: usersCommand,
                    databaseOption: databaseOption) ?? 0);
            });

            app.OnExecute(() => ShowHelp(app) ?? 0);

            try
            {
                return app.Execute(args);
            }
            catch (CommandParsingException ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.BackgroundColor = ConsoleColor.Black;
                app.Error.WriteLine(ex.Message + ".");
                Console.ResetColor();
                return -1;
            }
            catch (CommandAbortException ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.BackgroundColor = ConsoleColor.Black;
                app.Error.WriteLine(ex.Message);
                Console.ResetColor();
                return -1;
            }
        }

        private static int? ShowHelp(CommandLineApplication command, string commandName = null)
        {
            command.ShowHelp(commandName);

            return null;
        }

        private static int? Scan(CommandLineApplication command, CommandOption databaseOption, CommandOption forceOption)
        {
            EnsureFfmpegExists();

            if (!databaseOption.HasValue())
                databaseOption.Values.Add(DefaultDatabasePath);

            SqliteConnection dbConnection = OpenDatabase(databaseOption);

            var optionsBuilder = new DbContextOptionsBuilder<MediaInfoContext>()
                .UseSqlite(dbConnection);

            var services = new ServiceCollection();

            services.AddDbContext<MediaInfoContext>(
                options =>
                {
                    options
#if DEBUG
                        .UseLoggerFactory(_loggerFactory)
#endif
                        .UseSqlite(dbConnection);
                });

            services.AddSingleton<MediaScanService>();

            using (var serviceProvider = services.BuildServiceProvider())
            {
                var mediaScanService = serviceProvider.GetRequiredService<MediaScanService>();

                mediaScanService.ScanAsync(forceOption.HasValue()).Wait();
            }

            return null;
        }

        private static int? Serve(CommandLineApplication command, CommandOption databaseOption, CommandOption bindOption, CommandOption certificateOption)
        {
            EnsureFfmpegExists();

            if (!databaseOption.HasValue())
                databaseOption.Values.Add(DefaultDatabasePath);

            var endPoints = new List<IPEndPoint>();
            if (bindOption.HasValue())
            {
                foreach (string bindValue in bindOption.Values)
                {
                    IPEndPoint endPoint;
                    if (!IPEndPointExtensions.TryParse(bindValue, out endPoint))
                        throw new CommandAbortException($"Unable to parse endpoint '{bindValue}'.");
                    endPoints.Add(endPoint);
                }
            }
            else
            {
                endPoints.Add(new IPEndPoint(IPAddress.IPv6Any, 4040));
            }

            X509Certificate2 certificate = null;
            if (certificateOption.HasValue())
            {
                string certificateValue = certificateOption.Value();
                try
                {
                    certificate = new X509Certificate2(certificateValue);
                    if (!certificate.HasPrivateKey)
                        throw new CommandAbortException($"No private key included in certificate '{certificateValue}'.");
                }
                catch (CryptographicException)
                {
                    throw new CommandAbortException($"Unable to load certificate '{certificateValue}'.");
                }
            }

            SqliteConnection dbConnection = OpenDatabase(databaseOption);

            IWebHost host = new WebHostBuilder()
                .UseKestrel(options =>
                {
                    foreach (var endPoint in endPoints)
                        options.Listen(endPoint, listenOptions =>
                        {
                            if (certificate != null)
                                listenOptions.UseHttps(certificate);
                        });
                })
                .ConfigureServices(services =>
                {
                    services.AddDbContext<MediaInfoContext>(
                        options =>
                        {
                            options
#if DEBUG
                                .UseLoggerFactory(_loggerFactory)
                                .DisableClientSideEvaluation()
#endif
                                .UseSqlite(dbConnection);
                        });

                    services.AddSingleton<JukeboxService>();
                    services.AddSingleton<MediaScanService>();

                    services.AddSingleton<IStartup>(new Startup());
                })
                .UseSetting(WebHostDefaults.ApplicationKey, nameof(Hypersonic))
                .Build();

            host.Services.GetRequiredService<JukeboxService>();
            host.Services.GetRequiredService<MediaScanService>();

            try
            {
                host.Run();
            }
            catch (IOException ex)
            {
                throw new CommandAbortException($"Unable to start server: {ex.Message}");
            }

            return null;
        }

        private static int? ListLibraries(CommandLineApplication command, CommandOption databaseOption)
        {
            if (!databaseOption.HasValue())
                databaseOption.Values.Add(DefaultDatabasePath);

            SqliteConnection dbConnection = OpenDatabase(databaseOption);

            var optionsBuilder = new DbContextOptionsBuilder<MediaInfoContext>()
                .UseSqlite(dbConnection);

            using (var dbContext = new MediaInfoContext(optionsBuilder.Options))
            {
                var rootDirectories = dbContext.Directories
                    .Where(d => d.ParentDirectoryId == null);

                var libraries = dbContext.Libraries
                    .Join(rootDirectories, l => l.LibraryId, d => d.LibraryId, (l, d) => new
                    {
                        l.Name,
                        l.IsAccessControlled,
                        UserCount = l.LibraryUsers.Count(),
                        d.Path,
                    })
                    .AsEnumerable()
                    .Select(e => new
                    {
                        e.Name,
                        Users = e.IsAccessControlled ? e.UserCount.ToString(CultureInfo.CurrentCulture) : "all",
                        e.Path,
                    })
                    .ToList();

                const string nameHeader = "Name";
                const string usersHeader = "Users";
                const string pathHeader = "Path";

                int nameWidth = libraries.Aggregate(nameHeader.Length, (acc, e) => Math.Max(acc, e.Name.Length));
                int usersWidth = libraries.Aggregate(usersHeader.Length, (acc, e) => Math.Max(acc, e.Users.Length));

                command.Out.WriteRightPadded(nameHeader, nameWidth);
                command.Out.Write("  ");
                command.Out.WriteRightPadded(usersHeader, usersWidth);
                command.Out.Write("  ");
                command.Out.Write(pathHeader);
                command.Out.WriteLine();

                foreach (var library in libraries)
                {
                    command.Out.WriteRightPadded(library.Name, nameWidth);
                    command.Out.Write("  ");
                    command.Out.WriteRightPadded(library.Users, usersWidth);
                    command.Out.Write("  ");
                    command.Out.Write(library.Path);
                    command.Out.WriteLine();
                }
            }

            return null;
        }

        private static int? AddLibrary(CommandLineApplication command, CommandOption databaseOption, CommandArgument nameArgument, CommandArgument pathArgument, CommandOption accessControlOption)
        {
            if (!databaseOption.HasValue())
                databaseOption.Values.Add(DefaultDatabasePath);

            string name = nameArgument.Value;
            string path = pathArgument.Value;

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(path))
                return ShowHelp(command) ?? -1;

            string originalPath = path;

            path = IOPath.GetFullPath(path);
            if (IOPath.GetFileName(path).Length == 0)
                path = IOPath.GetDirectoryName(path) ?? path;

            if (!IODirectory.Exists(path))
                throw new CommandAbortException($"The path '{originalPath}' does not exist or is not a directory.");

            bool isAccessControlled = accessControlOption.HasValue();

            SqliteConnection dbConnection = OpenDatabase(databaseOption);

            var optionsBuilder = new DbContextOptionsBuilder<MediaInfoContext>()
                .UseSqlite(dbConnection);

            using (var dbContext = new MediaInfoContext(optionsBuilder.Options))
            {
                using (var transaction = dbContext.Database.BeginTransaction(System.Data.IsolationLevel.Serializable))
                {
                    int? libraryId = dbContext.Libraries
                        .Where(l => l.Name == name)
                        .Select(l => l.LibraryId as int?)
                        .SingleOrDefault();

                    if (libraryId.HasValue)
                        throw new CommandAbortException($"Music library '{name}' already exists.");

                    var library = new Library
                    {
                        Name = name,
                        IsAccessControlled = isAccessControlled,
                        ContentModified = DateTime.UtcNow,
                    };
                    dbContext.Libraries.Add(library);

                    var directory = new Data.Directory
                    {
                        Library = library,
                        ParentDirectory = null,
                        Path = path,
                    };
                    dbContext.Directories.Add(directory);

                    dbContext.SaveChanges();

                    transaction.Commit();
                }
            }

            return null;
        }

        private static int? ModifyLibrary(CommandLineApplication command, CommandOption databaseOption, CommandArgument nameArgument, CommandOption accessControlOption, CommandOption renameOption)
        {
            if (!databaseOption.HasValue())
                databaseOption.Values.Add(DefaultDatabasePath);

            string name = nameArgument.Value;

            if (string.IsNullOrEmpty(name))
                return ShowHelp(command) ?? -1;

            bool isAccessControlled = default;
            if (accessControlOption.HasValue())
            {
                string accessControlOptionValue = accessControlOption.Value();
                if (!TryParseYesNo(accessControlOptionValue, out isAccessControlled))
                    throw new CommandAbortException($"Unable to parse option value '{accessControlOptionValue}'.");
            }

            string newName = renameOption.Value();
            if (renameOption.HasValue() && string.IsNullOrWhiteSpace(newName))
                throw new CommandAbortException($"Invalid library name '{newName}'.");

            SqliteConnection dbConnection = OpenDatabase(databaseOption);

            var optionsBuilder = new DbContextOptionsBuilder<MediaInfoContext>()
                .UseSqlite(dbConnection);

            using (var dbContext = new MediaInfoContext(optionsBuilder.Options))
            {
                using (var transaction = dbContext.Database.BeginTransaction(System.Data.IsolationLevel.Serializable))
                {
                    var library = dbContext.Libraries
                        .Where(u => u.Name == name)
                        .SingleOrDefault();

                    if (library == null)
                        throw new CommandAbortException($"Library '{name}' does not exist.");

                    if (accessControlOption.HasValue())
                        library.IsAccessControlled = isAccessControlled;

                    if (renameOption.HasValue())
                    {
                        int? newLibraryId = dbContext.Libraries
                            .Where(u => u.Name == name)
                            .Select(u => u.LibraryId as int?)
                            .SingleOrDefault();

                        if (newLibraryId.HasValue)
                            throw new CommandAbortException($"Library '{newName}' already exists.");

                        library.Name = newName;
                    }

                    dbContext.SaveChanges();

                    transaction.Commit();
                }
            }

            return null;
        }

        private static int? DeleteLibrary(CommandLineApplication command, CommandOption databaseOption, CommandArgument nameArgument)
        {
            if (!databaseOption.HasValue())
                databaseOption.Values.Add(DefaultDatabasePath);

            string name = nameArgument.Value;

            if (string.IsNullOrEmpty(name))
                return ShowHelp(command) ?? -1;

            SqliteConnection dbConnection = OpenDatabase(databaseOption);

            var optionsBuilder = new DbContextOptionsBuilder<MediaInfoContext>()
                .UseSqlite(dbConnection);

            using (var dbContext = new MediaInfoContext(optionsBuilder.Options))
            {
                using (var transaction = dbContext.Database.BeginTransaction(System.Data.IsolationLevel.Serializable))
                {
                    int? libraryId = dbContext.Libraries
                        .Where(l => l.Name == name)
                        .Select(l => l.LibraryId as int?)
                        .SingleOrDefault();

                    if (!libraryId.HasValue)
                        throw new CommandAbortException($"Music library '{name}' does not exist.");

                    dbContext.Libraries.RemoveRange(dbContext.Libraries
                        .Where(l => l.LibraryId == libraryId));

                    dbContext.SaveChanges();

                    transaction.Commit();
                }
            }

            return null;
        }

        private static int? GrantLibraryUser(CommandLineApplication command, CommandOption databaseOption, CommandArgument libraryNameArgument, CommandArgument userNameArgument)
        {
            if (!databaseOption.HasValue())
                databaseOption.Values.Add(DefaultDatabasePath);

            string libraryName = libraryNameArgument.Value;
            string userName = userNameArgument.Value;

            if (string.IsNullOrEmpty(libraryName) || string.IsNullOrEmpty(userName))
                return ShowHelp(command) ?? -1;

            SqliteConnection dbConnection = OpenDatabase(databaseOption);

            var optionsBuilder = new DbContextOptionsBuilder<MediaInfoContext>()
                .UseSqlite(dbConnection);

            using (var dbContext = new MediaInfoContext(optionsBuilder.Options))
            {
                using (var transaction = dbContext.Database.BeginTransaction(System.Data.IsolationLevel.Serializable))
                {
                    int? libraryId = dbContext.Libraries
                        .Where(l => l.Name == libraryName)
                        .Select(l => l.LibraryId as int?)
                        .SingleOrDefault();

                    if (!libraryId.HasValue)
                        throw new CommandAbortException($"Music library '{libraryName}' does not exist.");

                    int? userId = dbContext.Users
                        .Where(l => l.Name == userName)
                        .Select(l => l.UserId as int?)
                        .SingleOrDefault();

                    if (!userId.HasValue)
                        throw new CommandAbortException($"User '{userName}' does not exist.");

                    var libraryUser = dbContext.LibraryUsers
                        .Where(lu => lu.LibraryId == libraryId.Value)
                        .Where(lu => lu.UserId == userId.Value)
                        .SingleOrDefault();

                    if (libraryUser != null)
                        throw new CommandAbortException($"Access to music library '{libraryName}' is already granted to user '{userName}'.");

                    dbContext.LibraryUsers.Add(new LibraryUser
                    {
                        LibraryId = libraryId.Value,
                        UserId = userId.Value,
                    });

                    dbContext.SaveChanges();

                    transaction.Commit();
                }
            }

            return null;
        }

        private static int? RevokeLibraryUser(CommandLineApplication command, CommandOption databaseOption, CommandArgument libraryNameArgument, CommandArgument userNameArgument)
        {
            if (!databaseOption.HasValue())
                databaseOption.Values.Add(DefaultDatabasePath);

            string libraryName = libraryNameArgument.Value;
            string userName = userNameArgument.Value;

            if (string.IsNullOrEmpty(libraryName) || string.IsNullOrEmpty(userName))
                return ShowHelp(command) ?? -1;

            SqliteConnection dbConnection = OpenDatabase(databaseOption);

            var optionsBuilder = new DbContextOptionsBuilder<MediaInfoContext>()
                .UseSqlite(dbConnection);

            using (var dbContext = new MediaInfoContext(optionsBuilder.Options))
            {
                using (var transaction = dbContext.Database.BeginTransaction(System.Data.IsolationLevel.Serializable))
                {
                    int? libraryId = dbContext.Libraries
                        .Where(l => l.Name == libraryName)
                        .Select(l => l.LibraryId as int?)
                        .SingleOrDefault();

                    if (!libraryId.HasValue)
                        throw new CommandAbortException($"Music library '{libraryName}' does not exist.");

                    int? userId = dbContext.Users
                        .Where(l => l.Name == userName)
                        .Select(l => l.UserId as int?)
                        .SingleOrDefault();

                    if (!userId.HasValue)
                        throw new CommandAbortException($"User '{userName}' does not exist.");

                    var libraryUser = dbContext.LibraryUsers
                        .Where(lu => lu.LibraryId == libraryId.Value)
                        .Where(lu => lu.UserId == userId.Value)
                        .SingleOrDefault();

                    if (libraryUser == null)
                        throw new CommandAbortException($"Access to music library '{libraryName}' was already not granted to user '{userName}'.");

                    dbContext.LibraryUsers.Remove(libraryUser);

                    dbContext.SaveChanges();

                    transaction.Commit();
                }
            }

            return null;
        }

        private static int? ListUsers(CommandLineApplication command, CommandOption databaseOption)
        {
            if (!databaseOption.HasValue())
                databaseOption.Values.Add(DefaultDatabasePath);

            SqliteConnection dbConnection = OpenDatabase(databaseOption);

            var optionsBuilder = new DbContextOptionsBuilder<MediaInfoContext>()
                .UseSqlite(dbConnection);

            using (var dbContext = new MediaInfoContext(optionsBuilder.Options))
            {
                var rootDirectories = dbContext.Directories
                    .Where(d => d.ParentDirectoryId == null);

                var users = dbContext.Users
                    .Select(u => new
                    {
                        u.Name,
                        u.MaxBitRate,
                        u.IsAdmin,
                        u.IsGuest,
                        u.CanJukebox,
                    })
                    .AsEnumerable()
                    .Select(e => new
                    {
                        e.Name,
                        MaxBitRate = e.MaxBitRate.ToStringInvariant(),
                        IsAdmin = e.IsAdmin ? "yes" : "no",
                        IsGuest = e.IsGuest ? "yes" : "no",
                        CanJukebox = e.CanJukebox ? "yes" : "no",
                    })
                    .ToList();

                const string nameHeader = "Name";
                const string maxBitRateHeader = "Max Bit Rate";
                const string adminHeader = "Admin";
                const string guestHeader = "Guest";
                const string jukeboxHeader = "Jukebox";

                int nameWidth = users.Aggregate(nameHeader.Length, (acc, e) => Math.Max(acc, e.Name.Length));
                int maxBitRateWidth = users.Aggregate(maxBitRateHeader.Length, (acc, e) => Math.Max(acc, e.MaxBitRate.Length));
                int guestWidth = users.Aggregate(guestHeader.Length, (acc, e) => Math.Max(acc, e.IsGuest.Length));
                int adminWidth = users.Aggregate(adminHeader.Length, (acc, e) => Math.Max(acc, e.IsAdmin.Length));

                command.Out.WriteRightPadded(nameHeader, nameWidth);
                command.Out.Write("  ");
                command.Out.WriteRightPadded(maxBitRateHeader, maxBitRateWidth);
                command.Out.Write("  ");
                command.Out.WriteRightPadded(adminHeader, adminWidth);
                command.Out.Write("  ");
                command.Out.WriteRightPadded(guestHeader, guestWidth);
                command.Out.Write("  ");
                command.Out.Write(jukeboxHeader);
                command.Out.WriteLine();

                foreach (var user in users)
                {
                    command.Out.WriteRightPadded(user.Name, nameWidth);
                    command.Out.Write("  ");
                    command.Out.WriteRightPadded(user.MaxBitRate, maxBitRateWidth);
                    command.Out.Write("  ");
                    command.Out.WriteRightPadded(user.IsAdmin, adminWidth);
                    command.Out.Write("  ");
                    command.Out.WriteRightPadded(user.IsGuest, guestWidth);
                    command.Out.Write("  ");
                    command.Out.Write(user.CanJukebox);
                    command.Out.WriteLine();
                }
            }

            return null;
        }

        private static int? AddUser(CommandLineApplication command, CommandOption databaseOption, CommandArgument nameArgument, CommandOption maxBitRateOption, CommandOption adminOption, CommandOption guestOption, CommandOption jukeboxOption)
        {
            if (!databaseOption.HasValue())
                databaseOption.Values.Add(DefaultDatabasePath);

            string name = nameArgument.Value;

            if (string.IsNullOrEmpty(name))
                return ShowHelp(command) ?? -1;

            int maxBitRate = 128000;
            if (maxBitRateOption.HasValue())
            {
                string maxBitRateValue = maxBitRateOption.Value();
                if (!TryParseNonNegativeInt32(maxBitRateValue, out maxBitRate))
                    throw new CommandAbortException($"Unable to parse maximum bit rate '{maxBitRateValue}'.");
            }

            bool isAdmin = adminOption.HasValue();

            bool isGuest = guestOption.HasValue();

            bool canJukebox = jukeboxOption.HasValue();

            string password = GeneratePassword();

            SqliteConnection dbConnection = OpenDatabase(databaseOption);

            var optionsBuilder = new DbContextOptionsBuilder<MediaInfoContext>()
                .UseSqlite(dbConnection);

            using (var dbContext = new MediaInfoContext(optionsBuilder.Options))
            {
                using (var transaction = dbContext.Database.BeginTransaction(System.Data.IsolationLevel.Serializable))
                {
                    int? userId = dbContext.Users
                        .Where(u => u.Name == name)
                        .Select(u => u.UserId as int?)
                        .SingleOrDefault();

                    if (userId.HasValue)
                        throw new CommandAbortException($"User '{name}' already exists.");

                    var user = new User
                    {
                        Name = name,
                        Password = password,
                        MaxBitRate = maxBitRate,
                        IsAdmin = isAdmin,
                        IsGuest = isGuest,
                        CanJukebox = canJukebox,
                    };
                    dbContext.Users.Add(user);

                    dbContext.SaveChanges();

                    transaction.Commit();
                }
            }

            if (!isGuest)
                Console.Out.WriteLine($"Password for user '{name}' is set to '{password}'.");

            return null;
        }

        private static int? ModifyUser(CommandLineApplication command, CommandOption databaseOption, CommandArgument nameArgument, CommandOption maxBitRateOption, CommandOption adminOption, CommandOption guestOption, CommandOption jukeboxOption, CommandOption renameOption, CommandOption resetPasswordOption)
        {
            if (!databaseOption.HasValue())
                databaseOption.Values.Add(DefaultDatabasePath);

            string name = nameArgument.Value;

            if (string.IsNullOrEmpty(name))
                return ShowHelp(command) ?? -1;

            int maxBitRate = default;
            if (maxBitRateOption.HasValue())
            {
                string maxBitRateValue = maxBitRateOption.Value();
                if (!TryParseNonNegativeInt32(maxBitRateValue, out maxBitRate))
                    throw new CommandAbortException($"Unable to parse maximum bit rate '{maxBitRateValue}'.");
            }

            bool isAdmin = default;
            if (adminOption.HasValue())
            {
                string adminOptionValue = adminOption.Value();
                if (!TryParseYesNo(adminOptionValue, out isAdmin))
                    throw new CommandAbortException($"Unable to parse option value '{adminOptionValue}'.");
            }

            bool isGuest = default;
            if (guestOption.HasValue())
            {
                string guestOptionValue = guestOption.Value();
                if (!TryParseYesNo(guestOptionValue, out isGuest))
                    throw new CommandAbortException($"Unable to parse option value '{guestOptionValue}'.");
            }

            bool canJukebox = default;
            if (jukeboxOption.HasValue())
            {
                string jukeboxOptionValue = jukeboxOption.Value();
                if (!TryParseYesNo(jukeboxOptionValue, out canJukebox))
                    throw new CommandAbortException($"Unable to parse option value '{jukeboxOptionValue}'.");
            }

            string newName = renameOption.Value();
            if (renameOption.HasValue() && string.IsNullOrWhiteSpace(newName))
                throw new CommandAbortException($"Invalid user name '{newName}'.");

            string newPassword = default;
            if (resetPasswordOption.HasValue())
                newPassword = GeneratePassword();

            SqliteConnection dbConnection = OpenDatabase(databaseOption);

            var optionsBuilder = new DbContextOptionsBuilder<MediaInfoContext>()
                .UseSqlite(dbConnection);

            using (var dbContext = new MediaInfoContext(optionsBuilder.Options))
            {
                using (var transaction = dbContext.Database.BeginTransaction(System.Data.IsolationLevel.Serializable))
                {
                    var user = dbContext.Users
                        .Where(u => u.Name == name)
                        .SingleOrDefault();

                    if (user == null)
                        throw new CommandAbortException($"User '{name}' does not exist.");

                    if (maxBitRateOption.HasValue())
                        user.MaxBitRate = maxBitRate;

                    if (adminOption.HasValue())
                        user.IsAdmin = isAdmin;

                    if (guestOption.HasValue())
                        user.IsGuest = isGuest;

                    if (jukeboxOption.HasValue())
                        user.CanJukebox = canJukebox;

                    if (renameOption.HasValue())
                    {
                        int? newUserId = dbContext.Users
                            .Where(u => u.Name == name)
                            .Select(u => u.UserId as int?)
                            .SingleOrDefault();

                        if (newUserId.HasValue)
                            throw new CommandAbortException($"User '{newName}' already exists.");

                        user.Name = newName;
                    }

                    if (resetPasswordOption.HasValue())
                        user.Password = newPassword;

                    dbContext.SaveChanges();

                    transaction.Commit();
                }
            }

            if (resetPasswordOption.HasValue())
                Console.Out.WriteLine($"Password for user '{name}' is set to '{newPassword}'.");

            return null;
        }

        private static int? DeleteUser(CommandLineApplication command, CommandOption databaseOption, CommandArgument nameArgument)
        {
            if (!databaseOption.HasValue())
                databaseOption.Values.Add(DefaultDatabasePath);

            string name = nameArgument.Value;

            if (string.IsNullOrEmpty(name))
                return ShowHelp(command) ?? -1;

            SqliteConnection dbConnection = OpenDatabase(databaseOption);

            var optionsBuilder = new DbContextOptionsBuilder<MediaInfoContext>()
                .UseSqlite(dbConnection);

            using (var dbContext = new MediaInfoContext(optionsBuilder.Options))
            {
                using (var transaction = dbContext.Database.BeginTransaction(System.Data.IsolationLevel.Serializable))
                {
                    int? userId = dbContext.Users
                        .Where(u => u.Name == name)
                        .Select(u => u.UserId as int?)
                        .SingleOrDefault();

                    if (!userId.HasValue)
                        throw new CommandAbortException($"User '{name}' does not exist.");

                    dbContext.Users.RemoveRange(dbContext.Users
                        .Where(l => l.UserId == userId));

                    dbContext.SaveChanges();

                    transaction.Commit();
                }
            }

            return null;
        }

        private static void EnsureFfmpegExists()
        {
            var arguments = new Ffmpeg.ArgumentList()
                .Add("-v").Add("fatal")
                .Add("-version");

            try
            {
                using (var stream = new Ffmpeg.FfmpegStream("ffmpeg", arguments))
                using (var reader = new StreamReader(stream))
                {
                    string line = reader.ReadLine();
                    if (!line.StartsWith("ffmpeg version ", StringComparison.Ordinal))
                        throw new CommandAbortException("ffmpeg is not functional.");
                }
            }
            catch (Win32Exception ex)
            {
                throw new CommandAbortException("Unable to start ffmpeg: " + ex.Message);
            }

            try
            {
                using (var stream = new Ffmpeg.FfmpegStream("ffplay", arguments))
                using (var reader = new StreamReader(stream))
                {
                    string line = reader.ReadLine();
                    if (!line.StartsWith("ffplay version ", StringComparison.Ordinal))
                        throw new CommandAbortException("ffplay is not functional.");
                }
            }
            catch (Win32Exception ex)
            {
                throw new CommandAbortException("Unable to start ffplay: " + ex.Message);
            }

            try
            {
                using (var stream = new Ffmpeg.FfmpegStream("ffprobe", arguments))
                using (var reader = new StreamReader(stream))
                {
                    string line = reader.ReadLine();
                    if (!line.StartsWith("ffprobe version ", StringComparison.Ordinal))
                        throw new CommandAbortException("ffprobe is not functional.");
                }
            }
            catch (Win32Exception ex)
            {
                throw new CommandAbortException("Unable to start ffprobe: " + ex.Message);
            }
        }

        private static SqliteConnection OpenDatabase(CommandOption databaseOption)
        {
            if (!databaseOption.HasValue())
                throw new ArgumentException("Database option must have a value.", nameof(databaseOption));

            string databaseValue = databaseOption.Value();

            try
            {
                IODirectory.CreateDirectory(IOPath.GetDirectoryName(databaseValue));
            }
            catch
            {
                throw new CommandAbortException($"Unable to open database '{databaseValue}'.");
            }

            var dbConnectionStringBuilder = new SqliteConnectionStringBuilder()
            {
                DataSource = databaseValue,
            };

            var dbConnection = new SqliteConnection(dbConnectionStringBuilder.ConnectionString);
            try
            {
                dbConnection.Open();
            }
            catch (SqliteException)
            {
                throw new CommandAbortException($"Unable to open database '{databaseValue}'.");
            }

            try
            {
                var optionsBuilder = new DbContextOptionsBuilder<MediaInfoContext>()
                    .UseSqlite(dbConnection);

                using (var dbContext = new MediaInfoContext(optionsBuilder.Options))
                    dbContext.Database.Migrate();
            }
            catch (Exception)
            {
                throw new CommandAbortException($"Unable to migrate database '{databaseValue}'.");
            }

            return dbConnection;
        }

        private static bool TryParseYesNo(string s, out bool value)
        {
            switch (s)
            {
                case "yes":
                    value = true;
                    return true;
                case "no":
                    value = false;
                    return true;
                default:
                    value = default;
                    return false;
            }
        }

        private static bool TryParseNonNegativeInt32(string s, out int value)
        {
            return int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out value);
        }

        private static string GeneratePassword(int bits = 48)
        {
            const int log2 = 5;
            const int mask = (1 << log2) - 1;
            var symbols = new char[]
            {
                '2', '3', '4', '5', '6', '7', '8', '9',
                'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h',
                'i', 'j', 'k', 'm', 'n', 'p', 'q', 'r',
                's', 't', 'u', 'v', 'w', 'x', 'y', 'z',
            };

            var chars = new char[(bits - 1) / log2 + 1];
            var bytes = new byte[(chars.Length * log2 - 1) / 8 + 1];
            RandomNumberGenerator.Fill(bytes);
            int byteIndex = 0;
            int bitBuffer = 0;
            int bitCount = 0;
            for (int i = 0; i < chars.Length; ++i)
            {
                if (bitCount < log2)
                {
                    bitBuffer |= bytes[byteIndex] << bitCount;
                    bitCount += 8;
                    byteIndex += 1;
                }
                chars[i] = symbols[bitBuffer & mask];
                bitBuffer >>= log2;
                bitCount -= log2;
            }
            return new string(chars);
        }

#pragma warning disable CA1032 // Implement standard exception constructors
#pragma warning disable CA1064 // Exceptions should be public
        private class CommandAbortException : Exception
#pragma warning restore CA1032 // Implement standard exception constructors
#pragma warning restore CA1064 // Exceptions should be public
        {
            public CommandAbortException(string message)
                : base(message)
            {
            }
        }
    }
}
