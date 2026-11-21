using System.Collections.Generic;
using System;

namespace pameka_7033_l005.Models;

// ตารางในฐานข้อมูล (1 class = 1 ตาราง) — ความสัมพันธ์/ชื่อคอลัมน์กำหนดใน Csi402dbContext.cs

// ======================================================================
// Booking
// ======================================================================
public partial class Booking
{
    public int BookingId { get; set; }

    public int UserId { get; set; }

    public int? PromoId { get; set; }

    public int? ConfirmedBy { get; set; }

    public DateOnly CheckIn { get; set; }

    public DateOnly CheckOut { get; set; }

    public DateTime BookedAt { get; set; }

    public string Status { get; set; } = null!;

    public decimal Subtotal { get; set; }

    public decimal DiscountTotal { get; set; }

    public decimal NetTotal { get; set; }

    /// <summary>จำนวนผู้เข้าพัก (null = การจองก่อน Part 13 ที่ยังไม่ได้เก็บ)</summary>
    public sbyte? Guests { get; set; }

    /// <summary>Part 50: เวลาที่ลูกค้าแจ้งว่าจะมาถึง (HH:mm) — เช็คอินออนไลน์ล่วงหน้า</summary>
    public string? ArrivalTime { get; set; }
    /// <summary>Part 50: คำขอพิเศษจากลูกค้า (ไม่เกิน 300 ตัวอักษร)</summary>
    public string? GuestRequest { get; set; }

    public virtual ICollection<BookingRoom> BookingRooms { get; set; } = new List<BookingRoom>();

    public virtual ICollection<BookingService> BookingServices { get; set; } = new List<BookingService>();

    public virtual User? ConfirmedByNavigation { get; set; }

    public virtual ICollection<HousekeepingTask> HousekeepingTasks { get; set; } = new List<HousekeepingTask>();

    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();

    public virtual Promotion? Promo { get; set; }

    public virtual Review? Review { get; set; }

    public virtual User User { get; set; } = null!;
}

// ======================================================================
// BookingRoom
// ======================================================================
public partial class BookingRoom
{
    public int BrId { get; set; }

    public int BookingId { get; set; }

    public int RoomId { get; set; }

    public decimal PricePerNight { get; set; }

    public short Nights { get; set; }

    public virtual Booking Booking { get; set; } = null!;

    public virtual Room Room { get; set; } = null!;
}

// ======================================================================
// BookingService
// ======================================================================
public partial class BookingService
{
    public int BsId { get; set; }

    public int BookingId { get; set; }

    public int RoomId { get; set; }

    public int ServiceId { get; set; }

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public virtual Booking Booking { get; set; } = null!;

    public virtual ICollection<DeliveryTask> DeliveryTasks { get; set; } = new List<DeliveryTask>();

    public virtual Room Room { get; set; } = null!;

    public virtual ExtraService Service { get; set; } = null!;
}

// ======================================================================
// DeliveryTask
// ======================================================================
public partial class DeliveryTask
{
    public int TaskId { get; set; }

    public int BsId { get; set; }

    public int RoomId { get; set; }

    public int? AssignedTo { get; set; }

    public string Status { get; set; } = null!;

    public DateTime? DoneAt { get; set; }

    public virtual User? AssignedToNavigation { get; set; }

    public virtual BookingService Bs { get; set; } = null!;

    public virtual Room Room { get; set; } = null!;
}

// ======================================================================
// ErrorViewModel
// ======================================================================
public class ErrorViewModel
{
    public int StatusCode { get; set; } = 500;

    public string? RequestId { get; set; }

    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
}

// ======================================================================
// ExtraService
// ======================================================================
public partial class ExtraService
{
    public int ServiceId { get; set; }

    public string ServiceName { get; set; } = null!;

    public decimal Price { get; set; }

    public bool? IsActive { get; set; }

    /// <summary>SPA / DINE / ROOM / OTHER (null = ให้ระบบเดาจากชื่อ)</summary>
    public string? Category { get; set; }

    public string? Description { get; set; }

    public string? ImageUrl { get; set; }

