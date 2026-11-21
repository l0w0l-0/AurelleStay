using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Pomelo.EntityFrameworkCore.MySql.Scaffolding.Internal;

namespace pameka_7033_l005.Models;

public partial class Csi402dbContext : DbContext
{
    public Csi402dbContext()
    {
    }

    public Csi402dbContext(DbContextOptions<Csi402dbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Booking> Bookings { get; set; }

    public virtual DbSet<BookingRoom> BookingRooms { get; set; }

    public virtual DbSet<BookingService> BookingServices { get; set; }

    public virtual DbSet<DeliveryTask> DeliveryTasks { get; set; }

    public virtual DbSet<ExtraService> ExtraServices { get; set; }

    public virtual DbSet<Expense> Expenses { get; set; }

    public virtual DbSet<AuditLog> AuditLogs { get; set; }

    public virtual DbSet<Notification> Notifications { get; set; }

    public virtual DbSet<ContactMessage> ContactMessages { get; set; }

    public virtual DbSet<PromoRecipient> PromoRecipients { get; set; }

    public virtual DbSet<RateSeason> RateSeasons { get; set; }

    public virtual DbSet<HousekeepingTask> HousekeepingTasks { get; set; }

    public virtual DbSet<Payment> Payments { get; set; }

    public virtual DbSet<Promotion> Promotions { get; set; }

    public virtual DbSet<Review> Reviews { get; set; }

    public virtual DbSet<Role> Roles { get; set; }

    public virtual DbSet<Room> Rooms { get; set; }

    public virtual DbSet<RoomTheme> RoomThemes { get; set; }

    public virtual DbSet<RoomType> RoomTypes { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<LoginHistory> LoginHistories { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .UseCollation("utf8mb4_unicode_ci")
            .HasCharSet("utf8mb4");

        modelBuilder.Entity<Booking>(entity =>
        {
            entity.HasKey(e => e.BookingId).HasName("PRIMARY");

            entity.ToTable("bookings");

            entity.HasIndex(e => e.ConfirmedBy, "confirmed_by");

            entity.HasIndex(e => e.PromoId, "promo_id");

            entity.HasIndex(e => e.UserId, "user_id");

            entity.Property(e => e.BookingId).HasColumnName("booking_id");
            entity.Property(e => e.BookedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("datetime")
                .HasColumnName("booked_at");
            entity.Property(e => e.CheckIn).HasColumnName("check_in");
            entity.Property(e => e.CheckOut).HasColumnName("check_out");
            entity.Property(e => e.ConfirmedBy).HasColumnName("confirmed_by");
            entity.Property(e => e.Guests).HasColumnName("guests");
            entity.Property(e => e.ArrivalTime).HasMaxLength(5).HasColumnName("arrival_time");
            entity.Property(e => e.GuestRequest).HasMaxLength(300).HasColumnName("guest_request");
            entity.Property(e => e.DiscountTotal)
                .HasPrecision(10, 2)
                .HasColumnName("discount_total");
            entity.Property(e => e.NetTotal)
                .HasPrecision(10, 2)
                .HasColumnName("net_total");
            entity.Property(e => e.PromoId).HasColumnName("promo_id");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'PENDING'")
                .HasColumnType("enum('PENDING','CONFIRMED','CHECKED_IN','CHECKED_OUT','CANCELLED')")
                .HasColumnName("status");
            entity.Property(e => e.Subtotal)
                .HasPrecision(10, 2)
                .HasColumnName("subtotal");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.ConfirmedByNavigation).WithMany(p => p.BookingConfirmedByNavigations)
                .HasForeignKey(d => d.ConfirmedBy)
                .HasConstraintName("bookings_ibfk_3");

            entity.HasOne(d => d.Promo).WithMany(p => p.Bookings)
                .HasForeignKey(d => d.PromoId)
                .HasConstraintName("bookings_ibfk_2");

            entity.HasOne(d => d.User).WithMany(p => p.BookingUsers)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("bookings_ibfk_1");
        });

        modelBuilder.Entity<BookingRoom>(entity =>
        {
            entity.HasKey(e => e.BrId).HasName("PRIMARY");

            entity.ToTable("booking_rooms");

            entity.HasIndex(e => e.BookingId, "booking_id");

            entity.HasIndex(e => e.RoomId, "room_id");

            entity.Property(e => e.BrId).HasColumnName("br_id");
            entity.Property(e => e.BookingId).HasColumnName("booking_id");
            entity.Property(e => e.Nights).HasColumnName("nights");
            entity.Property(e => e.PricePerNight)
                .HasPrecision(10, 2)
                .HasColumnName("price_per_night");
            entity.Property(e => e.RoomId).HasColumnName("room_id");

            entity.HasOne(d => d.Booking).WithMany(p => p.BookingRooms)
                .HasForeignKey(d => d.BookingId)
                .HasConstraintName("booking_rooms_ibfk_1");

            entity.HasOne(d => d.Room).WithMany(p => p.BookingRooms)
                .HasForeignKey(d => d.RoomId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("booking_rooms_ibfk_2");
        });

        modelBuilder.Entity<BookingService>(entity =>
        {
            entity.HasKey(e => e.BsId).HasName("PRIMARY");

            entity.ToTable("booking_services");

            entity.HasIndex(e => e.BookingId, "booking_id");

            entity.HasIndex(e => e.RoomId, "room_id");

            entity.HasIndex(e => e.ServiceId, "service_id");

            entity.Property(e => e.BsId).HasColumnName("bs_id");
            entity.Property(e => e.BookingId).HasColumnName("booking_id");
            entity.Property(e => e.Quantity)
                .HasDefaultValueSql("'1'")
                .HasColumnName("quantity");
            entity.Property(e => e.RoomId).HasColumnName("room_id");
            entity.Property(e => e.ServiceId).HasColumnName("service_id");
            entity.Property(e => e.UnitPrice)
                .HasPrecision(10, 2)
                .HasColumnName("unit_price");

            entity.HasOne(d => d.Booking).WithMany(p => p.BookingServices)
                .HasForeignKey(d => d.BookingId)
                .HasConstraintName("booking_services_ibfk_1");

            entity.HasOne(d => d.Room).WithMany(p => p.BookingServices)
                .HasForeignKey(d => d.RoomId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("booking_services_ibfk_2");

            entity.HasOne(d => d.Service).WithMany(p => p.BookingServices)
                .HasForeignKey(d => d.ServiceId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("booking_services_ibfk_3");
        });

        modelBuilder.Entity<DeliveryTask>(entity =>
        {
            entity.HasKey(e => e.TaskId).HasName("PRIMARY");

            entity.ToTable("delivery_tasks");

            entity.HasIndex(e => e.AssignedTo, "assigned_to");

            entity.HasIndex(e => e.BsId, "bs_id");

            entity.HasIndex(e => e.RoomId, "room_id");

            entity.Property(e => e.TaskId).HasColumnName("task_id");
            entity.Property(e => e.AssignedTo).HasColumnName("assigned_to");
            entity.Property(e => e.BsId).HasColumnName("bs_id");
            entity.Property(e => e.DoneAt)
                .HasColumnType("datetime")
                .HasColumnName("done_at");
            entity.Property(e => e.RoomId).HasColumnName("room_id");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'PENDING'")
                .HasColumnType("enum('PENDING','DELIVERING','DONE')")
                .HasColumnName("status");

            entity.HasOne(d => d.AssignedToNavigation).WithMany(p => p.DeliveryTasks)
                .HasForeignKey(d => d.AssignedTo)
                .HasConstraintName("delivery_tasks_ibfk_3");

            entity.HasOne(d => d.Bs).WithMany(p => p.DeliveryTasks)
                .HasForeignKey(d => d.BsId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("delivery_tasks_ibfk_1");

            entity.HasOne(d => d.Room).WithMany(p => p.DeliveryTasks)
                .HasForeignKey(d => d.RoomId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("delivery_tasks_ibfk_2");
        });

        modelBuilder.Entity<ExtraService>(entity =>
        {
            entity.HasKey(e => e.ServiceId).HasName("PRIMARY");

            entity.ToTable("extra_services");

            entity.HasIndex(e => e.ServiceName, "service_name").IsUnique();

            entity.Property(e => e.ServiceId).HasColumnName("service_id");
            entity.Property(e => e.IsActive)
                .IsRequired()
                .HasDefaultValueSql("'1'")
                .HasColumnName("is_active");
            entity.Property(e => e.Price)
                .HasPrecision(10, 2)
                .HasColumnName("price");
            entity.Property(e => e.ServiceName)
                .HasMaxLength(50)
                .HasColumnName("service_name");
            entity.Property(e => e.Category).HasMaxLength(10).HasColumnName("category");
            entity.Property(e => e.Description).HasMaxLength(300).HasColumnName("description");
            entity.Property(e => e.ImageUrl).HasMaxLength(255).HasColumnName("image_url");
        });

        modelBuilder.Entity<RateSeason>(entity =>
        {
            entity.HasKey(e => e.SeasonId);
            entity.ToTable("rate_seasons");
            entity.HasIndex(e => new { e.IsActive, e.StartDate, e.EndDate }, "ix_season_dates");
            entity.Property(e => e.SeasonId).HasColumnName("season_id");
            entity.Property(e => e.SeasonName).HasMaxLength(100).HasColumnName("season_name");
            entity.Property(e => e.StartDate).HasColumnName("start_date");
            entity.Property(e => e.EndDate).HasColumnName("end_date");
            entity.Property(e => e.AdjustPercent).HasPrecision(5, 2).HasColumnName("adjust_percent");
            entity.Property(e => e.IsActive).HasColumnName("is_active");
            entity.Property(e => e.CreatedAt).HasColumnType("datetime").HasColumnName("created_at");
            entity.Property(e => e.AutoKey).HasMaxLength(40).HasColumnName("auto_key");
            entity.HasIndex(e => e.AutoKey, "ux_season_auto").IsUnique();
        });

        modelBuilder.Entity<PromoRecipient>(entity =>
        {
            entity.HasKey(e => e.RecipientId);
            entity.ToTable("promo_recipients");
            entity.HasIndex(e => new { e.PromoId, e.UserId }, "ux_promo_user").IsUnique();
            entity.HasIndex(e => e.UserId, "ix_recipient_user");
            entity.Property(e => e.RecipientId).HasColumnName("recipient_id");
            entity.Property(e => e.PromoId).HasColumnName("promo_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.SentAt).HasColumnType("datetime").HasColumnName("sent_at");
            entity.Property(e => e.SentBy).HasMaxLength(150).HasColumnName("sent_by");
            entity.HasOne(d => d.Promo).WithMany(p => p.Recipients).HasForeignKey(d => d.PromoId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("promo_recipients_ibfk_1");
            entity.HasOne(d => d.User).WithMany().HasForeignKey(d => d.UserId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("promo_recipients_ibfk_2");
        });

        modelBuilder.Entity<ContactMessage>(entity =>
        {
            entity.HasKey(e => e.MessageId);
            entity.ToTable("contact_messages");
            entity.HasIndex(e => new { e.Status, e.CreatedAt }, "ix_contact_status");
            entity.HasIndex(e => new { e.Ip, e.CreatedAt }, "ix_contact_ip");
            entity.Property(e => e.MessageId).HasColumnName("message_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Name).HasMaxLength(100).HasColumnName("name");
            entity.Property(e => e.Email).HasMaxLength(150).HasColumnName("email");
            entity.Property(e => e.Phone).HasMaxLength(20).HasColumnName("phone");
            entity.Property(e => e.Topic).HasMaxLength(20).HasColumnName("topic");
            entity.Property(e => e.Message).HasMaxLength(2000).HasColumnName("message");
            entity.Property(e => e.Status).HasMaxLength(10).HasColumnName("status");
            entity.Property(e => e.StaffNote).HasMaxLength(500).HasColumnName("staff_note");
            entity.Property(e => e.HandledBy).HasMaxLength(150).HasColumnName("handled_by");
            entity.Property(e => e.Ip).HasMaxLength(45).HasColumnName("ip");
            entity.Property(e => e.CreatedAt).HasColumnType("datetime").HasColumnName("created_at");
            entity.Property(e => e.HandledAt).HasColumnType("datetime").HasColumnName("handled_at");
            entity.HasOne<User>().WithMany().HasForeignKey(d => d.UserId).OnDelete(DeleteBehavior.SetNull).HasConstraintName("contact_messages_ibfk_1");
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(e => e.NotificationId);
            entity.ToTable("notifications");
            entity.HasIndex(e => new { e.UserId, e.IsRead, e.CreatedAt }, "ix_notify_user");
            entity.Property(e => e.NotificationId).HasColumnName("notification_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Title).HasMaxLength(150).HasColumnName("title");
            entity.Property(e => e.Body).HasMaxLength(500).HasColumnName("body");
            entity.Property(e => e.Link).HasMaxLength(200).HasColumnName("link");
            entity.Property(e => e.IsRead).HasColumnName("is_read");
            entity.Property(e => e.CreatedAt).HasColumnType("datetime").HasColumnName("created_at");
            entity.HasOne<User>().WithMany().HasForeignKey(d => d.UserId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("notifications_ibfk_1");
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.AuditId);
            entity.ToTable("audit_logs");
            entity.HasIndex(e => e.CreatedAt, "ix_audit_created");
            entity.Property(e => e.AuditId).HasColumnName("audit_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Actor).HasMaxLength(100).HasColumnName("actor");
            entity.Property(e => e.ActorRole).HasMaxLength(30).HasColumnName("actor_role");
            entity.Property(e => e.Action).HasMaxLength(60).HasColumnName("action");
            entity.Property(e => e.TargetId).HasColumnName("target_id");
            entity.Property(e => e.Success).HasColumnName("success");
            entity.Property(e => e.Detail).HasMaxLength(500).HasColumnName("detail");
            entity.Property(e => e.Ip).HasMaxLength(45).HasColumnName("ip");
            entity.Property(e => e.CreatedAt).HasColumnType("datetime").HasColumnName("created_at");
        });

        modelBuilder.Entity<Expense>(entity =>
        {
            entity.HasKey(e => e.ExpenseId).HasName("PRIMARY");
            entity.ToTable("expenses");
            entity.HasIndex(e => e.SpentOn, "ix_spent_on");
            entity.HasIndex(e => e.CreatedBy, "ix_created_by");
            entity.Property(e => e.ExpenseId).HasColumnName("expense_id");
            entity.Property(e => e.Category).HasMaxLength(50).HasColumnName("category");
            entity.Property(e => e.Amount).HasPrecision(10, 2).HasColumnName("amount");
            entity.Property(e => e.SpentOn).HasColumnName("spent_on");
            entity.Property(e => e.Note).HasMaxLength(255).HasColumnName("note");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP").HasColumnType("datetime").HasColumnName("created_at");
            entity.HasOne(d => d.CreatedByNavigation).WithMany().HasForeignKey(d => d.CreatedBy)
                .OnDelete(DeleteBehavior.SetNull).HasConstraintName("expenses_ibfk_1");
        });

        modelBuilder.Entity<HousekeepingTask>(entity =>
        {
            entity.HasKey(e => e.TaskId).HasName("PRIMARY");

            entity.ToTable("housekeeping_tasks");

            entity.HasIndex(e => e.AssignedTo, "assigned_to");

            entity.HasIndex(e => e.BookingId, "booking_id");

            entity.HasIndex(e => e.RoomId, "room_id");

            entity.Property(e => e.TaskId).HasColumnName("task_id");
            entity.Property(e => e.AssignedTo).HasColumnName("assigned_to");
            entity.Property(e => e.BookingId).HasColumnName("booking_id");
            entity.Property(e => e.DoneAt)
                .HasColumnType("datetime")
                .HasColumnName("done_at");
            entity.Property(e => e.RoomId).HasColumnName("room_id");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'PENDING'")
                .HasColumnType("enum('PENDING','CLEANING','DONE')")
                .HasColumnName("status");

            entity.HasOne(d => d.AssignedToNavigation).WithMany(p => p.HousekeepingTasks)
                .HasForeignKey(d => d.AssignedTo)
                .HasConstraintName("housekeeping_tasks_ibfk_3");

            entity.HasOne(d => d.Booking).WithMany(p => p.HousekeepingTasks)
                .HasForeignKey(d => d.BookingId)
                .HasConstraintName("housekeeping_tasks_ibfk_2");

            entity.HasOne(d => d.Room).WithMany(p => p.HousekeepingTasks)
                .HasForeignKey(d => d.RoomId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("housekeeping_tasks_ibfk_1");
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.HasKey(e => e.PaymentId).HasName("PRIMARY");

            entity.ToTable("payments");

            entity.HasIndex(e => e.BookingId, "booking_id");

            entity.HasIndex(e => e.HandledBy, "handled_by");

            entity.Property(e => e.PaymentId).HasColumnName("payment_id");
            entity.Property(e => e.Amount)
                .HasPrecision(10, 2)
                .HasColumnName("amount");
            entity.Property(e => e.BookingId).HasColumnName("booking_id");
            entity.Property(e => e.HandledBy).HasColumnName("handled_by");
            entity.Property(e => e.Method)
                .HasDefaultValueSql("'QR'")
                .HasColumnType("enum('CASH','QR','CARD')")
                .HasColumnName("method");
            entity.Property(e => e.PaidAt)
                .HasColumnType("datetime")
                .HasColumnName("paid_at");
            entity.Property(e => e.SlipPath).HasMaxLength(255).HasColumnName("slip_path");
            entity.Property(e => e.Reference).HasMaxLength(100).HasColumnName("reference");
            entity.Property(e => e.PaymentType)
                .HasDefaultValueSql("'PAYMENT'")
                .HasColumnType("enum('PAYMENT','REFUND')")
                .HasColumnName("payment_type");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'PENDING'")
                .HasColumnType("enum('PENDING','PAID','REFUNDED','REJECTED')")
                .HasColumnName("status");

            entity.HasOne(d => d.Booking).WithMany(p => p.Payments)
                .HasForeignKey(d => d.BookingId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("payments_ibfk_1");

            entity.HasOne(d => d.HandledByNavigation).WithMany(p => p.Payments)
                .HasForeignKey(d => d.HandledBy)
                .HasConstraintName("payments_ibfk_2");
        });

        modelBuilder.Entity<Promotion>(entity =>
        {
            entity.HasKey(e => e.PromoId).HasName("PRIMARY");

            entity.ToTable("promotions");

            entity.Property(e => e.PromoId).HasColumnName("promo_id");
            entity.Property(e => e.DiscountPercent)
                .HasPrecision(5, 2)
                .HasColumnName("discount_percent");
            entity.Property(e => e.EndDate).HasColumnName("end_date");
            entity.Property(e => e.IsActive)
                .IsRequired()
                .HasDefaultValueSql("'1'")
                .HasColumnName("is_active");
            entity.Property(e => e.MinAdvanceDays).HasColumnName("min_advance_days");
            entity.Property(e => e.NeedStaffConfirm).HasColumnName("need_staff_confirm");
            entity.Property(e => e.PromoName)
                .HasMaxLength(100)
                .HasColumnName("promo_name");
            entity.Property(e => e.PromoType)
                .HasColumnType("enum('MEMBER','MOTHERS_DAY','EARLY_BIRD','BIRTHDAY','ANNIVERSARY')")
                .HasColumnName("promo_type");
            entity.Property(e => e.StartDate).HasColumnName("start_date");
            entity.Property(e => e.PromoCode).HasMaxLength(30).HasColumnName("promo_code");
            entity.Property(e => e.Description).HasMaxLength(300).HasColumnName("description");
            entity.Property(e => e.MinNights).HasColumnName("min_nights");
            entity.Property(e => e.MaxUses).HasColumnName("max_uses");
            entity.Property(e => e.MaxDiscount).HasPrecision(10, 2).HasColumnName("max_discount");
            entity.Property(e => e.MaxPerUser).HasColumnName("max_per_user");
            entity.Property(e => e.RecipientsOnly).HasColumnName("recipients_only");
            entity.Property(e => e.Claimable).HasColumnName("claimable");
            entity.Property(e => e.ClaimFrom).HasColumnName("claim_from");
            entity.Property(e => e.ClaimUntil).HasColumnName("claim_until");
            entity.Property(e => e.Featured).HasColumnName("featured");
            entity.Property(e => e.AutoKey).HasMaxLength(40).HasColumnName("auto_key");
            entity.HasIndex(e => e.AutoKey, "ux_promo_auto").IsUnique();
        });

        modelBuilder.Entity<Review>(entity =>
        {
            entity.HasKey(e => e.ReviewId).HasName("PRIMARY");

            entity.ToTable("reviews");

            entity.HasIndex(e => e.BookingId, "booking_id").IsUnique();

            entity.HasIndex(e => e.UserId, "user_id");

            entity.Property(e => e.ReviewId).HasColumnName("review_id");
            entity.Property(e => e.BookingId).HasColumnName("booking_id");
            entity.Property(e => e.Comment)
                .HasMaxLength(500)
                .HasColumnName("comment");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("datetime")
                .HasColumnName("created_at");
            entity.Property(e => e.Rating).HasColumnName("rating");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Booking).WithOne(p => p.Review)
                .HasForeignKey<Review>(d => d.BookingId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("reviews_ibfk_1");

            entity.HasOne(d => d.User).WithMany(p => p.Reviews)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("reviews_ibfk_2");
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.RoleId).HasName("PRIMARY");

            entity.ToTable("roles");

            entity.HasIndex(e => e.RoleName, "role_name").IsUnique();

            entity.Property(e => e.RoleId).HasColumnName("role_id");
            entity.Property(e => e.RoleName)
                .HasMaxLength(30)
                .HasColumnName("role_name")
                // ฐานข้อมูลบางเครื่องเก็บชื่อบทบาทเป็นตัวพิมพ์ใหญ่ (เช่น CUSTOMER) — อ่านเป็นตัวพิมพ์เล็กเสมอ
                // ให้ตรงกับ RoleNames ในโค้ด (สิทธิ์ [Authorize(Roles=...)] เทียบแบบตรงตัว) โดยไม่แก้ข้อมูลเดิม
                .HasConversion(v => v, v => v.ToLowerInvariant());
        });

        modelBuilder.Entity<Room>(entity =>
        {
            entity.HasKey(e => e.RoomId).HasName("PRIMARY");

            entity.ToTable("rooms");

            entity.HasIndex(e => e.RoomNo, "room_no").IsUnique();

            entity.HasIndex(e => e.ThemeId, "theme_id");

            entity.HasIndex(e => e.TypeId, "type_id");

            entity.Property(e => e.RoomId).HasColumnName("room_id");
            entity.Property(e => e.RoomNo)
                .HasMaxLength(10)
                .HasColumnName("room_no");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'AVAILABLE'")
                .HasColumnType("enum('AVAILABLE','BOOKED','OCCUPIED','CLEANING','MAINTENANCE')")
                .HasColumnName("status");
            entity.Property(e => e.ThemeId).HasColumnName("theme_id");
            entity.Property(e => e.TypeId).HasColumnName("type_id");

            entity.HasOne(d => d.Theme).WithMany(p => p.Rooms)
                .HasForeignKey(d => d.ThemeId)
                .HasConstraintName("rooms_ibfk_2");

            entity.HasOne(d => d.Type).WithMany(p => p.Rooms)
                .HasForeignKey(d => d.TypeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("rooms_ibfk_1");
        });

        modelBuilder.Entity<RoomTheme>(entity =>
        {
            entity.HasKey(e => e.ThemeId).HasName("PRIMARY");

            entity.ToTable("room_themes");

            entity.HasIndex(e => e.ThemeName, "theme_name").IsUnique();

            entity.Property(e => e.ThemeId).HasColumnName("theme_id");
            entity.Property(e => e.Description)
                .HasMaxLength(255)
                .HasColumnName("description");
            entity.Property(e => e.ThemeName)
                .HasMaxLength(50)
                .HasColumnName("theme_name");
        });

        modelBuilder.Entity<RoomType>(entity =>
        {
            entity.HasKey(e => e.TypeId).HasName("PRIMARY");

            entity.ToTable("room_types");

            entity.HasIndex(e => e.TypeName, "type_name").IsUnique();

            entity.Property(e => e.TypeId).HasColumnName("type_id");
            entity.Property(e => e.BasePrice)
                .HasPrecision(10, 2)
                .HasColumnName("base_price");
            entity.Property(e => e.WeekendPrice).HasPrecision(10, 2).HasColumnName("weekend_price");
            entity.Property(e => e.Description).HasColumnType("text").HasColumnName("description");
            entity.Property(e => e.Amenities).HasMaxLength(500).HasColumnName("amenities");
            entity.Property(e => e.ImageUrl).HasMaxLength(255).HasColumnName("image_url");
            entity.Property(e => e.MaxGuests)
                .HasDefaultValueSql("'2'")
                .HasColumnName("max_guests");
            entity.Property(e => e.TypeName)
                .HasMaxLength(50)
                .HasColumnName("type_name");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("PRIMARY");

            entity.ToTable("users");

            entity.HasIndex(e => e.Email, "email").IsUnique();

            entity.HasIndex(e => e.Phone, "phone").IsUnique();

            entity.HasIndex(e => e.RoleId, "role_id");

            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.AnniversaryDate).HasColumnName("anniversary_date");
            entity.Property(e => e.BirthDate).HasColumnName("birth_date");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("datetime")
                .HasColumnName("created_at");
            entity.Property(e => e.Email)
                .HasMaxLength(100)
                .HasColumnName("email");
            entity.Property(e => e.FirstName)
                .HasMaxLength(50)
                .HasColumnName("first_name");
            entity.Property(e => e.IsMember).HasColumnName("is_member");
            entity.Property(e => e.LastName)
                .HasMaxLength(50)
                .HasColumnName("last_name");
            entity.Property(e => e.PasswordHash)
                .HasMaxLength(255)
                .HasColumnName("password_hash");
            entity.Property(e => e.SecurityStamp)
                .HasMaxLength(32)
                .HasColumnName("security_stamp");
            entity.Property(e => e.Phone)
                .HasMaxLength(15)
                .HasColumnName("phone");
            entity.Property(e => e.RoleId).HasColumnName("role_id");

            entity.HasOne(d => d.Role).WithMany(p => p.Users)
                .HasForeignKey(d => d.RoleId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("users_ibfk_1");
        });

        modelBuilder.Entity<LoginHistory>(entity =>
        {
            entity.HasKey(e => e.LoginHistoryId);

            entity.ToTable("login_history");

            entity.Property(e => e.LoginHistoryId).HasColumnName("login_history_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Email)
                .HasMaxLength(100)
                .HasColumnName("email");
            entity.Property(e => e.LoggedInAtUtc)
                .HasColumnType("datetime")
                .HasColumnName("logged_in_at_utc");

            entity.HasOne(d => d.User).WithMany()
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("fk_login_history_user");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
