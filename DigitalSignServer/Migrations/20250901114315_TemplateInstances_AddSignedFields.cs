using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigitalSignServer.Migrations
{
    /// <inheritdoc />
    public partial class TemplateInstances_AddSignedFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PdfSha256",
                table: "TemplateInstances",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SignedAt",
                table: "TemplateInstances",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignedPdfS3Key",
                table: "TemplateInstances",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SignatureInvites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Token = table.Column<string>(type: "text", nullable: false),
                    OtpHash = table.Column<string>(type: "text", nullable: false),
                    OtpExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RequiresPassword = table.Column<bool>(type: "boolean", nullable: false),
                    DeliveryChannel = table.Column<string>(type: "text", nullable: false),
                    RecipientEmail = table.Column<string>(type: "text", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MaxUses = table.Column<int>(type: "integer", nullable: false),
                    Uses = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    SignerName = table.Column<string>(type: "text", nullable: true),
                    SignerEmail = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OpenedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignatureInvites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SignatureInvites_TemplateInstances_TemplateInstanceId",
                        column: x => x.TemplateInstanceId,
                        principalTable: "TemplateInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SignatureDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InviteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ProviderMessageId = table.Column<string>(type: "text", nullable: true),
                    AttemptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignatureDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SignatureDeliveries_SignatureInvites_InviteId",
                        column: x => x.InviteId,
                        principalTable: "SignatureInvites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SignatureDeliveries_InviteId",
                table: "SignatureDeliveries",
                column: "InviteId");

            migrationBuilder.CreateIndex(
                name: "IX_SignatureInvites_TemplateInstanceId",
                table: "SignatureInvites",
                column: "TemplateInstanceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SignatureDeliveries");

            migrationBuilder.DropTable(
                name: "SignatureInvites");

            migrationBuilder.DropColumn(
                name: "PdfSha256",
                table: "TemplateInstances");

            migrationBuilder.DropColumn(
                name: "SignedAt",
                table: "TemplateInstances");

            migrationBuilder.DropColumn(
                name: "SignedPdfS3Key",
                table: "TemplateInstances");
        }
    }
}
