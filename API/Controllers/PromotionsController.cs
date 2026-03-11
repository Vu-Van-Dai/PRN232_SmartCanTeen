using API.Services;
using Application.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers
{
    [ApiController]
    [Route("api/promotions")]
    [Authorize]
    public class PromotionsController : ControllerBase
    {
        private readonly PromotionEngine _engine;

        public PromotionsController(PromotionEngine engine)
        {
            _engine = engine;
        }

        [HttpPost("quote")]
        public async Task<IActionResult> Quote([FromBody] PromotionQuoteRequest request, CancellationToken ct)
        {
            if (request.Items == null || request.Items.Count == 0)
                return Ok(new
                {
                    items = Array.Empty<object>(),
                    discountAmount = 0m,
                    grossTotal = 0m,
                    total = 0m,
                    appliedPromotionCode = (string?)null,
                    appliedPromotionName = (string?)null
                });

            var (result, error) = await _engine.QuoteAsync(
                request.Items.Select(x => (x.ItemId, x.Quantity)),
                request.PromoCode,
                ct);

            if (error != null)
                return BadRequest(error);

            return Ok(new
            {
                items = result!.Items.Select(x => new { itemId = x.itemId, quantity = x.quantity }).ToList(),
                discountAmount = result.DiscountAmount,
                grossTotal = result.GrossTotal,
                total = result.Total,
                appliedPromotionCode = result.AppliedPromotionCode,
                appliedPromotionName = result.AppliedPromotionName
            });
        }
    }
}
