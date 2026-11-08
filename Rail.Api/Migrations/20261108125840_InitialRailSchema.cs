using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Rail.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialRailSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "Identity");

            migrationBuilder.EnsureSchema(
                name: "Metro");

            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                schema: "Identity",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                schema: "Identity",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SecurityStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FareProducts",
                schema: "Metro",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    NameFa = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DescriptionFa = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DescriptionEn = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FareProducts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Lines",
                schema: "Metro",
                columns: table => new
                {
                    Id = table.Column<byte>(type: "tinyint", nullable: false),
                    NameFa = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Color = table.Column<string>(type: "char(7)", unicode: false, fixedLength: true, maxLength: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Lines", x => x.Id);
                    table.CheckConstraint("CK_Lines_Color", "[Color] LIKE '#[0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f]'");
                });

            migrationBuilder.CreateTable(
                name: "ServiceAlerts",
                schema: "Metro",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Severity = table.Column<byte>(type: "tinyint", nullable: false),
                    TitleFa = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TitleEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    MessageFa = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    MessageEn = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    StartsAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndsAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SourceUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceAlerts", x => x.Id);
                    table.CheckConstraint("CK_ServiceAlerts_Dates", "[EndsAtUtc] IS NULL OR [EndsAtUtc] > [StartsAtUtc]");
                    table.CheckConstraint("CK_ServiceAlerts_Severity", "[Severity] BETWEEN 1 AND 4");
                });

            migrationBuilder.CreateTable(
                name: "ServiceDayTypes",
                schema: "Metro",
                columns: table => new
                {
                    Code = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceDayTypes", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "Stations",
                schema: "Metro",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    NameFa = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Latitude = table.Column<decimal>(type: "decimal(10,7)", precision: 10, scale: 7, nullable: false),
                    Longitude = table.Column<decimal>(type: "decimal(10,7)", precision: 10, scale: 7, nullable: false),
                    InfrastructureStatus = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stations", x => x.Id);
                    table.CheckConstraint("CK_Stations_Latitude", "[Latitude] BETWEEN -90 AND 90");
                    table.CheckConstraint("CK_Stations_Longitude", "[Longitude] BETWEEN -180 AND 180");
                    table.CheckConstraint("CK_Stations_Status", "[InfrastructureStatus] IN ('operational', 'under_construction', 'planned', 'temporarily_closed', 'permanently_closed')");
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                schema: "Identity",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "Identity",
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                schema: "Identity",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "Identity",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                schema: "Identity",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "Identity",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                schema: "Identity",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RoleId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "Identity",
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "Identity",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                schema: "Identity",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "Identity",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FarePrices",
                schema: "Metro",
                columns: table => new
                {
                    FareProductId = table.Column<int>(type: "int", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    AmountRials = table.Column<long>(type: "bigint", nullable: false),
                    SourceUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FarePrices", x => new { x.FareProductId, x.EffectiveFrom });
                    table.CheckConstraint("CK_FarePrices_Amount", "[AmountRials] >= 0");
                    table.ForeignKey(
                        name: "FK_FarePrices_FareProducts_FareProductId",
                        column: x => x.FareProductId,
                        principalSchema: "Metro",
                        principalTable: "FareProducts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Routes",
                schema: "Metro",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    LineId = table.Column<byte>(type: "tinyint", nullable: false),
                    BranchCode = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Routes", x => x.Id);
                    table.UniqueConstraint("AK_Routes_Id_LineId", x => new { x.Id, x.LineId });
                    table.ForeignKey(
                        name: "FK_Routes_Lines_LineId",
                        column: x => x.LineId,
                        principalSchema: "Metro",
                        principalTable: "Lines",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ServiceAlertLines",
                schema: "Metro",
                columns: table => new
                {
                    AlertId = table.Column<long>(type: "bigint", nullable: false),
                    LineId = table.Column<byte>(type: "tinyint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceAlertLines", x => new { x.AlertId, x.LineId });
                    table.ForeignKey(
                        name: "FK_ServiceAlertLines_Lines_LineId",
                        column: x => x.LineId,
                        principalSchema: "Metro",
                        principalTable: "Lines",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ServiceAlertLines_ServiceAlerts_AlertId",
                        column: x => x.AlertId,
                        principalSchema: "Metro",
                        principalTable: "ServiceAlerts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ServiceCalendarDates",
                schema: "Metro",
                columns: table => new
                {
                    ServiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DayTypeCode = table.Column<string>(type: "varchar(30)", nullable: false),
                    IsOfficialHoliday = table.Column<bool>(type: "bit", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    VerifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceCalendarDates", x => x.ServiceDate);
                    table.ForeignKey(
                        name: "FK_ServiceCalendarDates_ServiceDayTypes_DayTypeCode",
                        column: x => x.DayTypeCode,
                        principalSchema: "Metro",
                        principalTable: "ServiceDayTypes",
                        principalColumn: "Code");
                });

            migrationBuilder.CreateTable(
                name: "FavoriteStations",
                schema: "Metro",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    StationId = table.Column<string>(type: "varchar(100)", nullable: false),
                    AddedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FavoriteStations", x => new { x.UserId, x.StationId });
                    table.ForeignKey(
                        name: "FK_FavoriteStations_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "Identity",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FavoriteStations_Stations_StationId",
                        column: x => x.StationId,
                        principalSchema: "Metro",
                        principalTable: "Stations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ServiceAlertStations",
                schema: "Metro",
                columns: table => new
                {
                    AlertId = table.Column<long>(type: "bigint", nullable: false),
                    StationId = table.Column<string>(type: "varchar(100)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceAlertStations", x => new { x.AlertId, x.StationId });
                    table.ForeignKey(
                        name: "FK_ServiceAlertStations_ServiceAlerts_AlertId",
                        column: x => x.AlertId,
                        principalSchema: "Metro",
                        principalTable: "ServiceAlerts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ServiceAlertStations_Stations_StationId",
                        column: x => x.StationId,
                        principalSchema: "Metro",
                        principalTable: "Stations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StationAmenities",
                schema: "Metro",
                columns: table => new
                {
                    StationId = table.Column<string>(type: "varchar(100)", nullable: false),
                    Type = table.Column<byte>(type: "tinyint", nullable: false),
                    IsAvailable = table.Column<bool>(type: "bit", nullable: false),
                    IsVerified = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StationAmenities", x => new { x.StationId, x.Type });
                    table.CheckConstraint("CK_StationAmenities_Type", "[Type] BETWEEN 1 AND 10");
                    table.ForeignKey(
                        name: "FK_StationAmenities_Stations_StationId",
                        column: x => x.StationId,
                        principalSchema: "Metro",
                        principalTable: "Stations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StationEntrances",
                schema: "Metro",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StationId = table.Column<string>(type: "varchar(100)", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    NameFa = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AddressFa = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AddressEn = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Latitude = table.Column<decimal>(type: "decimal(10,7)", precision: 10, scale: 7, nullable: true),
                    Longitude = table.Column<decimal>(type: "decimal(10,7)", precision: 10, scale: 7, nullable: true),
                    IsStepFree = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StationEntrances", x => x.Id);
                    table.CheckConstraint("CK_StationEntrances_Coordinates", "([Latitude] IS NULL AND [Longitude] IS NULL) OR\n([Latitude] IS NOT NULL AND [Longitude] IS NOT NULL AND [Latitude] BETWEEN -90 AND 90 AND [Longitude] BETWEEN -180 AND 180)");
                    table.ForeignKey(
                        name: "FK_StationEntrances_Stations_StationId",
                        column: x => x.StationId,
                        principalSchema: "Metro",
                        principalTable: "Stations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StationLines",
                schema: "Metro",
                columns: table => new
                {
                    StationId = table.Column<string>(type: "varchar(100)", nullable: false),
                    LineId = table.Column<byte>(type: "tinyint", nullable: false),
                    IsOperational = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StationLines", x => new { x.StationId, x.LineId });
                    table.ForeignKey(
                        name: "FK_StationLines_Lines_LineId",
                        column: x => x.LineId,
                        principalSchema: "Metro",
                        principalTable: "Lines",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StationLines_Stations_StationId",
                        column: x => x.StationId,
                        principalSchema: "Metro",
                        principalTable: "Stations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RouteStops",
                schema: "Metro",
                columns: table => new
                {
                    RouteId = table.Column<string>(type: "varchar(100)", nullable: false),
                    Sequence = table.Column<short>(type: "smallint", nullable: false),
                    StationId = table.Column<string>(type: "varchar(100)", nullable: false),
                    Status = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RouteStops", x => new { x.RouteId, x.Sequence });
                    table.CheckConstraint("CK_RouteStops_Sequence", "[Sequence] > 0");
                    table.CheckConstraint("CK_RouteStops_Status", "[Status] IN ('operational', 'under_construction', 'planned', 'temporarily_closed', 'permanently_closed')");
                    table.ForeignKey(
                        name: "FK_RouteStops_Routes_RouteId",
                        column: x => x.RouteId,
                        principalSchema: "Metro",
                        principalTable: "Routes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RouteStops_Stations_StationId",
                        column: x => x.StationId,
                        principalSchema: "Metro",
                        principalTable: "Stations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ScheduleGroups",
                schema: "Metro",
                columns: table => new
                {
                    Id = table.Column<short>(type: "smallint", nullable: false),
                    RouteId = table.Column<string>(type: "varchar(100)", nullable: false),
                    ScheduleKey = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    IsBranchService = table.Column<bool>(type: "bit", nullable: false),
                    FromTerminalId = table.Column<string>(type: "varchar(100)", nullable: false),
                    ToTerminalId = table.Column<string>(type: "varchar(100)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduleGroups_Routes_RouteId",
                        column: x => x.RouteId,
                        principalSchema: "Metro",
                        principalTable: "Routes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ScheduleGroups_Stations_FromTerminalId",
                        column: x => x.FromTerminalId,
                        principalSchema: "Metro",
                        principalTable: "Stations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ScheduleGroups_Stations_ToTerminalId",
                        column: x => x.ToTerminalId,
                        principalSchema: "Metro",
                        principalTable: "Stations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TrackSegments",
                schema: "Metro",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(220)", unicode: false, maxLength: 220, nullable: false),
                    RouteId = table.Column<string>(type: "varchar(100)", nullable: false),
                    FromStationId = table.Column<string>(type: "varchar(100)", nullable: false),
                    ToStationId = table.Column<string>(type: "varchar(100)", nullable: false),
                    Status = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackSegments", x => x.Id);
                    table.CheckConstraint("CK_TrackSegments_Endpoints", "[FromStationId] <> [ToStationId]");
                    table.CheckConstraint("CK_TrackSegments_Status", "[Status] IN ('operational', 'under_construction', 'planned', 'temporarily_closed', 'permanently_closed')");
                    table.ForeignKey(
                        name: "FK_TrackSegments_Routes_RouteId",
                        column: x => x.RouteId,
                        principalSchema: "Metro",
                        principalTable: "Routes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TrackSegments_Stations_FromStationId",
                        column: x => x.FromStationId,
                        principalSchema: "Metro",
                        principalTable: "Stations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TrackSegments_Stations_ToStationId",
                        column: x => x.ToStationId,
                        principalSchema: "Metro",
                        principalTable: "Stations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "HolidayEvents",
                schema: "Metro",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ServiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HolidayEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HolidayEvents_ServiceCalendarDates_ServiceDate",
                        column: x => x.ServiceDate,
                        principalSchema: "Metro",
                        principalTable: "ServiceCalendarDates",
                        principalColumn: "ServiceDate",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TransferRules",
                schema: "Metro",
                columns: table => new
                {
                    Id = table.Column<short>(type: "smallint", nullable: false),
                    StationId = table.Column<string>(type: "varchar(100)", nullable: false),
                    FromLineId = table.Column<byte>(type: "tinyint", nullable: false),
                    ToLineId = table.Column<byte>(type: "tinyint", nullable: false),
                    FromRouteId = table.Column<string>(type: "varchar(100)", nullable: true),
                    ToRouteId = table.Column<string>(type: "varchar(100)", nullable: true),
                    WalkSeconds = table.Column<short>(type: "smallint", nullable: false),
                    Confidence = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferRules", x => x.Id);
                    table.CheckConstraint("CK_TransferRules_DifferentServices", "[FromLineId] <> [ToLineId] OR ([FromRouteId] IS NOT NULL AND [ToRouteId] IS NOT NULL AND [FromRouteId] <> [ToRouteId])");
                    table.CheckConstraint("CK_TransferRules_WalkSeconds", "[WalkSeconds] >= 0");
                    table.ForeignKey(
                        name: "FK_TransferRules_Routes_FromRouteId_FromLineId",
                        columns: x => new { x.FromRouteId, x.FromLineId },
                        principalSchema: "Metro",
                        principalTable: "Routes",
                        principalColumns: new[] { "Id", "LineId" });
                    table.ForeignKey(
                        name: "FK_TransferRules_Routes_ToRouteId_ToLineId",
                        columns: x => new { x.ToRouteId, x.ToLineId },
                        principalSchema: "Metro",
                        principalTable: "Routes",
                        principalColumns: new[] { "Id", "LineId" });
                    table.ForeignKey(
                        name: "FK_TransferRules_StationLines_StationId_FromLineId",
                        columns: x => new { x.StationId, x.FromLineId },
                        principalSchema: "Metro",
                        principalTable: "StationLines",
                        principalColumns: new[] { "StationId", "LineId" });
                    table.ForeignKey(
                        name: "FK_TransferRules_StationLines_StationId_ToLineId",
                        columns: x => new { x.StationId, x.ToLineId },
                        principalSchema: "Metro",
                        principalTable: "StationLines",
                        principalColumns: new[] { "StationId", "LineId" });
                });

            migrationBuilder.CreateTable(
                name: "Trips",
                schema: "Metro",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    ScheduleGroupId = table.Column<short>(type: "smallint", nullable: false),
                    DayTypeCode = table.Column<string>(type: "varchar(30)", nullable: false),
                    DirectionStationId = table.Column<string>(type: "varchar(100)", nullable: false),
                    OriginStationId = table.Column<string>(type: "varchar(100)", nullable: false),
                    DestinationStationId = table.Column<string>(type: "varchar(100)", nullable: false),
                    IsExpress = table.Column<bool>(type: "bit", nullable: false),
                    DepartureTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    DepartureDayOffset = table.Column<byte>(type: "tinyint", nullable: false),
                    ArrivalTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    ArrivalDayOffset = table.Column<byte>(type: "tinyint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trips", x => x.Id);
                    table.CheckConstraint("CK_Trips_Arrival", "[ArrivalDayOffset] > [DepartureDayOffset] OR ([ArrivalDayOffset] = [DepartureDayOffset] AND [ArrivalTime] >= [DepartureTime])");
                    table.ForeignKey(
                        name: "FK_Trips_ScheduleGroups_ScheduleGroupId",
                        column: x => x.ScheduleGroupId,
                        principalSchema: "Metro",
                        principalTable: "ScheduleGroups",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Trips_ServiceDayTypes_DayTypeCode",
                        column: x => x.DayTypeCode,
                        principalSchema: "Metro",
                        principalTable: "ServiceDayTypes",
                        principalColumn: "Code");
                    table.ForeignKey(
                        name: "FK_Trips_Stations_DestinationStationId",
                        column: x => x.DestinationStationId,
                        principalSchema: "Metro",
                        principalTable: "Stations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Trips_Stations_DirectionStationId",
                        column: x => x.DirectionStationId,
                        principalSchema: "Metro",
                        principalTable: "Stations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Trips_Stations_OriginStationId",
                        column: x => x.OriginStationId,
                        principalSchema: "Metro",
                        principalTable: "Stations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StopTimes",
                schema: "Metro",
                columns: table => new
                {
                    TripId = table.Column<long>(type: "bigint", nullable: false),
                    Sequence = table.Column<short>(type: "smallint", nullable: false),
                    StationId = table.Column<string>(type: "varchar(100)", nullable: false),
                    ArrivalTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    ArrivalDayOffset = table.Column<byte>(type: "tinyint", nullable: false),
                    DepartureTime = table.Column<TimeOnly>(type: "time", nullable: true),
                    DepartureDayOffset = table.Column<byte>(type: "tinyint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StopTimes", x => new { x.TripId, x.Sequence });
                    table.CheckConstraint("CK_StopTimes_Departure", "([DepartureTime] IS NULL AND [DepartureDayOffset] IS NULL) OR\n([DepartureTime] IS NOT NULL AND [DepartureDayOffset] IS NOT NULL AND\n    ([DepartureDayOffset] > [ArrivalDayOffset] OR\n     ([DepartureDayOffset] = [ArrivalDayOffset] AND [DepartureTime] >= [ArrivalTime])))");
                    table.CheckConstraint("CK_StopTimes_Sequence", "[Sequence] > 0");
                    table.ForeignKey(
                        name: "FK_StopTimes_Stations_StationId",
                        column: x => x.StationId,
                        principalSchema: "Metro",
                        principalTable: "Stations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StopTimes_Trips_TripId",
                        column: x => x.TripId,
                        principalSchema: "Metro",
                        principalTable: "Trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "Metro",
                table: "ServiceDayTypes",
                columns: new[] { "Code", "Description" },
                values: new object[,]
                {
                    { "friday", "Friday and confirmed official-holiday timetable" },
                    { "saturday_wednesday", "Saturday through Wednesday timetable" },
                    { "thursday", "Thursday timetable" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                schema: "Identity",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                schema: "Identity",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true,
                filter: "[NormalizedName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                schema: "Identity",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                schema: "Identity",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                schema: "Identity",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                schema: "Identity",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                schema: "Identity",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true,
                filter: "[NormalizedUserName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FareProducts_Code",
                schema: "Metro",
                table: "FareProducts",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FavoriteStations_StationId",
                schema: "Metro",
                table: "FavoriteStations",
                column: "StationId");

            migrationBuilder.CreateIndex(
                name: "IX_HolidayEvents_ServiceDate",
                schema: "Metro",
                table: "HolidayEvents",
                column: "ServiceDate");

            migrationBuilder.CreateIndex(
                name: "IX_Routes_LineId",
                schema: "Metro",
                table: "Routes",
                column: "LineId");

            migrationBuilder.CreateIndex(
                name: "IX_RouteStops_StationId_RouteId_Sequence",
                schema: "Metro",
                table: "RouteStops",
                columns: new[] { "StationId", "RouteId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleGroups_FromTerminalId",
                schema: "Metro",
                table: "ScheduleGroups",
                column: "FromTerminalId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleGroups_RouteId",
                schema: "Metro",
                table: "ScheduleGroups",
                column: "RouteId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleGroups_ToTerminalId",
                schema: "Metro",
                table: "ScheduleGroups",
                column: "ToTerminalId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceAlertLines_LineId",
                schema: "Metro",
                table: "ServiceAlertLines",
                column: "LineId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceAlerts_StartsAtUtc",
                schema: "Metro",
                table: "ServiceAlerts",
                column: "StartsAtUtc",
                filter: "[PublishedAtUtc] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceAlertStations_StationId",
                schema: "Metro",
                table: "ServiceAlertStations",
                column: "StationId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceCalendarDates_DayTypeCode",
                schema: "Metro",
                table: "ServiceCalendarDates",
                column: "DayTypeCode");

            migrationBuilder.CreateIndex(
                name: "IX_StationEntrances_StationId_Code",
                schema: "Metro",
                table: "StationEntrances",
                columns: new[] { "StationId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StationLines_LineId",
                schema: "Metro",
                table: "StationLines",
                column: "LineId");

            migrationBuilder.CreateIndex(
                name: "IX_StopTimes_StationId_DepartureDayOffset_DepartureTime",
                schema: "Metro",
                table: "StopTimes",
                columns: new[] { "StationId", "DepartureDayOffset", "DepartureTime" });

            migrationBuilder.CreateIndex(
                name: "IX_TrackSegments_FromStationId_Status",
                schema: "Metro",
                table: "TrackSegments",
                columns: new[] { "FromStationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TrackSegments_RouteId",
                schema: "Metro",
                table: "TrackSegments",
                column: "RouteId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackSegments_ToStationId_Status",
                schema: "Metro",
                table: "TrackSegments",
                columns: new[] { "ToStationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TransferRules_FromRouteId_FromLineId",
                schema: "Metro",
                table: "TransferRules",
                columns: new[] { "FromRouteId", "FromLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_TransferRules_StationId_FromLineId_ToLineId_FromRouteId_ToRouteId",
                schema: "Metro",
                table: "TransferRules",
                columns: new[] { "StationId", "FromLineId", "ToLineId", "FromRouteId", "ToRouteId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransferRules_StationId_ToLineId",
                schema: "Metro",
                table: "TransferRules",
                columns: new[] { "StationId", "ToLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_TransferRules_ToRouteId_ToLineId",
                schema: "Metro",
                table: "TransferRules",
                columns: new[] { "ToRouteId", "ToLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_Trips_DayTypeCode",
                schema: "Metro",
                table: "Trips",
                column: "DayTypeCode");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_DestinationStationId",
                schema: "Metro",
                table: "Trips",
                column: "DestinationStationId");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_DirectionStationId",
                schema: "Metro",
                table: "Trips",
                column: "DirectionStationId");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_OriginStationId",
                schema: "Metro",
                table: "Trips",
                column: "OriginStationId");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_ScheduleGroupId_DayTypeCode",
                schema: "Metro",
                table: "Trips",
                columns: new[] { "ScheduleGroupId", "DayTypeCode" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AspNetRoleClaims",
                schema: "Identity");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims",
                schema: "Identity");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins",
                schema: "Identity");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles",
                schema: "Identity");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens",
                schema: "Identity");

            migrationBuilder.DropTable(
                name: "FarePrices",
                schema: "Metro");

            migrationBuilder.DropTable(
                name: "FavoriteStations",
                schema: "Metro");

            migrationBuilder.DropTable(
                name: "HolidayEvents",
                schema: "Metro");

            migrationBuilder.DropTable(
                name: "RouteStops",
                schema: "Metro");

            migrationBuilder.DropTable(
                name: "ServiceAlertLines",
                schema: "Metro");

            migrationBuilder.DropTable(
                name: "ServiceAlertStations",
                schema: "Metro");

            migrationBuilder.DropTable(
                name: "StationAmenities",
                schema: "Metro");

            migrationBuilder.DropTable(
                name: "StationEntrances",
                schema: "Metro");

            migrationBuilder.DropTable(
                name: "StopTimes",
                schema: "Metro");

            migrationBuilder.DropTable(
                name: "TrackSegments",
                schema: "Metro");

            migrationBuilder.DropTable(
                name: "TransferRules",
                schema: "Metro");

            migrationBuilder.DropTable(
                name: "AspNetRoles",
                schema: "Identity");

            migrationBuilder.DropTable(
                name: "FareProducts",
                schema: "Metro");

            migrationBuilder.DropTable(
                name: "AspNetUsers",
                schema: "Identity");

            migrationBuilder.DropTable(
                name: "ServiceCalendarDates",
                schema: "Metro");

            migrationBuilder.DropTable(
                name: "ServiceAlerts",
                schema: "Metro");

            migrationBuilder.DropTable(
                name: "Trips",
                schema: "Metro");

            migrationBuilder.DropTable(
                name: "StationLines",
                schema: "Metro");

            migrationBuilder.DropTable(
                name: "ScheduleGroups",
                schema: "Metro");

            migrationBuilder.DropTable(
                name: "ServiceDayTypes",
                schema: "Metro");

            migrationBuilder.DropTable(
                name: "Routes",
                schema: "Metro");

            migrationBuilder.DropTable(
                name: "Stations",
                schema: "Metro");

            migrationBuilder.DropTable(
                name: "Lines",
                schema: "Metro");
        }
    }
}
