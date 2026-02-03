using System;
using Core.Entities;
using Core.Enums;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    // ========= DbSets =========
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<StaffAssignment> StaffAssignments => Set<StaffAssignment>();
    public DbSet<UserRelation> UserRelations => Set<UserRelation>();
    public DbSet<DailyRevenue> DailyRevenues => Set<DailyRevenue>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<MenuItemImage> MenuItemImages => Set<MenuItemImage>();
    public DbSet<InventoryLog> InventoryLogs => Set<InventoryLog>();

    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<WalletAccess> WalletAccesses => Set<WalletAccess>();

    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();

    public DbSet<PasswordResetOtp> PasswordResetOtps => Set<PasswordResetOtp>();

    public DbSet<Promotion> Promotions => Set<Promotion>();

    public DbSet<DisplayScreen> DisplayScreens => Set<DisplayScreen>();
    public DbSet<DisplayScreenCategory> DisplayScreenCategories => Set<DisplayScreenCategory>();
    public DbSet<OrderStationTask> OrderStationTasks => Set<OrderStationTask>();

    // ========= Fluent API =========
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>()
            .HasQueryFilter(x => !x.IsDeleted);

        modelBuilder.Entity<MenuItem>()
            .HasQueryFilter(x =>
                !x.IsDeleted
            );

        modelBuilder.Entity<Category>()
            .HasQueryFilter(x =>
                !x.IsDeleted
            );

        modelBuilder.Entity<Promotion>()
            .HasQueryFilter(x =>
                !x.IsDeleted
            );

        modelBuilder.Entity<DisplayScreen>()
            .HasQueryFilter(x => !x.IsDeleted);

        modelBuilder.Entity<DailyRevenue>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => x.Date)
                  .IsUnique();

            entity.HasOne(x => x.ClosedByUser)
                  .WithMany()
                  .HasForeignKey(x => x.ClosedByUserId);
        });

        // =========================
        // USERS
        // =========================
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Email)
                  .IsRequired()
                  .HasMaxLength(255);

            entity.Property(x => x.PasswordHash)
                  .IsRequired();

            entity.HasIndex(x => x.Email)
                  .IsUnique()
                  .HasFilter("\"IsDeleted\" = false"); // PostgreSQL partial index
        });

        // =========================
        // PASSWORD RESET OTP
        // =========================
        modelBuilder.Entity<PasswordResetOtp>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.CodeHash)
                .IsRequired();

            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.UserId, x.ExpiresAt });
        });

        // =========================
        // ROLES
        // =========================
        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).IsRequired();
        });

        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.HasKey(x => new { x.UserId, x.RoleId });

            entity.HasOne(x => x.User)
                  .WithMany(x => x.UserRoles)
                  .HasForeignKey(x => x.UserId);

            entity.HasOne(x => x.Role)
                  .WithMany()
                  .HasForeignKey(x => x.RoleId);
        });

        // =========================
        // STAFF ASSIGNMENT
        // =========================
        modelBuilder.Entity<StaffAssignment>(entity =>
        {
            entity.HasKey(x => x.UserId);

            entity.HasOne(x => x.User)
                  .WithOne()
                  .HasForeignKey<StaffAssignment>(x => x.UserId);
        });

        // =========================
        // USER RELATIONS (Parent–Student)
        // =========================
        modelBuilder.Entity<UserRelation>(entity =>
        {
            entity.HasKey(x => new { x.ParentId, x.StudentId });

            entity.HasOne(x => x.Parent)
                  .WithMany()
                  .HasForeignKey(x => x.ParentId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Student)
                  .WithMany()
                  .HasForeignKey(x => x.StudentId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // =========================
        // CATEGORY
        // =========================
        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasKey(x => x.Id);
        });

        // =========================
        // DISPLAY SCREEN
        // =========================
        modelBuilder.Entity<DisplayScreen>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Key).IsRequired().HasMaxLength(100);
            entity.Property(x => x.Name).IsRequired().HasMaxLength(200);
            entity.HasIndex(x => x.Key).IsUnique().HasFilter("\"IsDeleted\" = false");
        });

        modelBuilder.Entity<DisplayScreenCategory>(entity =>
        {
            entity.HasKey(x => new { x.ScreenId, x.CategoryId });

            entity.HasOne(x => x.Screen)
                .WithMany(x => x.ScreenCategories)
                .HasForeignKey(x => x.ScreenId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Category)
                .WithMany()
                .HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // =========================
        // ORDER STATION TASK
        // =========================
        modelBuilder.Entity<OrderStationTask>(entity =>
        {
            entity.HasKey(x => new { x.OrderId, x.ScreenId });

            entity.HasOne(x => x.Order)
                .WithMany()
                .HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Screen)
                .WithMany()
                .HasForeignKey(x => x.ScreenId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.ScreenId, x.Status });
        });

        // =========================
        // MENU ITEM
        // =========================
        modelBuilder.Entity<MenuItem>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.HasOne(x => x.Category)
                  .WithMany()
                  .HasForeignKey(x => x.CategoryId);

            // NOTE: Do not map PostgreSQL system column xmin here.
            // Some dev DBs are created outside EF migrations and won't have a physical xmin column.
        });

          modelBuilder.Entity<MenuItemImage>(entity =>
          {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Url)
                .IsRequired();

            entity.HasOne(x => x.MenuItem)
                .WithMany(x => x.Images)
                .HasForeignKey(x => x.MenuItemId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.MenuItemId, x.SortOrder });
          });

        // =========================
        // INVENTORY LOG
        // =========================
        modelBuilder.Entity<InventoryLog>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.HasOne(x => x.Item)
                  .WithMany()
                  .HasForeignKey(x => x.ItemId);

            entity.HasOne(x => x.PerformedByUser)
                  .WithMany()
                  .HasForeignKey(x => x.PerformedByUserId);
        });

        // =========================
        // WALLET
        // =========================
        modelBuilder.Entity<Wallet>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.HasOne(x => x.User)
                  .WithMany()
                  .HasForeignKey(x => x.UserId);

            // 1 user chỉ có 1 wallet Active
            entity.HasIndex(x => x.UserId)
                  .IsUnique()
                  .HasFilter("\"Status\" = 0"); // WalletStatus.Active
        });

        modelBuilder.Entity<WalletAccess>(entity =>
        {
            entity.HasKey(x => new { x.WalletId, x.UserId });

            entity.HasOne(x => x.Wallet)
                  .WithMany()
                  .HasForeignKey(x => x.WalletId);

            entity.HasOne(x => x.User)
                  .WithMany()
                  .HasForeignKey(x => x.UserId);
        });

        // =========================
        // SHIFT
        // =========================
        modelBuilder.Entity<Shift>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.HasOne(x => x.User)
                  .WithMany()
                  .HasForeignKey(x => x.UserId);

            entity.Property(x => x.SystemCashTotal).HasPrecision(18, 2);
            entity.Property(x => x.SystemQrTotal).HasPrecision(18, 2);
            entity.Property(x => x.SystemOnlineTotal).HasPrecision(18, 2);

            entity.Property(x => x.StaffCashInput).HasPrecision(18, 2);
            entity.Property(x => x.StaffQrInput).HasPrecision(18, 2);

        });

        // =========================
        // ORDER
        // =========================
        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.HasOne(x => x.Wallet)
                  .WithMany()
                  .HasForeignKey(x => x.WalletId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(x => x.Shift)
                  .WithMany()
                  .HasForeignKey(x => x.ShiftId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.HasOne(x => x.Order)
                  .WithMany(x => x.Items)
                  .HasForeignKey(x => x.OrderId);

            entity.HasOne(x => x.Item)
                  .WithMany()
                  .HasForeignKey(x => x.ItemId);
        });

        // =========================
        // TRANSACTION
        // =========================
        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.HasOne(x => x.Wallet)
                  .WithMany()
                  .HasForeignKey(x => x.WalletId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(x => x.Order)
                  .WithMany()
                  .HasForeignKey(x => x.OrderId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(x => x.PerformedByUser)
                  .WithMany()
                  .HasForeignKey(x => x.PerformedByUserId);
        });

        // =========================
        // PROMOTION
        // =========================
        modelBuilder.Entity<Promotion>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(x => x.Code)
                .IsRequired()
                .HasMaxLength(64);

            entity.HasIndex(x => x.Code)
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");

            entity.Property(x => x.ConfigJson)
                .HasColumnType("jsonb");
        });
    }
    public override int SaveChanges()
    {
        PreventUpdateClosedShift();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        PreventUpdateClosedShift();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void PreventUpdateClosedShift()
    {
        var modifiedShifts = ChangeTracker.Entries<Shift>()
            .Where(e => e.State == EntityState.Modified);

        foreach (var entry in modifiedShifts)
        {
            // Block changes ONLY when the shift was already closed in the DB.
            // This allows the legitimate transition Open/Declaring/... -> Closed.
            var originalStatus = entry.OriginalValues.GetValue<ShiftStatus>(nameof(Shift.Status));

            if (originalStatus == ShiftStatus.Closed)
                throw new InvalidOperationException("Closed shift cannot be modified");
        }
    }
}
