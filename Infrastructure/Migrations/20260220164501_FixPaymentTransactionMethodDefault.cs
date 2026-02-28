using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixPaymentTransactionMethodDefault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing rows were created before PaymentMethod existed and ended up as 0.
            // All existing PaymentTransactions in this system are PayOS-based (QR), so map 0 -> Qr (3).
            migrationBuilder.Sql("UPDATE \"PaymentTransactions\" SET \"PaymentMethod\" = 3 WHERE \"PaymentMethod\" = 0;");

            // Default to Qr for safety (new code always sets PaymentMethod explicitly).
            migrationBuilder.Sql("ALTER TABLE \"PaymentTransactions\" ALTER COLUMN \"PaymentMethod\" SET DEFAULT 3;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE \"PaymentTransactions\" ALTER COLUMN \"PaymentMethod\" SET DEFAULT 0;");
        }
    }
}
