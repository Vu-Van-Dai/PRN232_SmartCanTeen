using Application.DTOs.Promotions;
using Core.Entities;
using Core.Enums;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace API.Controllers
{
    [ApiController]
    [Route("api/admin/promotions")]
    [Authorize(Roles = "AdminSystem")]
    public class AdminPromotionsController : ControllerBase
    {
        private readonly AppDbContext _db;

        public AdminPromotionsController(AppDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> GetList()
        {
            var items = await _db.Promotions
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new PromotionResponse
                {
                    Id = x.Id,
                    Name = x.Name,
                    Code = x.Code,
                    Type = x.Type.ToString(),
                    IsActive = x.IsActive,
                    StartAt = x.StartAt,
                    EndAt = x.EndAt,
                    ConfigJson = x.ConfigJson,
                    CreatedAt = x.CreatedAt
                })
                .ToListAsync();

            return Ok(items);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> Get(Guid id)
        {
            var promo = await _db.Promotions
                .Where(x => x.Id == id)
                .Select(x => new PromotionResponse
                {
                    Id = x.Id,
                    Name = x.Name,
                    Code = x.Code,
                    Type = x.Type.ToString(),
                    IsActive = x.IsActive,
                    StartAt = x.StartAt,
                    EndAt = x.EndAt,
                    ConfigJson = x.ConfigJson,
                    CreatedAt = x.CreatedAt
                })
                .FirstOrDefaultAsync();

            if (promo == null)
                return NotFound();

            return Ok(promo);
        }

        [HttpPost]
        public async Task<IActionResult> Create(CreatePromotionRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return BadRequest("Name is required");

            if (string.IsNullOrWhiteSpace(request.Code))
                return BadRequest("Code is required");

            if (!Enum.TryParse<PromotionType>(request.Type, ignoreCase: true, out var type))
                return BadRequest("Invalid promotion type");

            var code = request.Code.Trim();

            var exists = await _db.Promotions.AnyAsync(x => x.Code == code);
            if (exists)
                return BadRequest("Code already exists");

            if (request.StartAt.HasValue && request.EndAt.HasValue && request.StartAt > request.EndAt)
                return BadRequest("StartAt must be <= EndAt");

            if (request.Config.ValueKind != JsonValueKind.Object)
                return BadRequest("Config must be an object");

            var validationError = ValidateConfig(type, request.Config);
            if (validationError != null)
                return BadRequest(validationError);

            var promo = new Promotion
            {
                Id = Guid.NewGuid(),
                Name = request.Name.Trim(),
                Code = code,
                Type = type,
                IsActive = request.IsActive,
                StartAt = NormalizeUtc(request.StartAt),
                EndAt = NormalizeUtc(request.EndAt),
                ConfigJson = request.Config.GetRawText(),
                IsDeleted = false,
                CreatedAt = DateTime.UtcNow
            };

            _db.Promotions.Add(promo);
            await _db.SaveChangesAsync();

            return Ok(promo.Id);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(Guid id, UpdatePromotionRequest request)
        {
            var promo = await _db.Promotions.FirstOrDefaultAsync(x => x.Id == id);
            if (promo == null)
                return NotFound();

            if (string.IsNullOrWhiteSpace(request.Name))
                return BadRequest("Name is required");

            if (string.IsNullOrWhiteSpace(request.Code))
                return BadRequest("Code is required");

            if (!Enum.TryParse<PromotionType>(request.Type, ignoreCase: true, out var type))
                return BadRequest("Invalid promotion type");

            var code = request.Code.Trim();

            var duplicate = await _db.Promotions.AnyAsync(x => x.Id != id && x.Code == code);
            if (duplicate)
                return BadRequest("Code already exists");

            if (request.StartAt.HasValue && request.EndAt.HasValue && request.StartAt > request.EndAt)
                return BadRequest("StartAt must be <= EndAt");

            if (request.Config.ValueKind != JsonValueKind.Object)
                return BadRequest("Config must be an object");

            var validationError = ValidateConfig(type, request.Config);
            if (validationError != null)
                return BadRequest(validationError);

            promo.Name = request.Name.Trim();
            promo.Code = code;
            promo.Type = type;
            promo.IsActive = request.IsActive;
            promo.StartAt = NormalizeUtc(request.StartAt);
            promo.EndAt = NormalizeUtc(request.EndAt);
            promo.ConfigJson = request.Config.GetRawText();

            await _db.SaveChangesAsync();
            return Ok();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var promo = await _db.Promotions.FirstOrDefaultAsync(x => x.Id == id);
            if (promo == null)
                return NotFound();

            promo.IsDeleted = true;
            await _db.SaveChangesAsync();
            return Ok();
        }

        private static DateTime? NormalizeUtc(DateTime? dt)
        {
            if (!dt.HasValue) return null;
            if (dt.Value.Kind == DateTimeKind.Utc) return dt.Value;
            if (dt.Value.Kind == DateTimeKind.Local) return dt.Value.ToUniversalTime();
            return DateTime.SpecifyKind(dt.Value, DateTimeKind.Utc);
        }

        private static string? ValidateConfig(PromotionType type, JsonElement config)
        {
            static bool HasGuid(JsonElement obj, string prop)
            {
                return obj.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String && Guid.TryParse(v.GetString(), out _);
            }

            static bool HasPositiveInt(JsonElement obj, string prop)
            {
                if (!obj.TryGetProperty(prop, out var v)) return false;
                if (v.ValueKind != JsonValueKind.Number) return false;
                return v.TryGetInt32(out var n) && n > 0;
            }

            static bool HasPercent(JsonElement obj, string prop)
            {
                if (!obj.TryGetProperty(prop, out var v)) return false;
                if (v.ValueKind != JsonValueKind.Number) return false;
                if (!v.TryGetDecimal(out var d)) return false;
                return d > 0 && d <= 100;
            }

            switch (type)
            {
                case PromotionType.BuyXGetY:
                    if (!HasGuid(config, "buyItemId")) return "Config.buyItemId must be a GUID";
                    if (!HasPositiveInt(config, "buyQuantity")) return "Config.buyQuantity must be a positive integer";
                    if (!HasGuid(config, "getItemId")) return "Config.getItemId must be a GUID";
                    if (!HasPositiveInt(config, "getQuantity")) return "Config.getQuantity must be a positive integer";
                    return null;

                case PromotionType.CategoryDiscount:
                    if (!HasGuid(config, "categoryId")) return "Config.categoryId must be a GUID";
                    if (!HasPercent(config, "discountPercent")) return "Config.discountPercent must be in (0, 100]";
                    return null;

                case PromotionType.Bundle:
                    if (!config.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                        return "Config.items must be an array";

                    if (!config.TryGetProperty("bundlePrice", out var price) || price.ValueKind != JsonValueKind.Number)
                        return "Config.bundlePrice must be a number";

                    if (items.GetArrayLength() <= 0)
                        return "Config.items must have at least one entry";

                    foreach (var it in items.EnumerateArray())
                    {
                        if (it.ValueKind != JsonValueKind.Object)
                            return "Config.items entries must be objects";

                        if (!it.TryGetProperty("itemId", out var itemId) || itemId.ValueKind != JsonValueKind.String || !Guid.TryParse(itemId.GetString(), out _))
                            return "Config.items[].itemId must be a GUID";

                        if (!it.TryGetProperty("quantity", out var qty) || qty.ValueKind != JsonValueKind.Number || !qty.TryGetInt32(out var q) || q <= 0)
                            return "Config.items[].quantity must be a positive integer";
                    }

                    return null;

                case PromotionType.Clearance:
                    if (!HasGuid(config, "itemId")) return "Config.itemId must be a GUID";
                    if (!HasPercent(config, "discountPercent")) return "Config.discountPercent must be in (0, 100]";
                    return null;

                case PromotionType.BuyMoreSaveMore:
                    if (!config.TryGetProperty("tiers", out var tiers) || tiers.ValueKind != JsonValueKind.Array)
                        return "Config.tiers must be an array";

                    if (tiers.GetArrayLength() <= 0)
                        return "Config.tiers must have at least one entry";

                    foreach (var t in tiers.EnumerateArray())
                    {
                        if (t.ValueKind != JsonValueKind.Object)
                            return "Config.tiers entries must be objects";

                        if (!t.TryGetProperty("minQuantity", out var minQ) || minQ.ValueKind != JsonValueKind.Number || !minQ.TryGetInt32(out var mq) || mq <= 0)
                            return "Config.tiers[].minQuantity must be a positive integer";

                        if (!t.TryGetProperty("discountPercent", out var dp) || dp.ValueKind != JsonValueKind.Number || !dp.TryGetDecimal(out var d) || d <= 0 || d > 100)
                            return "Config.tiers[].discountPercent must be in (0, 100]";
                    }

                    return null;

                default:
                    return "Unsupported promotion type";
            }
        }
    }
}
