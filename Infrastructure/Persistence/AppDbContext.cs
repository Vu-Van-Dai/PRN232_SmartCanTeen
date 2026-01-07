using Core.Entities;
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

    // ========= Fluent API =========
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

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
