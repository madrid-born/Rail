using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rail.Api.Migrations
{
    /// <inheritdoc />
    public partial class TrackSegmentGeometry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Preserve every existing connection and its original external identity.
            // SQL Server cannot convert a varchar key into an identity column in place.
            migrationBuilder.DropPrimaryKey("PK_TrackSegments", "TrackSegments", "Metro");
            migrationBuilder.RenameColumn("Id", "TrackSegments", "SourceId", "Metro");
            migrationBuilder.AddColumn<int>("Id", "TrackSegments", "int", schema: "Metro", nullable: false)
                .Annotation("SqlServer:Identity", "1, 1");
            migrationBuilder.AddPrimaryKey("PK_TrackSegments", "TrackSegments", "Id", "Metro");

            migrationBuilder.CreateTable(
                name: "TrackSegmentPoints",
                schema: "Metro",
                columns: table => new
                {
                    TrackSegmentId = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Latitude = table.Column<decimal>(type: "decimal(10,7)", precision: 10, scale: 7, nullable: false),
                    Longitude = table.Column<decimal>(type: "decimal(10,7)", precision: 10, scale: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackSegmentPoints", x => new { x.TrackSegmentId, x.Sequence });
                    table.CheckConstraint("CK_TrackSegmentPoints_Latitude", "[Latitude] BETWEEN -90 AND 90");
                    table.CheckConstraint("CK_TrackSegmentPoints_Longitude", "[Longitude] BETWEEN -180 AND 180");
                    table.CheckConstraint("CK_TrackSegmentPoints_Sequence", "[Sequence] >= 0");
                    table.ForeignKey(
                        name: "FK_TrackSegmentPoints_TrackSegments_TrackSegmentId",
                        column: x => x.TrackSegmentId,
                        principalSchema: "Metro",
                        principalTable: "TrackSegments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrackSegments_SourceId",
                schema: "Metro",
                table: "TrackSegments",
                column: "SourceId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrackSegmentPoints",
                schema: "Metro");

            migrationBuilder.DropIndex(
                name: "IX_TrackSegments_SourceId",
                schema: "Metro",
                table: "TrackSegments");

            migrationBuilder.DropPrimaryKey("PK_TrackSegments", "TrackSegments", "Metro");
            migrationBuilder.DropColumn("Id", "TrackSegments", "Metro");
            migrationBuilder.RenameColumn("SourceId", "TrackSegments", "Id", "Metro");
            migrationBuilder.AddPrimaryKey("PK_TrackSegments", "TrackSegments", "Id", "Metro");
        }
    }
}
