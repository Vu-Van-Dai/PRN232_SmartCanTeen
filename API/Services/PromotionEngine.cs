using Core.Entities;
using Core.Enums;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace API.Services
{
    internal sealed record MenuItemSnapshot(Guid Id, decimal Price, Guid CategoryId);

    public class PromotionQuoteResult
    {
        public List<(Guid itemId, int quantity)> Items { get; init; } = new();
        public decimal DiscountAmount { get; init; }
        public decimal GrossTotal { get; init; }
        public decimal Total { get; init; }
        public string? AppliedPromotionCode { get; init; }
        public string? AppliedPromotionName { get; init; }
    }

    public class PromotionEngine
    {
        private readonly AppDbContext _db;

        public PromotionEngine(AppDbContext db)
        {
            _db = db;
        }

        public async Task<(PromotionQuoteResult? result, string? error)> QuoteAsync(
            IEnumerable<(Guid itemId, int quantity)> requestedItems,
            string? promoCode,
            CancellationToken ct = default)
        {
            var normalized = NormalizeItems(requestedItems, out var invalid);
            if (invalid)
                return (null, "Invalid quantity");

            if (normalized.Count == 0)
                return (new PromotionQuoteResult
                {
                    Items = new(),
                    DiscountAmount = 0,
                    GrossTotal = 0,
                    Total = 0
                }, null);

            var nowUtc = DateTime.UtcNow;

            // Load active promotions (or by code)
            IQueryable<Promotion> promoQuery = _db.Promotions
                .Where(x => !x.IsDeleted && x.IsActive)
                .Where(x => x.StartAt == null || x.StartAt <= nowUtc)
                .Where(x => x.EndAt == null || x.EndAt >= nowUtc);

            Promotion? forcedPromotion = null;
            var trimmedCode = string.IsNullOrWhiteSpace(promoCode) ? null : promoCode.Trim();
            if (trimmedCode != null)
            {
                forcedPromotion = await promoQuery
                    .FirstOrDefaultAsync(x => x.Code.ToUpper() == trimmedCode.ToUpper(), ct);

                if (forcedPromotion == null)
                    return (null, "Mã khuyến mãi không hợp lệ hoặc đã hết hạn.");
            }

            var candidates = forcedPromotion != null
                ? new List<Promotion> { forcedPromotion }
                : await promoQuery
                    .OrderByDescending(x => x.CreatedAt)
                    .ToListAsync(ct);

            // Determine all menu items needed for evaluation.
            var requestedIds = normalized.Keys.ToHashSet();
            var referencedIds = new HashSet<Guid>();
            foreach (var p in candidates)
            {
                TryCollectReferencedItemIds(p, referencedIds);
            }

            var allItemIds = requestedIds.Union(referencedIds).ToList();

            var menuItems = await _db.MenuItems
                .Where(x => allItemIds.Contains(x.Id) && x.IsActive && !x.IsDeleted)
                .Select(x => new MenuItemSnapshot(x.Id, x.Price, x.CategoryId))
                .ToDictionaryAsync(x => x.Id, ct);

            // Requested items must exist.
            foreach (var id in requestedIds)
            {
                if (!menuItems.ContainsKey(id))
                    return (null, $"Item {id} not found");
            }

            PromotionQuoteResult? best = null;

            foreach (var promo in candidates)
            {
                var (candidate, candidateError) = TryApplyPromotion(promo, normalized, menuItems);
                if (candidateError != null)
                {
                    // If promo is forced, surface error.
                    if (forcedPromotion != null)
                        return (null, candidateError);
                    continue;
                }

                if (best == null || candidate!.DiscountAmount > best.DiscountAmount)
                    best = candidate;
            }

            // If forced promotion exists but yields 0 discount, still return it (to be transparent).
            if (forcedPromotion != null && best == null)
            {
                var gross = ComputeGrossTotal(normalized, menuItems);
                return (new PromotionQuoteResult
                {
                    Items = normalized.Select(kv => (kv.Key, kv.Value)).ToList(),
                    GrossTotal = gross,
                    DiscountAmount = 0,
                    Total = gross,
                    AppliedPromotionCode = forcedPromotion.Code,
                    AppliedPromotionName = forcedPromotion.Name
                }, null);
            }

            // No promotion applied.
            if (best == null)
            {
                var gross = ComputeGrossTotal(normalized, menuItems);
                return (new PromotionQuoteResult
                {
                    Items = normalized.Select(kv => (kv.Key, kv.Value)).ToList(),
                    GrossTotal = gross,
                    DiscountAmount = 0,
                    Total = gross
                }, null);
            }

            return (best, null);
        }

        private static Dictionary<Guid, int> NormalizeItems(IEnumerable<(Guid itemId, int quantity)> items, out bool invalid)
        {
            invalid = false;
            var dict = new Dictionary<Guid, int>();
            foreach (var (itemId, quantity) in items)
            {
                if (quantity <= 0)
                {
                    invalid = true;
                    continue;
                }

                if (dict.TryGetValue(itemId, out var existing))
                    dict[itemId] = existing + quantity;
                else
                    dict[itemId] = quantity;
            }
            return dict;
        }

        private static decimal ComputeGrossTotal(Dictionary<Guid, int> qtyByItemId, Dictionary<Guid, MenuItemSnapshot> menuItems)
        {
            decimal sum = 0;
            foreach (var kv in qtyByItemId)
            {
                var m = menuItems[kv.Key];
                sum += m.Price * kv.Value;
            }
            return sum;
        }

        private static void TryCollectReferencedItemIds(Promotion promo, HashSet<Guid> ids)
        {
            try
            {
                using var doc = JsonDocument.Parse(promo.ConfigJson);
                var root = doc.RootElement;

                switch (promo.Type)
                {
                    case PromotionType.BuyXGetY:
                        if (root.TryGetProperty("buyItemId", out var buyId) && Guid.TryParse(buyId.GetString(), out var b))
                            ids.Add(b);
                        if (root.TryGetProperty("getItemId", out var getId) && Guid.TryParse(getId.GetString(), out var g))
                            ids.Add(g);
                        break;

                    case PromotionType.Clearance:
                        if (root.TryGetProperty("itemId", out var itemId) && Guid.TryParse(itemId.GetString(), out var c))
                            ids.Add(c);
                        break;

                    case PromotionType.Bundle:
                        if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var it in items.EnumerateArray())
                            {
                                if (it.ValueKind != JsonValueKind.Object) continue;
                                if (it.TryGetProperty("itemId", out var iid) && Guid.TryParse(iid.GetString(), out var bid))
                                    ids.Add(bid);
                            }
                        }
                        break;

                    case PromotionType.BuyMoreSaveMore:
                    case PromotionType.CategoryDiscount:
                    default:
                        break;
                }
            }
            catch
            {
                // ignore malformed config for collection; it will be handled during apply
            }
        }

        private static (PromotionQuoteResult? result, string? error) TryApplyPromotion(
            Promotion promo,
            Dictionary<Guid, int> requestedQty,
            Dictionary<Guid, MenuItemSnapshot> menuItems)
        {
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(promo.ConfigJson);
            }
            catch
            {
                return (null, "Cấu hình khuyến mãi không hợp lệ.");
            }

            using (doc)
            {
                var root = doc.RootElement;

                // start from requested quantities
                var finalQty = requestedQty.ToDictionary(k => k.Key, v => v.Value);
                var gross = ComputeGrossTotal(finalQty, menuItems);

                decimal discount = 0;

                switch (promo.Type)
                {
                    case PromotionType.BuyXGetY:
                    {
                        if (!root.TryGetProperty("buyItemId", out var buyIdEl) || !Guid.TryParse(buyIdEl.GetString(), out var buyItemId))
                            return (null, "Config.buyItemId invalid");
                        if (!root.TryGetProperty("buyQuantity", out var buyQtyEl) || !buyQtyEl.TryGetInt32(out var buyQty) || buyQty <= 0)
                            return (null, "Config.buyQuantity invalid");
                        if (!root.TryGetProperty("getItemId", out var getIdEl) || !Guid.TryParse(getIdEl.GetString(), out var getItemId))
                            return (null, "Config.getItemId invalid");
                        if (!root.TryGetProperty("getQuantity", out var getQtyEl) || !getQtyEl.TryGetInt32(out var getQty) || getQty <= 0)
                            return (null, "Config.getQuantity invalid");

                        if (!requestedQty.TryGetValue(buyItemId, out var actualBuyQty) || actualBuyQty < buyQty)
                            return (null, "Chưa đủ điều kiện áp dụng khuyến mãi.");

                        if (!menuItems.ContainsKey(getItemId) || !menuItems.ContainsKey(buyItemId))
                            return (null, "Sản phẩm áp dụng khuyến mãi không còn khả dụng.");

                        var times = actualBuyQty / buyQty;
                        var freeQty = times * getQty;
                        if (freeQty <= 0)
                            return (null, "Chưa đủ điều kiện áp dụng khuyến mãi.");

                        // Auto-add free items to the order lines.
                        if (finalQty.TryGetValue(getItemId, out var existing))
                            finalQty[getItemId] = existing + freeQty;
                        else
                            finalQty[getItemId] = freeQty;

                        // Recompute gross including free items at full price, then discount them.
                        gross = ComputeGrossTotal(finalQty, menuItems);
                        var getPrice = menuItems[getItemId].Price;
                        discount = getPrice * freeQty;
                        break;
                    }

                    case PromotionType.CategoryDiscount:
                    {
                        if (!root.TryGetProperty("categoryId", out var catEl) || !Guid.TryParse(catEl.GetString(), out var categoryId))
                            return (null, "Config.categoryId invalid");
                        if (!root.TryGetProperty("discountPercent", out var dpEl) || !dpEl.TryGetDecimal(out var percent) || percent <= 0 || percent > 100)
                            return (null, "Config.discountPercent invalid");

                        decimal affected = 0;
                        foreach (var kv in requestedQty)
                        {
                            var m = menuItems[kv.Key];
                            if (m.CategoryId == categoryId)
                                affected += m.Price * kv.Value;
                        }

                        if (affected <= 0)
                            return (null, "Chưa đủ điều kiện áp dụng khuyến mãi.");

                        discount = affected * (percent / 100m);
                        break;
                    }

                    case PromotionType.Clearance:
                    {
                        if (!root.TryGetProperty("itemId", out var itemEl) || !Guid.TryParse(itemEl.GetString(), out var itemId))
                            return (null, "Config.itemId invalid");
                        if (!root.TryGetProperty("discountPercent", out var dpEl) || !dpEl.TryGetDecimal(out var percent) || percent <= 0 || percent > 100)
                            return (null, "Config.discountPercent invalid");

                        if (!requestedQty.TryGetValue(itemId, out var q) || q <= 0)
                            return (null, "Chưa đủ điều kiện áp dụng khuyến mãi.");

                        if (!menuItems.ContainsKey(itemId))
                            return (null, "Sản phẩm áp dụng khuyến mãi không còn khả dụng.");

                        var price = menuItems[itemId].Price;
                        discount = price * q * (percent / 100m);
                        break;
                    }

                    case PromotionType.Bundle:
                    {
                        if (!root.TryGetProperty("items", out var itemsEl) || itemsEl.ValueKind != JsonValueKind.Array)
                            return (null, "Config.items invalid");
                        if (!root.TryGetProperty("bundlePrice", out var priceEl) || !priceEl.TryGetDecimal(out var bundlePrice) || bundlePrice <= 0)
                            return (null, "Config.bundlePrice invalid");

                        var requirements = new List<(Guid itemId, int qty)>();
                        foreach (var it in itemsEl.EnumerateArray())
                        {
                            if (it.ValueKind != JsonValueKind.Object) return (null, "Config.items invalid");
                            if (!it.TryGetProperty("itemId", out var iid) || !Guid.TryParse(iid.GetString(), out var id))
                                return (null, "Config.items[].itemId invalid");
                            if (!it.TryGetProperty("quantity", out var qEl) || !qEl.TryGetInt32(out var rq) || rq <= 0)
                                return (null, "Config.items[].quantity invalid");
                            requirements.Add((id, rq));
                        }

                        if (requirements.Count == 0)
                            return (null, "Chưa đủ điều kiện áp dụng khuyến mãi.");

                        int bundleCount = int.MaxValue;
                        foreach (var (id, rq) in requirements)
                        {
                            if (!requestedQty.TryGetValue(id, out var have) || have < rq)
                                return (null, "Chưa đủ điều kiện áp dụng khuyến mãi.");

                            bundleCount = Math.Min(bundleCount, have / rq);
                        }

                        if (bundleCount <= 0 || bundleCount == int.MaxValue)
                            return (null, "Chưa đủ điều kiện áp dụng khuyến mãi.");

                        decimal bundleGross = 0;
                        foreach (var (id, rq) in requirements)
                        {
                            if (!menuItems.ContainsKey(id))
                                return (null, "Sản phẩm áp dụng khuyến mãi không còn khả dụng.");
                            bundleGross += menuItems[id].Price * rq;
                        }

                        var perBundleDiscount = bundleGross - bundlePrice;
                        if (perBundleDiscount <= 0)
                            return (null, "Chưa đủ điều kiện áp dụng khuyến mãi.");

                        discount = perBundleDiscount * bundleCount;
                        break;
                    }

                    case PromotionType.BuyMoreSaveMore:
                    {
                        if (!root.TryGetProperty("tiers", out var tiersEl) || tiersEl.ValueKind != JsonValueKind.Array)
                            return (null, "Config.tiers invalid");

                        var totalQty = requestedQty.Values.Sum();
                        if (totalQty <= 0)
                            return (null, "Chưa đủ điều kiện áp dụng khuyến mãi.");

                        decimal bestPercent = 0;
                        foreach (var t in tiersEl.EnumerateArray())
                        {
                            if (t.ValueKind != JsonValueKind.Object) continue;
                            if (!t.TryGetProperty("minQuantity", out var minEl) || !minEl.TryGetInt32(out var minQ) || minQ <= 0)
                                continue;
                            if (!t.TryGetProperty("discountPercent", out var dpEl) || !dpEl.TryGetDecimal(out var percent) || percent <= 0 || percent > 100)
                                continue;

                            if (totalQty >= minQ)
                                bestPercent = Math.Max(bestPercent, percent);
                        }

                        if (bestPercent <= 0)
                            return (null, "Chưa đủ điều kiện áp dụng khuyến mãi.");

                        discount = gross * (bestPercent / 100m);
                        break;
                    }

                    default:
                        return (null, "Unsupported promotion type");
                }

                // Round discount to currency.
                discount = decimal.Round(discount, 0, MidpointRounding.AwayFromZero);
                if (discount < 0) discount = 0;
                if (discount > gross) discount = gross;

                var total = decimal.Round(gross - discount, 0, MidpointRounding.AwayFromZero);
                if (total < 0) total = 0;

                return (new PromotionQuoteResult
                {
                    Items = finalQty.Select(kv => (kv.Key, kv.Value)).ToList(),
                    GrossTotal = gross,
                    DiscountAmount = discount,
                    Total = total,
                    AppliedPromotionCode = promo.Code,
                    AppliedPromotionName = promo.Name
                }, null);
            }
        }
    }
}
