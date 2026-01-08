using Core.Enums;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace API.Controllers
{
    [ApiController]
    [Route("api/vnpay")]
    public class VnpayController : ControllerBase
    {
        [HttpGet("ipn")]
        public async Task<IActionResult> Ipn(
            AppDbContext db,
            IConfiguration config)
        {
            var vnpTxnRef = Request.Query["vnp_TxnRef"].ToString();
            var responseCode = Request.Query["vnp_ResponseCode"].ToString();

            var txn = await db.VnpayTransactions
                .FirstAsync(x => x.VnpTxnRef == vnpTxnRef);

            if (responseCode != "00")
                return Ok();

            txn.IsSuccess = true;

            if (txn.Purpose == PaymentPurpose.WalletTopup)
            {
                var wallet = await db.Wallets.FindAsync(txn.WalletId);
                wallet!.Balance += txn.Amount;
            }

            if (txn.Purpose == PaymentPurpose.OfflineOrder)
            {
                var shift = await db.Shifts.FindAsync(txn.ShiftId);
                shift!.SystemQrTotal += txn.Amount;
            }

            await db.SaveChangesAsync();
            return Ok();
        }
    }

}
