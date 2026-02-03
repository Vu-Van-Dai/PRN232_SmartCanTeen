using API.Hubs;
using Core.Enums;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Core.Entities;
using Npgsql;

namespace API.Controllers
{
    [ApiController]
    [Route("api/kitchen")]
    [Authorize(Roles = "Staff,StaffKitchen,StaffCoordination,StaffDrink,Manager,AdminSystem")]
    public class KitchenController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IHubContext<OrderHub> _orderHub;

        public KitchenController(
            AppDbContext db,
            IHubContext<OrderHub> orderHub)
        {
            _db = db;
            _orderHub = orderHub;
        }

        private async Task EnsureTasksForOrderByCategoryAsync(Guid orderId)
        {
            // Find categories in this order
            var orderCategoryIds = await _db.OrderItems
                .AsNoTracking()
                .Where(oi => oi.OrderId == orderId)
                .Select(oi => oi.Item.CategoryId)
                .Distinct()
                .ToListAsync();

            if (orderCategoryIds.Count == 0) return;

            // Screens whose configured categories intersect the order's categories
            var screenIds = await _db.DisplayScreenCategories
                .AsNoTracking()
                .Where(sc => orderCategoryIds.Contains(sc.CategoryId))
                .Select(sc => sc.ScreenId)
                .Distinct()
                .ToListAsync();

            if (screenIds.Count == 0) return;

            var existing = await _db.OrderStationTasks
                .AsNoTracking()
                .Where(t => t.OrderId == orderId)
                .Select(t => t.ScreenId)
                .ToListAsync();

            var existingSet = existing.ToHashSet();
            var missingScreenIds = screenIds.Where(id => !existingSet.Contains(id)).ToList();
            if (missingScreenIds.Count == 0) return;

            var screens = await _db.DisplayScreens
                .AsNoTracking()
                .Where(s => missingScreenIds.Contains(s.Id) && s.IsActive)
                .Select(s => new { s.Id, s.Key })
                .ToListAsync();

            foreach (var s in screens)
            {
                var isDrink = string.Equals(s.Key, "drink", StringComparison.OrdinalIgnoreCase);
                _db.OrderStationTasks.Add(new OrderStationTask
                {
                    OrderId = orderId,
                    ScreenId = s.Id,
                    Status = isDrink ? StationTaskStatus.Preparing : StationTaskStatus.Pending,
                    StartedAt = isDrink ? DateTime.UtcNow : null,
                });
            }

            await _db.SaveChangesAsync();
        }

        private async Task RecomputeOrderStatusAsync(Guid orderId)
        {
            var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId);
            if (order == null) return;

            var tasks = await _db.OrderStationTasks
                .AsNoTracking()
                .Where(t => t.OrderId == orderId)
                .Select(t => t.Status)
                .ToListAsync();

            if (tasks.Count == 0) return;

            var allCompleted = tasks.All(s => s == StationTaskStatus.Completed);
            if (allCompleted)
            {
                order.Status = OrderStatus.Completed;
                await _db.SaveChangesAsync();
                return;
            }

            var allReadyOrCompleted = tasks.All(s => s == StationTaskStatus.Ready || s == StationTaskStatus.Completed);
            if (allReadyOrCompleted)
            {
                order.Status = OrderStatus.Ready;
                await _db.SaveChangesAsync();
                return;
            }

