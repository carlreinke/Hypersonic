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
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hypersonic.Data
{
    internal class MediaInfoContextFactory : IDesignTimeDbContextFactory<MediaInfoContext>
    {
        public MediaInfoContext CreateDbContext(string[] args)
        {
            var dbConnection = new SqliteConnection("Data Source=:memory:");
            dbConnection.Open();

            var optionsBuilder = new DbContextOptionsBuilder<MediaInfoContext>()
                .UseSqlite(dbConnection);

            return new MediaInfoContext(optionsBuilder.Options);
        }
    }
}
