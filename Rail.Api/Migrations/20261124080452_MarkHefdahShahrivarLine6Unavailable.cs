using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rail.Api.Migrations
{
    /// <inheritdoc />
    public partial class MarkHefdahShahrivarLine6Unavailable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE [Metro].[StationLines]
                SET [IsOperational] = 0
                WHERE [StationId] = 'shohada-ye-hefdah-e-shahrivar' AND [LineId] = 6;

                UPDATE stop SET [Status] = 'under_construction'
                FROM [Metro].[RouteStops] AS stop
                JOIN [Metro].[Routes] AS route ON route.[Id] = stop.[RouteId]
                WHERE stop.[StationId] = 'shohada-ye-hefdah-e-shahrivar'
                    AND route.[LineId] = 6 AND stop.[Status] = 'operational';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE [Metro].[StationLines]
                SET [IsOperational] = 1
                WHERE [StationId] = 'shohada-ye-hefdah-e-shahrivar' AND [LineId] = 6;

                UPDATE stop SET [Status] = 'operational'
                FROM [Metro].[RouteStops] AS stop
                JOIN [Metro].[Routes] AS route ON route.[Id] = stop.[RouteId]
                WHERE stop.[StationId] = 'shohada-ye-hefdah-e-shahrivar'
                    AND route.[LineId] = 6 AND stop.[Status] = 'under_construction';
                """);
        }
    }
}
