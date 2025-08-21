using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigitalSignServer.Migrations
{
    /// <inheritdoc />
    public partial class changefrompasswordhashtopassword : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PasswordHash",
                table: "customers",
                newName: "Password");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Password",
                table: "customers",
                newName: "PasswordHash");
        }
    }
}
