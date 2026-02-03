using Application.DTOs;
using Core.Entities;
using Core.Enums;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace API.Seed;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration config)
    {
        using var scope = services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // IMPORTANT:
        // Many dev DBs already have tables created outside EF migrations.
        // Running Migrate() on startup can then fail with "relation already exists".
        // Default behavior: DO NOT auto-migrate unless explicitly enabled.
        var autoMigrate = config["SeedAdmin:AutoMigrate"]?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
        if (autoMigrate)
        {
            try
            {
                await db.Database.MigrateAsync();
            }
            catch (PostgresException ex) when (ex.SqlState == "42P07")
            {
                Console.WriteLine($"[DbSeeder] AutoMigrate skipped: {ex.SqlState} {ex.MessageText}");
            }
        }

        // =========================
        // 1) SEED ROLES
        // =========================
        var allRoleNames = new[]
        {
            "AdminSystem",
            "Manager",
            "Staff",
            "StaffKitchen",
            "StaffPOS",
            "StaffCoordination",
            "StaffDrink",
            "Student",
            "Parent"
        };

        var nextRoleId = (await db.Roles.MaxAsync(r => (int?)r.Id)) ?? 0;

        foreach (var roleName in allRoleNames)
        {
            var exists = await db.Roles.AnyAsync(r => r.Name == roleName);
            if (!exists)
            {
                nextRoleId++;
                db.Roles.Add(new Role
                {
                    Id = nextRoleId,
                    Name = roleName
                });
            }
        }

        await db.SaveChangesAsync();

        // =========================
        // 2) SEED USERS (ONE PER ROLE)
        // =========================
        var defaultPassword = config["SeedTestUsers:Password"] ?? "Test@123";

        var usersToSeed = new[]
        {
            new { Email = config["SeedTestUsers:AdminSystemEmail"] ?? "admin.system@smartcanteen.local", FullName = "Admin System", Roles = new[] { "AdminSystem" } },
            new { Email = config["SeedTestUsers:ManagerEmail"] ?? "manager@smartcanteen.local", FullName = "Manager", Roles = new[] { "Manager" } },
            new { Email = config["SeedTestUsers:StaffEmail"] ?? "staff@smartcanteen.local", FullName = "Staff", Roles = new[] { "Staff" } },
            new { Email = config["SeedTestUsers:KitchenEmail"] ?? "kitchen@smartcanteen.local", FullName = "Staff Kitchen", Roles = new[] { "Staff", "StaffKitchen" } },
            new { Email = config["SeedTestUsers:CoordinationeEmail"] ?? "coordination@smartcanteen.local", FullName = "Staff Coordination", Roles = new[] { "Staff", "StaffCoordination" } },
            new { Email = config["SeedTestUsers:PosEmail"] ?? "pos@smartcanteen.local", FullName = "Staff POS", Roles = new[] { "Staff", "StaffPOS" } },
            new { Email = config["SeedTestUsers:DrinkEmail"] ?? "drink@smartcanteen.local", FullName = "Staff Drink", Roles = new[] { "Staff", "StaffDrink" } },
            new { Email = config["SeedTestUsers:StudentEmail"] ?? "student@smartcanteen.local", FullName = "Student", Roles = new[] { "Student" } },
            new { Email = config["SeedTestUsers:ParentEmail"] ?? "parent@smartcanteen.local", FullName = "Parent", Roles = new[] { "Parent" } },
        };

        async Task<User> UpsertUserAsync(string email, string fullName)
        {
            var user = await db.Users
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Email == email);

            if (user == null)
            {
                user = new User
                {
                    Id = Guid.NewGuid(),
                    Email = email,
                    FullName = fullName,
                    PasswordHash = PasswordHasher.Hash(defaultPassword),
                    IsActive = true,
                    IsDeleted = false,
                    CreatedAt = DateTime.UtcNow,
                };

                db.Users.Add(user);
                await db.SaveChangesAsync();
            }
            else
            {
                user.IsDeleted = false;
                user.IsActive = true;
                user.FullName ??= fullName;

                if (config["SeedTestUsers:ForceResetPassword"]?.Equals("true", StringComparison.OrdinalIgnoreCase) == true)
                {
                    user.PasswordHash = PasswordHasher.Hash(defaultPassword);
                }

                await db.SaveChangesAsync();
            }

            return user;
        }

        async Task EnsureUserRoleAsync(Guid userId, int roleId)
        {
            var hasRole = await db.UserRoles.AnyAsync(ur => ur.UserId == userId && ur.RoleId == roleId);
            if (!hasRole)
            {
                db.UserRoles.Add(new UserRole
                {
                    UserId = userId,
                    RoleId = roleId
                });
            }
        }

        foreach (var u in usersToSeed)
        {
            var user = await UpsertUserAsync(u.Email, u.FullName);
            var roleIds = await db.Roles
                .Where(r => u.Roles.Contains(r.Name))
                .Select(r => r.Id)
                .ToListAsync();

            foreach (var roleId in roleIds)
            {
                await EnsureUserRoleAsync(user.Id, roleId);
            }

            await db.SaveChangesAsync();

            // =========================
            // 3) SEED DEFAULT DISPLAY SCREENS (if migrated)
            // =========================
            try
            {
                var defaultScreens = new[]
                {
                    new { Key = "hot-kitchen", Name = "Bếp nóng" },
                    new { Key = "drink", Name = "Quầy nước" },
                };

                foreach (var s in defaultScreens)
                {
                    var exists = await db.DisplayScreens.AnyAsync(x => x.Key == s.Key);
                    if (!exists)
                    {
                        db.DisplayScreens.Add(new DisplayScreen
                        {
                            Id = Guid.NewGuid(),
                            Key = s.Key,
                            Name = s.Name,
                            IsActive = true,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow,
                            IsDeleted = false,
                        });
                    }
                }

                await db.SaveChangesAsync();
            }
            catch (PostgresException ex) when (ex.SqlState == "42P01")
            {
                // Table doesn't exist yet (migration not applied). Safe to skip.
                Console.WriteLine($"[DbSeeder] DisplayScreens seed skipped: {ex.SqlState} {ex.MessageText}");
            }
        }

        // =========================
        // 3) WALLETS + ACCESS (PARENT -> STUDENT)
        // =========================
        var studentUser = await db.Users.FirstAsync(u => u.Email == (config["SeedTestUsers:StudentEmail"] ?? "student@smartcanteen.local"));
        var parentUser = await db.Users.FirstAsync(u => u.Email == (config["SeedTestUsers:ParentEmail"] ?? "parent@smartcanteen.local"));

        var studentWallet = await db.Wallets.FirstOrDefaultAsync(w => w.UserId == studentUser.Id && w.Status == WalletStatus.Active);
        if (studentWallet == null)
        {
            studentWallet = new Wallet
            {
                Id = Guid.NewGuid(),
                UserId = studentUser.Id,
                Balance = 500_000,
                Status = WalletStatus.Active,
                CreatedAt = DateTime.UtcNow
            };
            db.Wallets.Add(studentWallet);
            await db.SaveChangesAsync();
        }

        var hasAccess = await db.WalletAccesses.AnyAsync(a => a.WalletId == studentWallet.Id && a.UserId == parentUser.Id);
        if (!hasAccess)
        {
            db.WalletAccesses.Add(new WalletAccess
            {
                WalletId = studentWallet.Id,
                UserId = parentUser.Id,
                AccessType = WalletAccessType.Shared,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var relationExists = await db.UserRelations.AnyAsync(r => r.ParentId == parentUser.Id && r.StudentId == studentUser.Id);
        if (!relationExists)
        {
            db.UserRelations.Add(new UserRelation
            {
                ParentId = parentUser.Id,
                StudentId = studentUser.Id,
                RelationType = RelationType.Guardian,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // =========================
        // 4) CATEGORIES + MENU ITEMS
        // =========================
        async Task<Category> UpsertCategoryAsync(string name)
        {
            var cat = await db.Categories.FirstOrDefaultAsync(c => c.Name == name);
            if (cat == null)
            {
                cat = new Category
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    IsActive = true,
                    IsDeleted = false,
                    CreatedAt = DateTime.UtcNow
                };
                db.Categories.Add(cat);
                await db.SaveChangesAsync();
            }
            return cat;
        }

        async Task UpsertMenuItemAsync(Guid categoryId, string name, decimal price, int inventoryQty)
        {
            var item = await db.MenuItems.FirstOrDefaultAsync(m => m.Name == name && m.CategoryId == categoryId);
            if (item == null)
            {
                item = new MenuItem
                {
                    Id = Guid.NewGuid(),
                    CategoryId = categoryId,
                    Name = name,
                    Price = price,
                    InventoryQuantity = inventoryQty,
                    IsActive = true,
                    IsDeleted = false,
                    CreatedAt = DateTime.UtcNow
                };
                db.MenuItems.Add(item);
                await db.SaveChangesAsync();
            }
            else
            {
                var changed = false;

                if (item.IsDeleted)
                {
                    item.IsDeleted = false;
                    changed = true;
                }

                if (!item.IsActive)
                {
                    item.IsActive = true;
                    changed = true;
                }

                if (item.Price != price)
                {
                    item.Price = price;
                    changed = true;
                }

                if (item.InventoryQuantity < inventoryQty)
                {
                    item.InventoryQuantity = inventoryQty;
                    changed = true;
                }

                if (changed)
                {
                    await db.SaveChangesAsync();
                }
            }
        }

        var foodCat = await UpsertCategoryAsync("Food");
        var drinkCat = await UpsertCategoryAsync("Drink");

        await UpsertMenuItemAsync(foodCat.Id, "Banh mi", 20_000, 200);
        await UpsertMenuItemAsync(foodCat.Id, "Com ga", 35_000, 150);
        await UpsertMenuItemAsync(drinkCat.Id, "Tra sua", 25_000, 300);
        await UpsertMenuItemAsync(drinkCat.Id, "Nuoc suoi", 10_000, 500);

        Console.WriteLine("[DbSeeder] Seed completed. Test accounts:");
        Console.WriteLine($"  Password (all users): {defaultPassword}");
        foreach (var u in usersToSeed)
        {
            Console.WriteLine($"  {u.Email} => {string.Join(", ", u.Roles)}");
        }
    }
}
