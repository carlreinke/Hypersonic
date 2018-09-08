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
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hypersonic.Tests
{
    internal static class Helpers
    {
        internal static SqliteConnection OpenSqliteDatabase()
        {
            var dbConnectionStringBuilder = new SqliteConnectionStringBuilder()
            {
                DataSource = ":memory:",
            };

            var dbConnection = new SqliteConnection(dbConnectionStringBuilder.ConnectionString);

            dbConnection.Open();

            var dbContextOptionsBuilder = new DbContextOptionsBuilder<MediaInfoContext>()
                .UseSqlite(dbConnection);

            using (var dbContext = new MediaInfoContext(dbContextOptionsBuilder.Options))
            {
                dbContext.Database.EnsureCreated();
            }

            return dbConnection;
        }
    }
}
