using Microsoft.EntityFrameworkCore.Migrations;
using System;
using System.IO;

namespace Hypersonic.Migrations
{
    public partial class AddLibraryPath : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Path",
                table: "Libraries",
                nullable: false,
                defaultValue: "");

            string dirSep = Path.DirectorySeparatorChar.ToStringInvariant();
            string altDirSep = Path.AltDirectorySeparatorChar.ToStringInvariant();
            string dirSeps = dirSep + altDirSep;
            string escapedDirSeps = dirSeps.Replace("'", "''", StringComparison.Ordinal);

            migrationBuilder.Sql(@"
                UPDATE Libraries
                  SET Path = RTRIM((SELECT Path
                                      FROM Directories
                                      WHERE LibraryId == Libraries.LibraryId
                                        AND ParentDirectoryId IS NULL), '" + escapedDirSeps + @"')
                ");

            migrationBuilder.Sql(@"
                UPDATE Directories
                  SET Path = TRIM(SUBSTR(Path, 1 + (SELECT LENGTH(Path)
                                                      FROM Libraries
                                                      WHERE LibraryId == Directories.LibraryId)), '" + escapedDirSeps + @"')
                ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            string dirSep = Path.DirectorySeparatorChar.ToStringInvariant();
            string escapedDirSep = dirSep.Replace("'", "''", StringComparison.Ordinal);


            migrationBuilder.Sql(@"
                UPDATE Directories
                  SET Path = RTRIM((SELECT Path
                                      FROM Libraries
                                      WHERE LibraryId == Directories.LibraryId) || '" + escapedDirSep + @"' || Path, '" + escapedDirSep + @"')
                ");

            migrationBuilder.DropColumn(
                name: "Path",
                table: "Libraries");
        }
    }
}