    public virtual ICollection<BookingService> BookingServices { get; set; } = new List<BookingService>();
}

// ======================================================================
// HousekeepingTask
// ======================================================================
public partial class HousekeepingTask
{
    public int TaskId { get; set; }

    public int RoomId { get; set; }

    public int? BookingId { get; set; }

    public int? AssignedTo { get; set; }

    public string Status { get; set; } = null!;

    public DateTime? DoneAt { get; set; }

    public virtual User? AssignedToNavigation { get; set; }

    public virtual Booking? Booking { get; set; }

    public virtual Room Room { get; set; } = null!;
}

// ======================================================================
// LoginHistory
// ======================================================================
public class LoginHistory
{
    public int LoginHistoryId { get; set; }

    public int UserId { get; set; }

    public string Email { get; set; } = "";

    public DateTime LoggedInAtUtc { get; set; }

    public virtual User User { get; set; } = null!;
}

// ======================================================================
// Payment
// ======================================================================
public partial class Payment
{
    public int PaymentId { get; set; }

    public int BookingId { get; set; }

    public decimal Amount { get; set; }

    public string PaymentType { get; set; } = null!;

    public string Method { get; set; } = null!;

    public string Status { get; set; } = null!;

    public int? HandledBy { get; set; }

    public DateTime? PaidAt { get; set; }

    /// <summary>ไฟล์สลิปที่ลูกค้าแนบ (ชื่อไฟล์ใน App_Data/slips ไม่เปิดเป็นไฟล์สาธารณะ)</summary>
    public string? SlipPath { get; set; }

    /// <summary>เลขอ้างอิงธุรกรรมจากสลิปธนาคาร (ไม่บังคับ)</summary>
    public string? Reference { get; set; }

    /// <summary>Part 53: ผลตรวจอัตโนมัติ (SlipOK / payment gateway) ให้ฝ่ายบัญชีเห็น</summary>
    public string? VerifyNote { get; set; }

    /// <summary>Part 53: เลขธุรกรรมจากสลิป (SlipOK) หรือ charge id ของ gateway — ใช้กันสลิป/รายการซ้ำ</summary>
    public string? TransRef { get; set; }

    public virtual Booking Booking { get; set; } = null!;

    public virtual User? HandledByNavigation { get; set; }
}

// ======================================================================
// Promotion
// ======================================================================
public partial class Promotion
{
    public int PromoId { get; set; }

    public string PromoName { get; set; } = null!;

    public string PromoType { get; set; } = null!;

    public decimal DiscountPercent { get; set; }

    public short? MinAdvanceDays { get; set; }

    public bool NeedStaffConfirm { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public bool? IsActive { get; set; }

    /// <summary>รหัสโปรโมชั่น (ตัวพิมพ์ใหญ่) — ใช้กับประเภท CODE</summary>
    public string? PromoCode { get; set; }

    public string? Description { get; set; }

    /// <summary>ต้องพักอย่างน้อยกี่คืน (ใช้ได้กับทุกประเภท)</summary>
    public short? MinNights { get; set; }

    /// <summary>ใช้ได้รวมกี่ครั้ง (นับการจองที่ไม่ถูกยกเลิก) null = ไม่จำกัด</summary>
    public int? MaxUses { get; set; }

    /// <summary>ส่วนลดสูงสุดต่อการจอง (บาท) null = ไม่จำกัด</summary>
    public decimal? MaxDiscount { get; set; }

    /// <summary>ลูกค้าหนึ่งคนใช้ได้กี่ครั้ง null = ไม่จำกัด</summary>
    public short? MaxPerUser { get; set; }

    /// <summary>รหัสใช้ได้เฉพาะลูกค้าที่ผู้จัดการส่งรหัสให้</summary>
    public bool RecipientsOnly { get; set; }

    // ---- Part 49 ----
    /// <summary>คูปองต้องกด "เก็บ" ก่อน แล้วระบบใช้ให้อัตโนมัติตอนจอง (ไม่ต้องพิมพ์รหัส)</summary>
    public bool Claimable { get; set; }
    /// <summary>ช่วงวันที่กดเก็บคูปองได้ (null = ตลอดช่วงโปร)</summary>
    public DateOnly? ClaimFrom { get; set; }
    public DateOnly? ClaimUntil { get; set; }
    /// <summary>แสดงเป็นแบนเนอร์/โฆษณาบนหน้าแรก</summary>
    public bool Featured { get; set; }
    /// <summary>รหัสแคมเปญเทศกาลที่ระบบสร้างอัตโนมัติ เช่น p-songkran-2027 (null = ผู้จัดการสร้างเอง)</summary>
    public string? AutoKey { get; set; }

    public virtual ICollection<Booking> Bookings { get; set; } = new List<Booking>();

    public virtual ICollection<PromoRecipient> Recipients { get; set; } = new List<PromoRecipient>();
}

// ======================================================================
// Review
// ======================================================================
public partial class Review
{
    public int ReviewId { get; set; }

