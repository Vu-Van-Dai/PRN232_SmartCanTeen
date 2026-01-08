using Application.Payments;
using Core.Entities;
using Core.Enums;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace API.Controllers
{
    public class PosController : ControllerBase
    {
        [Authorize(Roles = "Staff")]
        [HttpPost("create-qr")]
        public async Task<IActionResult> CreateQr(
    decimal amount,
    AppDbContext db,
    VnpayService vnpay,
    ICurrentCampusService campus)
        {
            var shift = await db.Shifts.FirstAsync(x =>
                x.CampusId == campus.CampusId &&
                x.Status == ShiftStatus.Open);

            var txnRef = Guid.NewGuid().ToString("N");

            db.VnpayTransactions.Add(new VnpayTransaction
            {
                Id = Guid.NewGuid(),
                VnpTxnRef = txnRef,
                Amount = amount,
                Purpose = PaymentPurpose.OfflineOrder,
                ShiftId = shift.Id
            });

            await db.SaveChangesAsync();

            var url = vnpay.CreatePaymentUrl(amount, txnRef, "Thanh toan POS");

            return Ok(new { qrUrl = url });
        }
    }
}
