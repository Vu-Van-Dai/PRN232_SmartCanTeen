using API.Hubs;
using Application.Orders;
using Application.Payments;
using Core.Entities;
using Core.Enums;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using API.Services;

namespace API.Controllers
{
    [ApiController]
    [Route("api/payos")]
    public class PayosController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly PayosService _payos;
        private readonly PayosPaymentProcessor _processor;

        public PayosController(
            AppDbContext db,
            PayosService payos,
            PayosPaymentProcessor processor)
        {
            _db = db;
            _payos = payos;
            _processor = processor;
        }

        // Local-dev fallback: PayOS dashboard/webhook usually can't reach localhost.
        // These endpoints allow the FE return/cancel pages to confirm the payment outcome.
        // NOTE: This is not a replacement for webhooks in production.

        [HttpPost("confirm")]
        public async Task<IActionResult> Confirm([FromQuery] int orderCode, [FromQuery] string? status = null)
        {
            if (orderCode <= 0) return BadRequest("Invalid orderCode");

            // Accept only PAID confirmations
            if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "PAID", StringComparison.OrdinalIgnoreCase))
                return Ok();

            await _processor.TryMarkPaidAsync(orderCode, HttpContext.RequestAborted);
            return Ok();
        }

        [HttpPost("cancel")]
        public async Task<IActionResult> Cancel([FromQuery] int orderCode)
        {
            if (orderCode <= 0) return BadRequest("Invalid orderCode");

            var payRef = $"PAYOS-{orderCode}";
            var txn = await _db.PaymentTransactions.FirstOrDefaultAsync(x => x.PaymentRef == payRef);
            if (txn == null || txn.IsSuccess)
                return Ok();

            // Intentionally no DB state change here:
            // - In local dev, we rely on FE to bring the user back to POS.
            // - The pending order is kept so cashier can switch to CASH without creating duplicates.
            // - If the cashier wants to void the order, call /api/pos/orders/{orderId}/cancel.
            return Ok();
        }

        [HttpPost("webhook")]
        public async Task<IActionResult> Webhook([FromBody] PayosWebhookPayload payload)
        {
            // Always be tolerant: if DB was recreated after a payment link was generated,
            // we no-op instead of throwing (prevents "payment failed" loops).
            if (payload == null)
                return Ok();

            if (!_payos.VerifyWebhookSignature(payload.Data, payload.Signature))
                return BadRequest();

            // Accept only successful notifications
            if (!payload.Success || payload.Code != "00")
                return Ok();

            var orderCode = TryGetInt(payload.Data, "orderCode");
            if (orderCode == null)
                return Ok();

            // Some payloads also include a nested success code inside data
            var dataCode = TryGetString(payload.Data, "code");
            if (!string.IsNullOrWhiteSpace(dataCode) && dataCode != "00")
                return Ok();

            var payRef = $"PAYOS-{orderCode.Value}";

            // If txn is missing or already processed, the processor will no-op.
            await _processor.TryMarkPaidAsync(orderCode.Value, HttpContext.RequestAborted);
            return Ok();
        }

        private static int? TryGetInt(System.Collections.Generic.Dictionary<string, object?> data, string key)
        {
            if (!data.TryGetValue(key, out var value) || value == null)
                return null;

            if (value is int i) return i;
            if (value is long l) return checked((int)l);

            if (value is JsonElement el)
            {
                if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var j))
                    return j;

                if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var s))
                    return s;
            }

            if (value is string str && int.TryParse(str, out var parsed))
                return parsed;

            return null;
        }

        private static string? TryGetString(System.Collections.Generic.Dictionary<string, object?> data, string key)
        {
            if (!data.TryGetValue(key, out var value) || value == null)
                return null;

            if (value is string s) return s;
            if (value is JsonElement el && el.ValueKind == JsonValueKind.String)
                return el.GetString();

            return value.ToString();
        }
    }
}