            var anyPreparing = tasks.Any(s => s == StationTaskStatus.Preparing);
            if (anyPreparing)
            {
                order.Status = OrderStatus.Preparing;
                await _db.SaveChangesAsync();
            }
        }

        /// <summary>
        /// Danh sách đơn cho màn hình bếp
        /// </summary>
        [HttpGet("orders")]
        public async Task<IActionResult> GetKitchenOrders([FromQuery] string? screenKey = null)
        {
            var now = DateTime.UtcNow;

            HashSet<Guid>? allowedCategoryIds = null;
            Guid? screenId = null;
            var stationKey = string.IsNullOrWhiteSpace(screenKey) ? null : screenKey.Trim();
            var stationKeyNormalized = stationKey?.ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(screenKey))
            {
                try
                {
                    var key = stationKey!;
                    var isBuiltIn = string.Equals(key, "drink", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(key, "hot-kitchen", StringComparison.OrdinalIgnoreCase);

                    // Load by key (even if inactive) so we can recover built-in screens.
                    var screen = await _db.DisplayScreens
                        .Include(s => s.ScreenCategories)
                        .FirstOrDefaultAsync(s => s.Key == key);

                    if (screen == null && isBuiltIn)
                    {
                        screen = new DisplayScreen
                        {
                            Id = Guid.NewGuid(),
                            Key = key,
                            Name = key,
                            IsActive = true,
                            UpdatedAt = DateTime.UtcNow,
                        };
                        _db.DisplayScreens.Add(screen);
                        await _db.SaveChangesAsync();
                    }

                    if (screen != null && !screen.IsActive && isBuiltIn)
                    {
                        screen.IsActive = true;
                        screen.UpdatedAt = DateTime.UtcNow;
                        await _db.SaveChangesAsync();
                    }

                    // If not found (or not configured yet), fallback to no-filter to avoid breaking screens.
                    if (screen != null && screen.IsActive)
                    {
                        screenId = screen.Id;
                        var ids = screen.ScreenCategories.Select(sc => sc.CategoryId).Distinct().ToList();
                        if (ids.Count > 0)
                        {
                            allowedCategoryIds = ids.ToHashSet();
                        }
                    }
                }
                catch (PostgresException ex) when (ex.SqlState == "42P01")
                {
                    // DisplayScreens tables not migrated yet.
                    allowedCategoryIds = null;
                    screenId = null;
                }
            }

            // If a screen exists, ensure station tasks exist for this screen for matching orders.
            if (screenId != null)
            {
                var isDrinkStation = string.Equals(stationKeyNormalized, "drink", StringComparison.OrdinalIgnoreCase);

                var relevantStatuses = new[]
                {
                    OrderStatus.SystemHolding,
                    OrderStatus.Paid,
                    OrderStatus.Preparing,
                    OrderStatus.Ready,
                };

                var candidateOrderIds = await _db.Orders
                    .AsNoTracking()
                    .Where(o => relevantStatuses.Contains(o.Status))
                    .Where(o => allowedCategoryIds == null || o.Items.Any(i => allowedCategoryIds.Contains(i.Item.CategoryId)))
                    .Select(o => o.Id)
                    .ToListAsync();

                if (candidateOrderIds.Count > 0)
                {
                    var existing = await _db.OrderStationTasks
                        .AsNoTracking()
                        .Where(t => t.ScreenId == screenId && candidateOrderIds.Contains(t.OrderId))
                        .Select(t => t.OrderId)
                        .ToListAsync();

                    var existingSet = existing.ToHashSet();
                    var missing = candidateOrderIds.Where(id => !existingSet.Contains(id)).ToList();

                    if (missing.Count > 0)
                    {
                        // Derive initial task status from current order status (best-effort).
                        var orders = await _db.Orders
                            .AsNoTracking()
                            .Where(o => missing.Contains(o.Id))
                            .Select(o => new { o.Id, o.Status })
                            .ToListAsync();

                        foreach (var o in orders)
                        {
                            // Default: tasks start as Pending, except drink station which starts as Preparing.
                            var initial = isDrinkStation ? StationTaskStatus.Preparing : StationTaskStatus.Pending;

                            // Derive from current order status when possible.
                            if (o.Status == OrderStatus.Ready) initial = StationTaskStatus.Ready;
                            else if (o.Status == OrderStatus.Preparing) initial = StationTaskStatus.Preparing;

                            _db.OrderStationTasks.Add(new OrderStationTask
                            {
                                OrderId = o.Id,
                                ScreenId = screenId.Value,
                                Status = initial,
                                StartedAt = initial >= StationTaskStatus.Preparing ? DateTime.UtcNow : null,
                                ReadyAt = initial >= StationTaskStatus.Ready ? DateTime.UtcNow : null,
                            });
                        }

                        await _db.SaveChangesAsync();
                    }

                    // Legacy compatibility: drink station should not have Pending tasks.
                    if (isDrinkStation)
                    {
                        var pendingTasks = await _db.OrderStationTasks
                            .Where(t => t.ScreenId == screenId && candidateOrderIds.Contains(t.OrderId) && t.Status == StationTaskStatus.Pending)
                            .ToListAsync();

                        if (pendingTasks.Count > 0)
                        {
                            foreach (var t in pendingTasks)
                            {
                                t.Status = StationTaskStatus.Preparing;
                                t.StartedAt ??= DateTime.UtcNow;
                            }

                            await _db.SaveChangesAsync();
                        }
                    }
                    else
                    {
                        // If the order is already in Preparing, its station task should not remain Pending.
                        // This can happen when tasks were created earlier with Pending defaults.
                        var pendingPreparingTasks = await _db.OrderStationTasks
                            .Include(t => t.Order)
                            .Where(t => t.ScreenId == screenId
                                && candidateOrderIds.Contains(t.OrderId)
                                && t.Status == StationTaskStatus.Pending
                                && t.Order.Status == OrderStatus.Preparing)
                            .ToListAsync();

                        if (pendingPreparingTasks.Count > 0)
                        {
                            foreach (var t in pendingPreparingTasks)
                            {
                                t.Status = StationTaskStatus.Preparing;
                                t.StartedAt ??= DateTime.UtcNow;
                            }

                            await _db.SaveChangesAsync();
                        }
                    }
                }
            }


            var stationMode = screenId != null;
            var relevantOrderStatuses = new[]
            {
                OrderStatus.SystemHolding,
                OrderStatus.Paid,
                OrderStatus.Preparing,
                OrderStatus.Ready,
                OrderStatus.Completed,
            };

            var pending = await _db.Orders
                .AsNoTracking()
                .Where(x => stationMode ? relevantOrderStatuses.Contains(x.Status) : (x.Status == OrderStatus.SystemHolding || x.Status == OrderStatus.Paid))
                .Include(x => x.OrderedByUser)
                .Include(x => x.Items)
                    .ThenInclude(i => i.Item)
                .Where(x => allowedCategoryIds == null || x.Items.Any(i => allowedCategoryIds.Contains(i.Item.CategoryId)))
                .Where(x =>
                    screenId == null ||
                    _db.OrderStationTasks.Any(t => t.OrderId == x.Id && t.ScreenId == screenId && t.Status == StationTaskStatus.Pending))
                .OrderBy(x => x.PickupTime ?? x.CreatedAt)
                .Select(x => new
                {
                    id = x.Id,
                    createdAt = x.CreatedAt,
                    pickupTime = x.PickupTime,
                    status = x.Status.ToString(),
                    stationTaskStatus = screenId == null
                        ? null
                        : _db.OrderStationTasks
                            .Where(t => t.OrderId == x.Id && t.ScreenId == screenId)
                            .Select(t => (int?)t.Status)
                            .FirstOrDefault(),
                    stationTaskStartedAt = screenId == null
                        ? null
                        : _db.OrderStationTasks
                            .Where(t => t.OrderId == x.Id && t.ScreenId == screenId)
                            .Select(t => t.StartedAt)
                            .FirstOrDefault(),
                    stationTaskReadyAt = screenId == null
                        ? null
                        : _db.OrderStationTasks
                            .Where(t => t.OrderId == x.Id && t.ScreenId == screenId)
                            .Select(t => t.ReadyAt)
                            .FirstOrDefault(),
                    stationTaskCompletedAt = screenId == null
                        ? null
                        : _db.OrderStationTasks
                            .Where(t => t.OrderId == x.Id && t.ScreenId == screenId)
                            .Select(t => t.CompletedAt)
                            .FirstOrDefault(),
                    isUrgent = x.IsUrgent,
                    totalPrice = x.TotalPrice,
                    orderedBy = x.OrderedByUser.FullName ?? x.OrderedByUser.Email,
                    items = x.Items
                        .Where(i => allowedCategoryIds == null || allowedCategoryIds.Contains(i.Item.CategoryId))
                        .Select(i => new
                    {
                        itemId = i.ItemId,
                        name = i.Item.Name,
                        quantity = i.Quantity,
                        unitPrice = i.UnitPrice,
                    })
                })
                .ToListAsync();

            var preparing = await _db.Orders
                .AsNoTracking()
                .Where(x => stationMode ? relevantOrderStatuses.Contains(x.Status) : x.Status == OrderStatus.Preparing)
                .Include(x => x.OrderedByUser)
                .Include(x => x.Items)
                    .ThenInclude(i => i.Item)
                .Where(x => allowedCategoryIds == null || x.Items.Any(i => allowedCategoryIds.Contains(i.Item.CategoryId)))
                .Where(x =>
                    screenId == null ||
                    _db.OrderStationTasks.Any(t => t.OrderId == x.Id && t.ScreenId == screenId && t.Status == StationTaskStatus.Preparing))
                .OrderBy(x => x.CreatedAt)
                .Select(x => new
                {
                    id = x.Id,
                    createdAt = x.CreatedAt,
                    pickupTime = x.PickupTime,
                    status = x.Status.ToString(),
                    stationTaskStatus = screenId == null
                        ? null
                        : _db.OrderStationTasks
                            .Where(t => t.OrderId == x.Id && t.ScreenId == screenId)
                            .Select(t => (int?)t.Status)
                            .FirstOrDefault(),
                    stationTaskStartedAt = screenId == null
                        ? null
                        : _db.OrderStationTasks
                            .Where(t => t.OrderId == x.Id && t.ScreenId == screenId)
                            .Select(t => t.StartedAt)
                            .FirstOrDefault(),
                    stationTaskReadyAt = screenId == null
                        ? null
                        : _db.OrderStationTasks
                            .Where(t => t.OrderId == x.Id && t.ScreenId == screenId)
                            .Select(t => t.ReadyAt)
                            .FirstOrDefault(),
                    stationTaskCompletedAt = screenId == null
                        ? null
                        : _db.OrderStationTasks
                            .Where(t => t.OrderId == x.Id && t.ScreenId == screenId)
                            .Select(t => t.CompletedAt)
                            .FirstOrDefault(),
                    isUrgent = x.IsUrgent,
                    totalPrice = x.TotalPrice,
                    orderedBy = x.OrderedByUser.FullName ?? x.OrderedByUser.Email,
                    items = x.Items
                        .Where(i => allowedCategoryIds == null || allowedCategoryIds.Contains(i.Item.CategoryId))
                        .Select(i => new
                    {
                        itemId = i.ItemId,
                        name = i.Item.Name,
                        quantity = i.Quantity,
                        unitPrice = i.UnitPrice,
                    })
                })
                .ToListAsync();

            var ready = await _db.Orders
                .AsNoTracking()
                .Where(x => stationMode ? relevantOrderStatuses.Contains(x.Status) : x.Status == OrderStatus.Ready)
                .Include(x => x.OrderedByUser)
                .Include(x => x.Items)
                    .ThenInclude(i => i.Item)
                .Where(x => allowedCategoryIds == null || x.Items.Any(i => allowedCategoryIds.Contains(i.Item.CategoryId)))
                .Where(x =>
                    screenId == null ||
                    _db.OrderStationTasks.Any(t => t.OrderId == x.Id && t.ScreenId == screenId && t.Status == StationTaskStatus.Ready))
                .OrderBy(x => x.CreatedAt)
                .Select(x => new
                {
                    id = x.Id,
                    createdAt = x.CreatedAt,
                    pickupTime = x.PickupTime,
                    status = x.Status.ToString(),
                    stationTaskStatus = screenId == null
                        ? null
                        : _db.OrderStationTasks
                            .Where(t => t.OrderId == x.Id && t.ScreenId == screenId)
                            .Select(t => (int?)t.Status)
                            .FirstOrDefault(),
                    stationTaskStartedAt = screenId == null
                        ? null
                        : _db.OrderStationTasks
                            .Where(t => t.OrderId == x.Id && t.ScreenId == screenId)
                            .Select(t => t.StartedAt)
                            .FirstOrDefault(),
                    stationTaskReadyAt = screenId == null
                        ? null
                        : _db.OrderStationTasks
                            .Where(t => t.OrderId == x.Id && t.ScreenId == screenId)
                            .Select(t => t.ReadyAt)
                            .FirstOrDefault(),
                    stationTaskCompletedAt = screenId == null
                        ? null
                        : _db.OrderStationTasks
                            .Where(t => t.OrderId == x.Id && t.ScreenId == screenId)
                            .Select(t => t.CompletedAt)
                            .FirstOrDefault(),
                    isUrgent = x.IsUrgent,
                    totalPrice = x.TotalPrice,
                    orderedBy = x.OrderedByUser.FullName ?? x.OrderedByUser.Email,
                    items = x.Items
                        .Where(i => allowedCategoryIds == null || allowedCategoryIds.Contains(i.Item.CategoryId))
                        .Select(i => new
                    {
                        itemId = i.ItemId,
                        name = i.Item.Name,
                        quantity = i.Quantity,
                        unitPrice = i.UnitPrice,
                    })
                })
                .ToListAsync();

            var urgent = preparing
                .Where(x => x.isUrgent)
                .OrderBy(x => x.createdAt)
                .ToList();

            object completed = stationMode
                ? await _db.OrderStationTasks
                    .AsNoTracking()
                    .Where(t => t.ScreenId == screenId && t.Status == StationTaskStatus.Completed)
                    .OrderByDescending(t => t.CompletedAt ?? t.ReadyAt ?? t.StartedAt)
                    .Take(50)
                    .Select(t => new
                    {
                        id = t.OrderId,
                        createdAt = t.Order.CreatedAt,
                        pickupTime = t.Order.PickupTime,
                        status = t.Order.Status.ToString(),
                        stationTaskStatus = (int?)t.Status,
                        stationTaskStartedAt = t.StartedAt,
                        stationTaskReadyAt = t.ReadyAt,
                        stationTaskCompletedAt = t.CompletedAt,
                        isUrgent = t.Order.IsUrgent,
                        totalPrice = t.Order.TotalPrice,
                        orderedBy = t.Order.OrderedByUser.FullName ?? t.Order.OrderedByUser.Email,
                        items = t.Order.Items
                            .Where(i => allowedCategoryIds == null || allowedCategoryIds.Contains(i.Item.CategoryId))
                            .Select(i => new
                            {
                                itemId = i.ItemId,
                                name = i.Item.Name,
                                quantity = i.Quantity,
                                unitPrice = i.UnitPrice,
                            })
                    })
                    .ToListAsync()
                : new List<object>();

            var upcoming = pending
                .Where(x =>
                    x.pickupTime != null &&
                    x.pickupTime > now &&
                    x.pickupTime <= now.AddMinutes(60)
                )
                .OrderBy(x => x.pickupTime)
                .ToList();

            return Ok(new { pending, preparing, ready, completed, urgent, upcoming });
        }

        /// <summary>
        /// Bếp bấm "Nấu"
        /// </summary>
        [HttpPost("{orderId}/prepare")]
        [Authorize(Roles = "StaffKitchen,StaffDrink,Manager,AdminSystem")]
        public async Task<IActionResult> StartCooking(Guid orderId, [FromQuery] string? stationKey = null)
        {
            var key = string.IsNullOrWhiteSpace(stationKey) ? "hot-kitchen" : stationKey.Trim();

            var screen = await _db.DisplayScreens.FirstOrDefaultAsync(s => s.Key == key && s.IsActive);
            if (screen == null) return BadRequest("Invalid stationKey");

            // station-level auth
            var roles = User.FindAll(ClaimTypes.Role).Select(r => r.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var isManager = roles.Contains("Manager") || roles.Contains("AdminSystem");
            if (!isManager)
            {
                if (string.Equals(key, "drink", StringComparison.OrdinalIgnoreCase))
                {
                    if (!roles.Contains("StaffDrink")) return Forbid();
                }
                else
                {
                    if (!roles.Contains("StaffKitchen")) return Forbid();
                }
            }

            var order = await _db.Orders
                .Include(o => o.Items)
                    .ThenInclude(i => i.Item)
                .FirstOrDefaultAsync(x => x.Id == orderId);

            if (order == null) return BadRequest("Invalid order");

            var task = await _db.OrderStationTasks
                .FirstOrDefaultAsync(t => t.OrderId == orderId && t.ScreenId == screen.Id);

            if (task == null)
            {
                task = new OrderStationTask { OrderId = orderId, ScreenId = screen.Id, Status = StationTaskStatus.Pending };
                _db.OrderStationTasks.Add(task);
            }

            if (task.Status != StationTaskStatus.Pending)
                return BadRequest("Invalid task state");

            task.Status = StationTaskStatus.Preparing;
            task.StartedAt = DateTime.UtcNow;
            order.Status = OrderStatus.Preparing;
            // Avoid marking pre-orders as urgent just because a station starts early.
            order.IsUrgent = order.PickupTime == null;

            await _db.SaveChangesAsync();

            await EnsureTasksForOrderByCategoryAsync(orderId);
            await RecomputeOrderStatusAsync(orderId);

            return Ok();
        }

        /// <summary>
        /// Bếp bấm "Xong"
        /// </summary>
        [HttpPost("{orderId}/ready")]
        [Authorize(Roles = "StaffKitchen,StaffDrink,Manager,AdminSystem")]
        public async Task<IActionResult> MarkReady(Guid orderId, [FromQuery] string? stationKey = null)
        {
            var key = string.IsNullOrWhiteSpace(stationKey) ? "hot-kitchen" : stationKey.Trim();

            var screen = await _db.DisplayScreens.FirstOrDefaultAsync(s => s.Key == key && s.IsActive);
            if (screen == null) return BadRequest("Invalid stationKey");

            // station-level auth
            var roles = User.FindAll(ClaimTypes.Role).Select(r => r.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var isManager = roles.Contains("Manager") || roles.Contains("AdminSystem");
            if (!isManager)
            {
                if (string.Equals(key, "drink", StringComparison.OrdinalIgnoreCase))
                {
                    if (!roles.Contains("StaffDrink")) return Forbid();
                }
                else
                {
                    if (!roles.Contains("StaffKitchen")) return Forbid();
                }
            }

            var order = await _db.Orders.FirstOrDefaultAsync(x => x.Id == orderId);
            if (order == null) return BadRequest("Invalid order");

            var task = await _db.OrderStationTasks
                .FirstOrDefaultAsync(t => t.OrderId == orderId && t.ScreenId == screen.Id);

            if (task == null) return BadRequest("Task not found");

            var isDrink = string.Equals(key, "drink", StringComparison.OrdinalIgnoreCase);
            if (!isDrink)
            {
                if (task.Status != StationTaskStatus.Preparing) return BadRequest("Invalid task state");
            }
            else
            {
                if (task.Status != StationTaskStatus.Pending && task.Status != StationTaskStatus.Preparing)
                    return BadRequest("Invalid task state");

                if (task.Status == StationTaskStatus.Pending && task.StartedAt == null)
                {
                    task.StartedAt = DateTime.UtcNow;
                }
            }

            task.Status = StationTaskStatus.Ready;
            task.ReadyAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            await EnsureTasksForOrderByCategoryAsync(orderId);
            await RecomputeOrderStatusAsync(orderId);

            var allReady = order.Status == OrderStatus.Ready;

            // 🔔 Notify student only when whole order becomes Ready.
            if (allReady)
            {
                var notifyEnabled = await _db.Users
                    .AsNoTracking()
                    .Where(u => u.Id == order.OrderedByUserId)
                    .Select(u => u.OrderReadyNotificationsEnabled)
                    .FirstOrDefaultAsync();

                if (notifyEnabled)
                {
                    await _orderHub.Clients
                        .User(order.OrderedByUserId.ToString())
                        .SendAsync("OrderReady", new
                        {
                            orderId = order.Id,
                            pickupTime = order.PickupTime
                        });
                }
            }

            return Ok();
        }

        /// <summary>
        /// Nhân viên điều phối bấm "Đã giao" (đơn biến mất khỏi board)
        /// </summary>
        [HttpPost("{orderId}/complete")]
        [Authorize(Roles = "StaffCoordination,StaffDrink,Manager,AdminSystem")]
        public async Task<IActionResult> Complete(Guid orderId, [FromQuery] string? stationKey = null)
        {
            var roles = User.FindAll(ClaimTypes.Role).Select(r => r.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var isManager = roles.Contains("Manager") || roles.Contains("AdminSystem");
            var isCoordination = roles.Contains("StaffCoordination");

            var order = await _db.Orders.FirstOrDefaultAsync(x => x.Id == orderId);
            if (order == null) return BadRequest("Invalid order");

            // If stationKey is provided, complete only that station task.
            if (!string.IsNullOrWhiteSpace(stationKey))
            {
                var key = stationKey.Trim();
                var screen = await _db.DisplayScreens.FirstOrDefaultAsync(s => s.Key == key && s.IsActive);
                if (screen == null) return BadRequest("Invalid stationKey");

                if (!isManager)
                {
                    if (string.Equals(key, "drink", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!roles.Contains("StaffDrink")) return Forbid();
                    }
                    else
                    {
                        if (!isCoordination) return Forbid();
                    }
                }

                var task = await _db.OrderStationTasks
                    .FirstOrDefaultAsync(t => t.OrderId == orderId && t.ScreenId == screen.Id);

                if (task == null) return BadRequest("Task not found");
                if (task.Status != StationTaskStatus.Ready) return BadRequest("Invalid task state");

                task.Status = StationTaskStatus.Completed;
                task.CompletedAt = DateTime.UtcNow;

                var allCompleted = await _db.OrderStationTasks
                    .Where(t => t.OrderId == orderId)
                    .AllAsync(t => t.Status == StationTaskStatus.Completed);

                if (allCompleted)
                {
                    order.Status = OrderStatus.Completed;
                }

                await _db.SaveChangesAsync();

                if (allCompleted)
                {
                    var notifyEnabled = await _db.Users
                        .AsNoTracking()
                        .Where(u => u.Id == order.OrderedByUserId)
                        .Select(u => u.OrderReadyNotificationsEnabled)
                        .FirstOrDefaultAsync();

                    if (notifyEnabled)
                    {
                        await _orderHub.Clients
                            .User(order.OrderedByUserId.ToString())
                            .SendAsync("OrderCompleted", new { orderId = order.Id });
                    }
                }

                return Ok();
            }

            // No stationKey => coordination completes whole order after it's Ready.
            if (!isManager && !isCoordination) return Forbid();
            if (order.Status != OrderStatus.Ready) return BadRequest("Invalid order");

            // Mark all tasks completed (if any exist)
            var tasks = await _db.OrderStationTasks.Where(t => t.OrderId == orderId).ToListAsync();
            foreach (var t in tasks)
            {
                if (t.Status != StationTaskStatus.Completed)
                {
                    t.Status = StationTaskStatus.Completed;
                    t.CompletedAt = DateTime.UtcNow;
                }
            }

            order.Status = OrderStatus.Completed;
            await _db.SaveChangesAsync();

            var notify = await _db.Users
                .AsNoTracking()
                .Where(u => u.Id == order.OrderedByUserId)
                .Select(u => u.OrderReadyNotificationsEnabled)
                .FirstOrDefaultAsync();

            if (notify)
            {
                await _orderHub.Clients
                    .User(order.OrderedByUserId.ToString())
                    .SendAsync("OrderCompleted", new { orderId = order.Id });
            }

            return Ok();
        }
    }
}
