using Core.Common;
using Core.Entities;
using Core.Enums;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    private readonly ICurrentCampusService _currentCampus;
    private readonly Guid _campusId;
    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentCampusService? currentCampus = null)
        : base(options)
    {
        _currentCampus = currentCampus;
        _campusId = currentCampus?.CampusId ?? Guid.Empty;
    }

    // ========= DbSets =========
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<Campus> Campuses => Set<Campus>();
    public DbSet<StaffAssignment> StaffAssignments => Set<StaffAssignment>();
    public DbSet<UserRelation> UserRelations => Set<UserRelation>();

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<InventoryLog> InventoryLogs => Set<InventoryLog>();

    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<WalletAccess> WalletAccesses => Set<WalletAccess>();

    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<VnpayTransaction> VnpayTransactions => Set<VnpayTransaction>();

    // ========= Fluent API =========
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>()
            .HasQueryFilter(x => !x.IsDeleted);

        modelBuilder.Entity<MenuItem>()
            .HasQueryFilter(x =>
                !x.IsDeleted &&
                (_campusId == Guid.Empty || x.CampusId == _campusId)
            );

        modelBuilder.Entity<Category>()
            .HasQueryFilter(x =>
                !x.IsDeleted &&
                (_campusId == Guid.Empty || x.CampusId == _campusId)
            );

        modelBuilder.Entity<Order>()
            .HasQueryFilter(x =>
                _campusId == Guid.Empty || x.CampusId == _campusId
            );

        modelBuilder.Entity<Transaction>()
            .HasQueryFilter(x =>
                _campusId == Guid.Empty || x.CampusId == _campusId
            );

        //seed data

        var campusId = new Guid("11111111-1111-1111-1111-111111111111");

        modelBuilder.Entity<Campus>().HasData(
            new Campus
            {
                Id = campusId,
                Code = "FPTU_DN",
                Name = "FPT University Da Nang",
                IsActive = true,
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        );

        modelBuilder.Entity<Role>().HasData(
            new Role { Id = 1, Name = "Admin" },
            new Role { Id = 2, Name = "Manager" },
            new Role { Id = 3, Name = "Staff" },
            new Role { Id = 4, Name = "Student" },
            new Role { Id = 5, Name = "Parent" }
        );

        var adminId = new Guid("22222222-2222-2222-2222-222222222222");

        modelBuilder.Entity<User>().HasData(
            new User
            {
                Id = adminId,
                Email = "admin@canteen.com",
                PasswordHash = "HASHED_PASSWORD",
                FullName = "System Admin",
                IsActive = true,
                IsDeleted = false,
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        );

        modelBuilder.Entity<UserRole>().HasData(
            new UserRole
            {
                UserId = adminId,
                RoleId = 1
            }
        );

        modelBuilder.Entity<Wallet>().HasData(
            new Wallet
            {
                Id = new Guid("33333333-3333-3333-3333-333333333333"),
                UserId = adminId,
                CampusId = campusId,
                Balance = 0,
                Status = WalletStatus.Active,
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        );

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
        // CAMPUS
        // =========================
        modelBuilder.Entity<Campus>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Code)
                  .IsRequired()
                  .HasMaxLength(50);

            entity.HasIndex(x => x.Code).IsUnique();
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

            entity.HasOne(x => x.Campus)
                  .WithMany()
                  .HasForeignKey(x => x.CampusId);
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

            entity.HasOne(x => x.Campus)
                  .WithMany()
                  .HasForeignKey(x => x.CampusId);
        });

        // =========================
        // MENU ITEM
        // =========================
        modelBuilder.Entity<MenuItem>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.HasOne(x => x.Campus)
                  .WithMany()
                  .HasForeignKey(x => x.CampusId);

            entity.HasOne(x => x.Category)
                  .WithMany()
                  .HasForeignKey(x => x.CategoryId);

            // PostgreSQL optimistic concurrency
            entity.Property<uint>("xmin")
                  .IsConcurrencyToken();
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

            entity.HasOne(x => x.Campus)
                  .WithMany()
                  .HasForeignKey(x => x.CampusId);

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

            entity.HasOne(x => x.Campus)
                  .WithMany()
                  .HasForeignKey(x => x.CampusId);

            // 1 user chỉ có 1 wallet Active / campus
            entity.HasIndex(x => new { x.UserId, x.CampusId })
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

            entity.HasOne(x => x.Campus)
                  .WithMany()
                  .HasForeignKey(x => x.CampusId);

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

            entity.HasOne(x => x.Campus)
                  .WithMany()
                  .HasForeignKey(x => x.CampusId);

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

            entity.HasOne(x => x.Campus)
                  .WithMany()
                  .HasForeignKey(x => x.CampusId);

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
    }
}
