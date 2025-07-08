using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetAndGreet.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFingerprintMetadataToDevice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_Channels_ChannelId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_ChannelId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ChannelId",
                table: "Users");

            migrationBuilder.AddColumn<string>(
                name: "FingerprintMetadata",
                table: "TrustedDevices",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FingerprintMetadata",
                table: "TrustedDevices");

            migrationBuilder.AddColumn<Guid>(
                name: "ChannelId",
                table: "Users",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_ChannelId",
                table: "Users",
                column: "ChannelId");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Channels_ChannelId",
                table: "Users",
                column: "ChannelId",
                principalTable: "Channels",
                principalColumn: "Id");
        }
    }
}
