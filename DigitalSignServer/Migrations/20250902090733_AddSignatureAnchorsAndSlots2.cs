using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigitalSignServer.Migrations
{
    /// <inheritdoc />
    public partial class AddSignatureAnchorsAndSlots2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TemplateInstanceSignatureSlots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SlotKey = table.Column<string>(type: "text", nullable: false),
                    PageIndex = table.Column<int>(type: "integer", nullable: false),
                    X = table.Column<double>(type: "double precision", nullable: false),
                    Y = table.Column<double>(type: "double precision", nullable: false),
                    W = table.Column<double>(type: "double precision", nullable: false),
                    H = table.Column<double>(type: "double precision", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemplateInstanceSignatureSlots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TemplateSignatureAnchors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tag = table.Column<string>(type: "text", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Meta = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemplateSignatureAnchors", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TemplateInstanceSignatureSlots");

            migrationBuilder.DropTable(
                name: "TemplateSignatureAnchors");
        }
    }
}