    public int BookingId { get; set; }

    public int UserId { get; set; }

    public sbyte Rating { get; set; }

    public string? Comment { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Booking Booking { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}

// ======================================================================
// Role
// ======================================================================
public partial class Role
{
    public sbyte RoleId { get; set; }

    public string RoleName { get; set; } = null!;

    public virtual ICollection<User> Users { get; set; } = new List<User>();
}

// ======================================================================
// Room
// ======================================================================
public partial class Room
{
    public int RoomId { get; set; }

    public string RoomNo { get; set; } = null!;

    public int TypeId { get; set; }

    public int? ThemeId { get; set; }

    public string Status { get; set; } = null!;

    public virtual ICollection<BookingRoom> BookingRooms { get; set; } = new List<BookingRoom>();

    public virtual ICollection<BookingService> BookingServices { get; set; } = new List<BookingService>();

    public virtual ICollection<DeliveryTask> DeliveryTasks { get; set; } = new List<DeliveryTask>();

    public virtual ICollection<HousekeepingTask> HousekeepingTasks { get; set; } = new List<HousekeepingTask>();

    public virtual RoomTheme? Theme { get; set; }

    public virtual RoomType Type { get; set; } = null!;
}

// ======================================================================
// RoomTheme
// ======================================================================
public partial class RoomTheme
{
    public int ThemeId { get; set; }

    public string ThemeName { get; set; } = null!;

    public string? Description { get; set; }

    public virtual ICollection<Room> Rooms { get; set; } = new List<Room>();
}

// ======================================================================
// RoomType
// ======================================================================
public partial class RoomType
{
    public int TypeId { get; set; }

    public string TypeName { get; set; } = null!;

    public decimal BasePrice { get; set; }

    /// <summary>ราคาคืนวันศุกร์/เสาร์ (null = ใช้ราคาปกติ) — Part 43</summary>
    public decimal? WeekendPrice { get; set; }

    public sbyte MaxGuests { get; set; }

    public string? Description { get; set; }

    /// <summary>สิ่งอำนวยความสะดวก คั่นด้วย ,</summary>
    public string? Amenities { get; set; }

    /// <summary>รูปห้อง เช่น /images/rooms/xxx.jpg</summary>
    public string? ImageUrl { get; set; }

    public virtual ICollection<Room> Rooms { get; set; } = new List<Room>();
}

// ======================================================================
// User
// ======================================================================
public partial class User
{
    public int UserId { get; set; }

    /// <summary>ตราประจำเซสชัน: เปลี่ยนเมื่อรหัสผ่านเปลี่ยนหรือสั่งออกจากระบบทุกอุปกรณ์ (เซสชันที่ถือตราเก่าถูกตัดทันที)</summary>
    public string? SecurityStamp { get; set; }

    /// <summary>ตั้งรหัสผ่านใหม่ + เปลี่ยนตราประจำเซสชัน (ทุกจุดที่ตั้งรหัสผ่านต้องใช้เมธอดนี้)</summary>
    public void SetPassword(string plain)
    {
        PasswordHash = pameka_7033_l005.Helpers.PasswordHelper.Hash(plain);
        RotateStamp();
    }

    public void RotateStamp() => SecurityStamp = Guid.NewGuid().ToString("N");

    public sbyte RoleId { get; set; }

    public string FirstName { get; set; } = null!;

    public string LastName { get; set; } = null!;

    public string Phone { get; set; } = null!;

    public string Email { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public DateOnly? BirthDate { get; set; }

    public DateOnly? AnniversaryDate { get; set; }

    public bool IsMember { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<Booking> BookingConfirmedByNavigations { get; set; } = new List<Booking>();

    public virtual ICollection<Booking> BookingUsers { get; set; } = new List<Booking>();

    public virtual ICollection<DeliveryTask> DeliveryTasks { get; set; } = new List<DeliveryTask>();

    public virtual ICollection<HousekeepingTask> HousekeepingTasks { get; set; } = new List<HousekeepingTask>();

    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();

    public virtual ICollection<Review> Reviews { get; set; } = new List<Review>();

    public virtual Role Role { get; set; } = null!;
}

// ======================================================================
// Expense — รายจ่ายของโรงแรม (ฝ่ายบัญชีบันทึก)
// ======================================================================
/// <summary>การแจ้งเตือนในระบบของผู้ใช้ (notifications)</summary>
public class Notification
{
    public long NotificationId { get; set; }
    public int UserId { get; set; }
    public string Title { get; set; } = "";
    public string? Body { get; set; }
    public string? Link { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>Part 52: เวลาที่ส่งเป็นอีเมลแล้ว (NULL = ยังไม่ส่ง)</summary>
    public DateTime? EmailedAt { get; set; }
}

/// <summary>ข้อความติดต่อโรงแรมจากหน้า "ติดต่อเรา" (contact_messages) — รวมคำขอรีเซ็ตรหัสผ่าน</summary>
public class ContactMessage
{
    public long MessageId { get; set; }
    public int? UserId { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Phone { get; set; }
    public string Topic { get; set; } = "GENERAL";
    public string Message { get; set; } = "";
    public string Status { get; set; } = "NEW";
    public string? StaffNote { get; set; }
    public string? HandledBy { get; set; }
    public string? Ip { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? HandledAt { get; set; }
}

/// <summary>บันทึกกิจกรรม (audit_logs) — เขียนอย่างเดียว ไม่มีหน้าแก้ไข/ลบ</summary>
public class AuditLog
{
    public long AuditId { get; set; }
    public int? UserId { get; set; }
    public string Actor { get; set; } = "";
    public string ActorRole { get; set; } = "";
    public string Action { get; set; } = "";
    public int? TargetId { get; set; }
    public bool Success { get; set; }
    public string? Detail { get; set; }
    public string? Ip { get; set; }
    public DateTime CreatedAt { get; set; }
}

public partial class Expense
{
    public int ExpenseId { get; set; }

    public string Category { get; set; } = null!;

    public decimal Amount { get; set; }

    public DateOnly SpentOn { get; set; }

    public string? Note { get; set; }

    public int? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual User? CreatedByNavigation { get; set; }
}

/// <summary>ลูกค้าที่ได้รับรหัสโปรโมชั่น (Part 38)</summary>
public class PromoRecipient
{
    public long RecipientId { get; set; }
    public int PromoId { get; set; }
    public int UserId { get; set; }
    public DateTime SentAt { get; set; }
    public string? SentBy { get; set; }
    public virtual Promotion Promo { get; set; } = null!;
    public virtual User User { get; set; } = null!;
}

/// <summary>ช่วงราคาพิเศษ ปรับราคาห้องทุกประเภท +/− % ในช่วงวันที่ (Part 43)</summary>
public class RateSeason
{
    public int SeasonId { get; set; }
    public string SeasonName { get; set; } = null!;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal AdjustPercent { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    /// <summary>Part 47: รหัสเทศกาลที่ระบบสร้างให้อัตโนมัติ เช่น songkran-2027 (null = ผู้จัดการตั้งเอง)</summary>
    public string? AutoKey { get; set; }
}
