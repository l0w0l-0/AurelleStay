using System.ComponentModel.DataAnnotations;
using pameka_7033_l005.Helpers;

namespace pameka_7033_l005.ViewModels;

// ข้อมูลที่ส่งระหว่าง controller กับหน้าเว็บ (ฟอร์ม + ข้อมูลแสดงผล) ทั้งระบบ

// ======================================================================
// PersonInfoViewModel
// ======================================================================
/// <summary>ชื่อ/นามสกุล/เบอร์/อีเมล พร้อมกฎตรวจสอบ — ใช้ร่วมกันทั้งสมัครสมาชิก แก้ไขผู้ใช้ และสร้างพนักงาน</summary>
public abstract class PersonInfoViewModel
{
    [Required(ErrorMessage = "กรุณากรอกชื่อ")]
    [StringLength(50, ErrorMessage = "ชื่อยาวไม่เกิน 50 ตัวอักษร")]   // ตรงกับ HasMaxLength(50) ใน Csi402dbContext
    public string FirstName { get; set; } = "";

    [Required(ErrorMessage = "กรุณากรอกนามสกุล")]
    [StringLength(50, ErrorMessage = "นามสกุลยาวไม่เกิน 50 ตัวอักษร")]
    public string LastName { get; set; } = "";

    [Required(ErrorMessage = "กรุณากรอกเบอร์โทร")]
    [RegularExpression(@"^\+?[0-9]{9,14}$", ErrorMessage = "เบอร์โทรต้องเป็นตัวเลข 9-14 หลัก")]
    public string Phone { get; set; } = "";

    [Required(ErrorMessage = "กรุณากรอกอีเมล")]
    [EmailAddress(ErrorMessage = "รูปแบบอีเมลไม่ถูกต้อง")]
    [StringLength(100, ErrorMessage = "อีเมลยาวไม่เกิน 100 ตัวอักษร")]
    public string Email { get; set; } = "";
}

// ======================================================================
// LoginViewModel
// ======================================================================
public class LoginViewModel
{
    [Required(ErrorMessage = "กรุณากรอกอีเมล")]
    [EmailAddress(ErrorMessage = "รูปแบบอีเมลไม่ถูกต้อง")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "กรุณากรอกรหัสผ่าน")]
    public string Password { get; set; } = "";

    /// <summary>Part 49: ไม่ต้องเลือกบทบาทแล้ว — ระบบใช้บทบาทของบัญชีเอง (คงช่องไว้เพื่อบันทึกในประวัติเท่านั้น)</summary>
    public string? Role { get; set; }
}

// ======================================================================
// RegisterViewModel
// ======================================================================
public class RegisterViewModel : PersonInfoViewModel
{
    [Required(ErrorMessage = "กรุณากรอกรหัสผ่าน")]
    [StringLength(100, ErrorMessage = "รหัสผ่านยาวไม่เกิน 100 ตัวอักษร"), PasswordPolicy]
    public string Password { get; set; } = "";

    [Required(ErrorMessage = "กรุณายืนยันรหัสผ่าน")]
    [Compare(nameof(Password), ErrorMessage = "รหัสผ่านไม่ตรงกัน")]
    public string ConfirmPassword { get; set; } = "";

    // ความยินยอมตาม พ.ร.บ.คุ้มครองข้อมูลส่วนบุคคล: ต้องยอมรับเงื่อนไขและนโยบายก่อนสร้างบัญชี (ตรวจฝั่งเซิร์ฟเวอร์)
    [Range(typeof(bool), "true", "true", ErrorMessage = "กรุณายอมรับเงื่อนไขการจองและนโยบายความเป็นส่วนตัว")]
    public bool AcceptPolicy { get; set; }
}

// ======================================================================
// AuthPageViewModel
// ======================================================================
public class AuthPageViewModel
{
    public LoginViewModel Login { get; set; } = new();
    public RegisterViewModel Register { get; set; } = new();
    public string ActiveTab { get; set; } = "login";  
    public string? Message { get; set; }               
    public bool IsError { get; set; }                  
    public string? ReturnUrl { get; set; }           
}

// ======================================================================
// UserListItemViewModel
// ======================================================================
public class UserListItemViewModel
{
    public int UserId { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string RoleName { get; set; } = "";
    public bool IsMember { get; set; }
    public DateTime CreatedAt { get; set; }
}

// ======================================================================
// UserListPageViewModel
// ======================================================================
public class UserListPageViewModel
{
    public List<UserListItemViewModel> Items { get; set; } = new();
    public string? Query { get; set; }
    public string? Role { get; set; }
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
    public int TotalCount { get; set; }
}

// ======================================================================
// SettingsViewModels
// ======================================================================
/// <summary>ข้อมูลส่วนตัวที่แก้ได้ (ชื่อ/เบอร์/อีเมลมาจากฐานร่วม) พร้อมกฎวันที่</summary>
public class ProfileEditViewModel : PersonInfoViewModel, IValidatableObject
{
    [DataType(DataType.Date)]
    public DateOnly? BirthDate { get; set; }            // ใช้กับโปรวันเกิด

    [DataType(DataType.Date)]
    public DateOnly? AnniversaryDate { get; set; }      // ใช้กับโปรครบรอบ

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        var today = DateOnly.FromDateTime(pameka_7033_l005.Helpers.AppClock.Today);
        if (BirthDate > today) yield return new ValidationResult("วันเกิดต้องไม่เป็นวันในอนาคต", new[] { nameof(BirthDate) });
        if (AnniversaryDate > today) yield return new ValidationResult("วันครบรอบต้องไม่เป็นวันในอนาคต", new[] { nameof(AnniversaryDate) });
    }
}

public class ChangePasswordViewModel
{
    [Required(ErrorMessage = "กรุณากรอกรหัสผ่านปัจจุบัน")]
    public string CurrentPassword { get; set; } = "";

    [Required(ErrorMessage = "กรุณากรอกรหัสผ่านใหม่")]
    [StringLength(100, ErrorMessage = "รหัสผ่านยาวไม่เกิน 100 ตัวอักษร"), PasswordPolicy]
    public string NewPassword { get; set; } = "";

    [Compare(nameof(NewPassword), ErrorMessage = "ยืนยันรหัสผ่านใหม่ไม่ตรงกัน")]
    public string ConfirmPassword { get; set; } = "";
}

public class SettingsPageViewModel
{
    public string Tab { get; set; } = "profile";
    public string FullName { get; set; } = "";
    public string RoleName { get; set; } = "";
    public bool IsMember { get; set; }
    public DateTime CreatedAt { get; set; }
    public ProfileEditViewModel Profile { get; set; } = new();
    public List<DateTime> RecentLogins { get; set; } = new();
    public int BookingCount { get; set; }
    public UiPrefs Ui { get; set; } = UiPrefs.Default;
}

// ======================================================================
// EditUserViewModel
// ======================================================================
/// <summary>ผู้จัดการแก้ข้อมูลผู้ใช้คนอื่น (ข้อมูลของตัวเองแก้ที่หน้า "ตั้งค่า")</summary>
public class EditUserViewModel : ProfileEditViewModel
{
    public int UserId { get; set; }                      // 🔑 primary key ส่งมากับ hidden field

    public bool IsMember { get; set; }                   // เฉพาะผู้จัดการเปลี่ยนได้

    // 🔐 เว้นว่าง = ไม่เปลี่ยนรหัสผ่าน (ผู้จัดการตั้งให้ผู้ใช้ที่ลืมรหัสได้โดยไม่ต้องรู้รหัสเดิม)
    [StringLength(100, ErrorMessage = "รหัสผ่านยาวไม่เกิน 100 ตัวอักษร"), PasswordPolicy]
    public string? NewPassword { get; set; }

    [Compare(nameof(NewPassword), ErrorMessage = "รหัสผ่านไม่ตรงกัน")]
    public string? ConfirmPassword { get; set; }
}

// ======================================================================
// BookingViewModels
// ======================================================================
public class RoomSearchViewModel
{
    public DateOnly? CheckIn { get; set; }
    public DateOnly? CheckOut { get; set; }
    public int? Guests { get; set; }
    public decimal? MaxPrice { get; set; }
    public int? ThemeId { get; set; }
    public int? TypeId { get; set; }
    /// <summary>price / price_desc / guests (ค่าอื่น = เลขห้อง)</summary>
    public string? Sort { get; set; }
}

public class RoomCardViewModel
{
    public int RoomId { get; set; }
    public string RoomNo { get; set; } = "";
    public int TypeId { get; set; }
    public string TypeName { get; set; } = "";
    public string? Description { get; set; }
    public string? Amenities { get; set; }
    public string? ImageUrl { get; set; }
    public IEnumerable<string> AmenityList => (Amenities ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    public decimal BasePrice { get; set; }           // ราคาต่อคืน (เมื่อเลือกวันแล้ว = เฉลี่ยต่อคืนของช่วงนั้น)
    public decimal? WeekendPrice { get; set; }       // Part 43: ราคาคืนศุกร์/เสาร์
    public decimal RegularPrice { get; set; }        // ราคาปกติก่อนปรับตามวัน
    public bool PriceVaries { get; set; }            // ราคาช่วงที่เลือกต่างจากราคาปกติ
    public string? SeasonName { get; set; }          // ช่วงราคาพิเศษที่ทับกับวันที่เลือก
    public int MaxGuests { get; set; }
    public string? ThemeName { get; set; }
    public string? ThemeDescription { get; set; }
}

public record ThemeOption(int ThemeId, string Name);

public class PromoPublicViewModel
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public decimal Percent { get; set; }
    public string? Description { get; set; }
    public string Condition { get; set; } = "";
    public DateOnly? Start { get; set; }
    public DateOnly? End { get; set; }
    public int? MinNights { get; set; }
    public int? MinAdvanceDays { get; set; }
    public int? Remaining { get; set; }
    public bool Upcoming { get; set; }
    // ---- Part 49: คูปองกดเก็บ / แบนเนอร์ ----
    public int Id { get; set; }
    public bool Claimable { get; set; }
    public DateOnly? ClaimFrom { get; set; }
    public DateOnly? ClaimUntil { get; set; }
    /// <summary>none = ใช้ได้ทันที (ไม่ต้องเก็บ) · open · soon · closed · soldout</summary>
    public string ClaimState { get; set; } = "none";
    public bool Claimed { get; set; }
    public bool UsedUp { get; set; }
    public bool Featured { get; set; }
    public string Theme { get; set; } = "gold";
    public decimal? MaxDiscount { get; set; }
}

public class PromoCardViewModel
{
    public string Name { get; set; } = "";
    public decimal Percent { get; set; }
    public string Condition { get; set; } = "";
}

public class HomePageViewModel
{
    public List<PromoCardViewModel> Promos { get; set; } = new();
    /// <summary>ประเภทห้องที่เปิดจองได้จริง (มีห้องพร้อมขายอย่างน้อย 1 ห้อง) สำหรับแสดงในหน้าแรก</summary>
    public List<RoomTypeCardViewModel> Types { get; set; } = new();
    // Part 39: ส่วนที่ช่วยปิดการขายในหน้าแรก (ข้อมูลจริงจากฐานข้อมูลทั้งหมด)
    public int ReviewCount { get; set; }
    public double ReviewAverage { get; set; }
    public List<ReviewItemViewModel> TopReviews { get; set; } = new();
    public List<ServiceItemViewModel> Services { get; set; } = new();
    public decimal? MemberPercent { get; set; }
    public decimal? BestPercent { get; set; }
    public decimal? MinPrice { get; set; }
    /// <summary>Part 49: แบนเนอร์แคมเปญเทศกาล / คูปองกดเก็บ</summary>
    public List<PromoPublicViewModel> Campaigns { get; set; } = new();
}

public record RoomTypeCardViewModel(int TypeId, string Name, decimal Price, int MaxGuests, string? ImageUrl, string? Description, int RoomCount);

public class RoomsPageViewModel
{
    public RoomSearchViewModel Search { get; set; } = new();
    public List<RoomCardViewModel> Rooms { get; set; } = new();
    public List<ThemeOption> Themes { get; set; } = new();
    public List<ThemeOption> Types { get; set; } = new();
    public string? DateError { get; set; }
}

public class ServiceItemViewModel
{
    public int ServiceId { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public string? Category { get; set; }
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
}

public class PriceSummaryViewModel
{
    public int Nights { get; set; }
    public int RoomCount { get; set; }
    public decimal RoomsTotal { get; set; }
    public decimal ServicesTotal { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal Net { get; set; }
    public string? PromoName { get; set; }
    public decimal PromoPercent { get; set; }
    public bool PromoCapped { get; set; }       // ส่วนลดชนเพดานบาท
    public bool PromoNeedsId { get; set; }      // ต้องแสดงบัตรยืนยันตอนเช็คอิน
    public string? CodeMessage { get; set; }
    public bool CodeApplied { get; set; }
}

public class BookingFormViewModel
{
    // ---- ข้อมูลที่ผู้ใช้ส่งมา
    public DateOnly? CheckIn { get; set; }
    public DateOnly? CheckOut { get; set; }
    public List<int> RoomIds { get; set; } = new();
    public int? Guests { get; set; }
    public Dictionary<int, int> ServiceQty { get; set; } = new();   // serviceId -> จำนวนต่อห้อง
    public string? CustomerEmail { get; set; }                       // ใช้เฉพาะตอนพนักงานจองให้ลูกค้า (Staff = true)
    public string? NewFirstName { get; set; }                        // ลูกค้าใหม่ที่ยังไม่มีบัญชี (walk-in) → ระบบสร้างบัญชีให้ตอนยืนยัน
    public string? NewLastName { get; set; }
    public string? NewPhone { get; set; }
    public bool NewCustomer { get; set; }                            // ระบบเติม: อีเมลนี้ยังไม่มีบัญชี
    public bool Staff { get; set; }
    [StringLength(30)]
    public string? PromoCode { get; set; }                           // รหัสโปรโมชั่นที่ลูกค้ากรอก
    public decimal? QuotedNet { get; set; }                          // ยอดที่แสดงก่อนกดยืนยัน (ต้องตรงกับตอนบันทึก)

    // ---- ข้อมูลที่ระบบเติม
    public List<RoomCardViewModel> AvailableRooms { get; set; } = new();
    public List<ServiceItemViewModel> Services { get; set; } = new();
    public PriceSummaryViewModel? Summary { get; set; }
    public string? Error { get; set; }
    /// <summary>Part 49: คูปองกดเก็บที่ใช้กับวันเข้าพักนี้ได้ (เฉพาะหน้าจองของลูกค้า)</summary>
    public List<PromoPublicViewModel> Coupons { get; set; } = new();
}

public class MyBookingItemViewModel
{
    public int BookingId { get; set; }
    public DateOnly CheckIn { get; set; }
    public DateOnly CheckOut { get; set; }
    public DateTime BookedAt { get; set; }
    public string Status { get; set; } = "";
    public decimal Net { get; set; }
    public decimal Discount { get; set; }
    public string? PromoName { get; set; }
    public List<string> Rooms { get; set; } = new();
    public bool Paid { get; set; }
    public bool PendingPayment { get; set; }
    public bool PendingRefund { get; set; }
    public bool Refunded { get; set; }
    public DateTime? LastRejectedAt { get; set; }
    public bool Reviewed { get; set; }
    public PaymentFlags Flags => new(Paid, PendingPayment, PendingRefund, Refunded);
}

// ---------- หน้าสมาชิก (Part 36)
public class MemberDashboardViewModel
{
    public string FirstName { get; set; } = "";
    public DateTime MemberSince { get; set; }
    public DateOnly Today { get; set; }
    public MyBookingItemViewModel? Next { get; set; }               // การเข้าพักครั้งถัดไป / ที่กำลังพัก
    public List<MyBookingItemViewModel> Upcoming { get; set; } = new();   // ครั้งถัดไปอื่น ๆ
    public List<MyBookingItemViewModel> NeedAction { get; set; } = new(); // รอชำระ / สลิปถูกปฏิเสธ
    public List<MyBookingItemViewModel> ToReview { get; set; } = new();
    public int Stays { get; set; }
    public int Nights { get; set; }
    public decimal Spent { get; set; }
    public decimal Saved { get; set; }
    public int Unread { get; set; }
    public List<(string Title, string? Link, DateTime At, bool Read)> Notes { get; set; } = new();
    public List<(string Name, decimal Percent, string Condition, bool Ready, string? Hint)> Perks { get; set; } = new();
    public bool HasBirthDate { get; set; }
    public bool HasAnniversary { get; set; }
    public List<(string Code, string Name, decimal Percent, string Condition)> MyCodes { get; set; } = new();
    public List<WaitlistItemViewModel> Waiting { get; set; } = new();   // Part 51: รายชื่อรอห้องว่าง
    // Part 53: ขอของใช้เพิ่มระหว่างเข้าพัก
    public int? StayBookingId { get; set; }
    public List<(int RoomId, string RoomNo)> StayRooms { get; set; } = new();
    public List<ItemRequestRowViewModel> Requests { get; set; } = new();
    // Part 54: ทำความสะอาดระหว่างพัก / ห้ามรบกวน
    public bool DoNotDisturb { get; set; }
    public List<ItemRequestRowViewModel> Cleanings { get; set; } = new();
}

public class ItemRequestRowViewModel
{
    public int Id { get; set; }
    public string What { get; set; } = "";
    public string RoomNo { get; set; } = "";
    public string? Note { get; set; }
    public bool Urgent { get; set; }
    public string Status { get; set; } = "";
    public byte Missed { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DoneAt { get; set; }
}

public class DeliveryStatsViewModel
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public int OpenNow { get; set; }
    public int LateNow { get; set; }
    public int UrgentNow { get; set; }
    public List<DeliveryStaffStat> Staff { get; set; } = new();
    public List<(string Item, int Count)> TopItems { get; set; } = new();
    // Part 54: ใช้หน้าเดียวกันสำหรับแม่บ้าน
    public bool Housekeeping { get; set; }
    public List<RoomIssueRowViewModel> Issues { get; set; } = new();
}

public class RoomIssueRowViewModel
{
    public int Id { get; set; }
    public string RoomNo { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Serious { get; set; }
    public string Status { get; set; } = "";
    public string? Reporter { get; set; }
    public string RoomStatus { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? FixedAt { get; set; }
}

public class DeliveryStaffStat
{
    public string Name { get; set; } = "";
    public int Done { get; set; }
    public int Requests { get; set; }
    public double? AvgWait { get; set; }    // นาที: เข้าคิว → รับงาน
    public double? AvgTotal { get; set; }   // นาที: เข้าคิว → ส่งแล้ว
    public int Late { get; set; }           // ใช้เวลารวมเกินเกณฑ์
}

public class PayPageViewModel
{
    public int BookingId { get; set; }
    public DateOnly CheckIn { get; set; }
    public DateOnly CheckOut { get; set; }
    public decimal Net { get; set; }
    public decimal Discount { get; set; }
    public string? PromoName { get; set; }
    public List<string> Rooms { get; set; } = new();
    public bool PendingPayment { get; set; }
    /// <summary>ชำระภายในเวลานี้ มิฉะนั้นระบบยกเลิกการจองอัตโนมัติ</summary>
    public DateTime PayBy { get; set; }
    public bool Rejected { get; set; }
    /// <summary>null = ยังไม่ได้วางรูป QR ที่ wwwroot/images/payment-qr.png</summary>
    public string? QrUrl { get; set; }
    // ---- Part 49: QR พร้อมเพย์แบบระบุยอด + บัญชีธนาคาร ----
    public string? QrPayload { get; set; }
    public pameka_7033_l005.Helpers.PaymentInfo Payment { get; set; } = new(null, null, null, null);
}

// ======================================================================
// ReviewViewModels
// ======================================================================
public class ReviewItemViewModel
{
    public int Id { get; set; }
    public int BookingId { get; set; }
    public string Name { get; set; } = "";
    public int Rating { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ReviewsPageViewModel
{
    public int Count { get; set; }
    public double Average { get; set; }
    public List<ReviewItemViewModel> Items { get; set; } = new();
}

public class ReviewFormViewModel
{
    public int BookingId { get; set; }
    [Range(1, 5, ErrorMessage = "กรุณาให้คะแนน 1 – 5 ดาว")]
    public int Rating { get; set; }
    [StringLength(500, ErrorMessage = "ความคิดเห็นยาวไม่เกิน 500 ตัวอักษร")]
    public string? Comment { get; set; }
    // แสดงผลเท่านั้น
    public string Summary { get; set; } = "";
}

public class AdminReviewsPageViewModel
{
    public int? Rating { get; set; }
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
    public int TotalCount { get; set; }
    public List<ReviewItemViewModel> Items { get; set; } = new();
}

/// <summary>รายละเอียดการจอง/ใบเสร็จ (ลูกค้าเห็นของตัวเอง, ผู้จัดการ/ฝ่ายบริการ/ฝ่ายบัญชีเห็นทุกการจอง)</summary>
/// <summary>Part 50: หน้าสถานะระบบของผู้จัดการ</summary>
public class SystemStatusViewModel
{
    public DateTime ServerTime { get; set; }
    public string Environment { get; set; } = "";
    public string AppVersion { get; set; } = "";
    public string Runtime { get; set; } = "";
    public string Os { get; set; } = "";
    public TimeSpan Uptime { get; set; }
    public bool DbOk { get; set; }
    public string? DbError { get; set; }
    public string DbVersion { get; set; } = "-";
    public string DbName { get; set; } = "-";
    public List<(string Name, long Rows, long Bytes)> Tables { get; set; } = new();
    public long? DiskFree { get; set; }
    public long? DiskTotal { get; set; }
    public int SlipFiles { get; set; }
    public long SlipBytes { get; set; }
    public DateTime? LastBackup { get; set; }
    public int DemoUsers { get; set; }
    // Part 51: อีเมล
    public bool MailEnabled { get; set; }
    public string? MailServer { get; set; }
    public string? MailFrom { get; set; }
    public Dictionary<string, int> OutboxCounts { get; set; } = new();
    public List<pameka_7033_l005.Models.EmailOutbox> Outbox { get; set; } = new();
    public int Waiting { get; set; }
}

public class BookingDetailViewModel
{
    public int BookingId { get; set; }
    public string Status { get; set; } = "";
    public string Customer { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public DateOnly CheckIn { get; set; }
    public DateOnly CheckOut { get; set; }
    public DateTime BookedAt { get; set; }
    public string? PromoName { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal Net { get; set; }
    public string? ConfirmedBy { get; set; }
    public List<(string RoomNo, string Type, decimal Price, int Nights)> Rooms { get; set; } = new();
    public List<(string RoomNo, string Service, int Qty, decimal Unit)> Services { get; set; } = new();
    public List<(int Id, string Type, string Method, string Status, decimal Amount, DateTime? PaidAt, string? By, bool HasSlip, string? Reference)> Payments { get; set; } = new();
    public int? Guests { get; set; }
    public DateTime? LastRejectedAt { get; set; }
    public List<(string Kind, string RoomNo, string Status, string? Who, DateTime? DoneAt)> Tasks { get; set; } = new();
    public (int Rating, string? Comment, DateTime At)? Review { get; set; }
    public PaymentFlags Flags { get; set; }
    public bool Reviewed => Review != null;
    public List<int> RoomIds { get; set; } = new();   // ใช้กับปุ่ม "จองห้องเดิมอีกครั้ง"
    // Part 50: เช็คอินออนไลน์ล่วงหน้า
    public string? ArrivalTime { get; set; }
    public string? GuestRequest { get; set; }
    /// <summary>แจ้งเวลามาถึง/คำขอพิเศษได้: การจองยังไม่เช็คอิน (รอชำระ/ยืนยันแล้ว)</summary>
    public bool CanPreArrival => Status is "PENDING" or "CONFIRMED";
}

// ======================================================================
// OperationsViewModels
// ======================================================================
// ---------- ฝ่ายบัญชี
public class PaymentRowViewModel
{
    public int PaymentId { get; set; }
    public int BookingId { get; set; }
    public string Customer { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Rooms { get; set; } = "";
    public DateOnly CheckIn { get; set; }
    public DateOnly CheckOut { get; set; }
    public decimal Amount { get; set; }
    public string Type { get; set; } = "";
    public string Method { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime? PaidAt { get; set; }
    public string? Handler { get; set; }
    public bool HasSlip { get; set; }
    public bool SlipIsPdf { get; set; }   // Part 48: สลิปรูปภาพเปิดดูเต็มจอในหน้าเดิม (GLightbox) · PDF เปิดแท็บใหม่
    public string? Reference { get; set; }
}

/// <summary>Mode: verify = รอตรวจสอบชำระเงิน, refund = คำขอคืนเงิน, history = ประวัติ</summary>
public class PaymentListViewModel
{
    public string Mode { get; set; } = "verify";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public List<PaymentRowViewModel> Rows { get; set; } = new();
}

// ---------- ฝ่ายบริการ
public class FrontDeskRowViewModel
{
    public int BookingId { get; set; }
    public string Customer { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Rooms { get; set; } = "";
    public DateOnly CheckIn { get; set; }
    public DateOnly CheckOut { get; set; }
    public string Status { get; set; } = "";
    public int UnsentServices { get; set; }
    public bool PendingRefund { get; set; }
    public bool CanCheckIn { get; set; }
    public bool CanCheckOut { get; set; }
    public bool CanSendDelivery { get; set; }
    public bool CanMarkNoShow { get; set; }
    public bool CanUndoNoShow { get; set; }
    public string? IdCheckPromo { get; set; }   // โปรที่ต้องตรวจบัตรตอนเช็คอิน (เช่น วันเกิด)
    public string? ArrivalTime { get; set; }    // Part 50: เวลาที่ลูกค้าแจ้งว่าจะมาถึง
    public string? GuestRequest { get; set; }   // Part 50: คำขอพิเศษ
}

public class RoomStatusRowViewModel
{
    public int RoomId { get; set; }
    public string RoomNo { get; set; } = "";
    public string TypeName { get; set; } = "";
    public string Status { get; set; } = "";
    public bool CanMakeAvailable { get; set; }
    public bool CanMaintenance { get; set; }
    public bool CanSendCleaning { get; set; }
}

// ---------- ฝ่ายส่งของ / ทำความสะอาด (หน้าจอเดียวกัน)
public class TaskRowViewModel
{
    public int TaskId { get; set; }
    public string RoomNo { get; set; } = "";
    public string Detail { get; set; } = "";
    public string Status { get; set; } = "";
    public string? Assignee { get; set; }
    public DateTime? DoneAt { get; set; }
    public bool CanTake { get; set; }
    public bool CanFinish { get; set; }
    // Part 53: ฝ่ายส่งของ
    public string Kind { get; set; } = "";          // S = บริการเสริม · R = ของใช้ที่ลูกค้าขอเพิ่ม
    public string? StatusText { get; set; }
    public string? Source { get; set; }
    public string? Guest { get; set; }
    public string? Note { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? PickedAt { get; set; }
    public bool Urgent { get; set; }
    public byte Missed { get; set; }
    public int? Minutes { get; set; }
    public bool Late { get; set; }
}

public class TaskListViewModel
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string EmptyText { get; set; } = "ไม่มีรายการ";
    public string TakeLabel { get; set; } = "รับงาน";
    public string FinishLabel { get; set; } = "เสร็จแล้ว";
    public bool ShowDone { get; set; }
    public bool Delivery { get; set; }   // Part 53: แสดงคอลัมน์เวลา/ปุ่มด่วน/ปุ่มคืนงาน (ใช้ทั้งฝ่ายส่งของและแม่บ้าน)
    public bool KindField { get; set; } = true;          // ส่ง kind (S/R) ไปกับฟอร์ม — เฉพาะฝ่ายส่งของ
    public string NotHomeLabel { get; set; } = "ไม่พบผู้เข้าพัก";
    public string NotHomeAction { get; set; } = "NotHome";
    public List<TaskRowViewModel> Rows { get; set; } = new();
}

// ======================================================================
// StaffViewModels
// ======================================================================
public class CreateStaffViewModel : PersonInfoViewModel
{
    [Required(ErrorMessage = "กรุณาเลือกบทบาท")]
    public string Role { get; set; } = "";
}

public class StaffListItemViewModel
{
    public int UserId { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string RoleName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public class StaffPageViewModel
{
    public List<StaffListItemViewModel> Staff { get; set; } = new();
    public CreateStaffViewModel Form { get; set; } = new();
}

// ======================================================================
// AdminViewModels
// ======================================================================
// ---------- ข้อมูลที่ผู้จัดการส่งมาจากฟอร์ม (ตรวจสอบด้วย DataAnnotations)
public class RoomEditViewModel
{
    public int RoomId { get; set; }
    [Required(ErrorMessage = "กรุณากรอกเลขห้อง"), StringLength(10, ErrorMessage = "เลขห้องยาวไม่เกิน 10 ตัวอักษร")]
    public string RoomNo { get; set; } = "";
    [Range(1, int.MaxValue, ErrorMessage = "กรุณาเลือกประเภทห้อง")]
    public int TypeId { get; set; }
    public int? ThemeId { get; set; }
}

public class RoomTypeEditViewModel
{
    public int TypeId { get; set; }
    [Required(ErrorMessage = "กรุณากรอกชื่อประเภทห้อง"), StringLength(50, ErrorMessage = "ชื่อประเภทห้องยาวไม่เกิน 50 ตัวอักษร")]
    public string TypeName { get; set; } = "";
    // ใช้ช่วงแบบ decimal: ถ้าเป็น Range(int, int) ค่าที่ใหญ่เกิน int จะทำให้ระบบล้ม (OverflowException) แทนที่จะขึ้นข้อความเตือน
    [Range(typeof(decimal), "1", "1000000", ErrorMessage = "ราคาต่อคืนต้องอยู่ระหว่าง 1 – 1,000,000 บาท")]
    public decimal BasePrice { get; set; }
    [Range(typeof(decimal), "0", "1000000", ErrorMessage = "ราคาคืนศุกร์–เสาร์ต้องอยู่ระหว่าง 0 – 1,000,000 บาท (เว้นว่าง = ใช้ราคาปกติ)")]
    public decimal? WeekendPrice { get; set; }
    [Range(1, 20, ErrorMessage = "จำนวนผู้เข้าพักต้องอยู่ระหว่าง 1 – 20 คน")]
    public sbyte MaxGuests { get; set; }
    public string? Description { get; set; }
    public string? Amenities { get; set; }
    public string? ImageUrl { get; set; }
}

/// <summary>รายละเอียดที่แสดงลูกค้าของประเภทห้อง (แยกฟอร์มจากราคา/ชื่อ เพราะมีอัปโหลดรูป)</summary>
public class RoomTypeDetailsViewModel
{
    public int TypeId { get; set; }
    [StringLength(2000, ErrorMessage = "คำอธิบายยาวไม่เกิน 2,000 ตัวอักษร")]
    public string? Description { get; set; }
    [StringLength(500, ErrorMessage = "สิ่งอำนวยความสะดวกยาวรวมไม่เกิน 500 ตัวอักษร")]
    public string? Amenities { get; set; }
    public IFormFile? Image { get; set; }
    public bool RemoveImage { get; set; }
}

public class ThemeEditViewModel
{
    public int ThemeId { get; set; }
    [Required(ErrorMessage = "กรุณากรอกชื่อธีม"), StringLength(30, ErrorMessage = "ชื่อธีมยาวไม่เกิน 30 ตัวอักษร")]
    public string ThemeName { get; set; } = "";
    [StringLength(255, ErrorMessage = "คำอธิบายธีมยาวไม่เกิน 255 ตัวอักษร")]
    public string? Description { get; set; }
}

public class ServiceEditViewModel
{
    public int ServiceId { get; set; }
    [Required(ErrorMessage = "กรุณากรอกชื่อบริการ"), StringLength(50, ErrorMessage = "ชื่อบริการยาวไม่เกิน 50 ตัวอักษร")]
    public string ServiceName { get; set; } = "";
    [Range(0.01, 100000, ErrorMessage = "ราคาบริการต้องอยู่ระหว่าง 0.01 – 100,000 บาท")]
    public decimal Price { get; set; }
    public bool IsActive { get; set; }
    public string? Category { get; set; }
    [StringLength(300, ErrorMessage = "คำอธิบายบริการยาวไม่เกิน 300 ตัวอักษร")]
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }       // แสดงผลเท่านั้น (ไม่รับจากฟอร์ม)
    public IFormFile? Image { get; set; }
    public bool RemoveImage { get; set; }
    public int TimesBooked { get; set; }        // แสดงผล
}

public class PromoEditViewModel : IValidatableObject
{
    public int PromoId { get; set; }
    [StringLength(100, ErrorMessage = "ชื่อโปรโมชั่นยาวไม่เกิน 100 ตัวอักษร")]
    public string? PromoName { get; set; }
    public string? PromoType { get; set; }                 // ใช้ตอนสร้างใหม่เท่านั้น (ประเภทเดิมแก้ไม่ได้)
    [Range(typeof(decimal), "0.01", "100", ErrorMessage = "ส่วนลดต้องอยู่ระหว่าง 0.01 – 100 เปอร์เซ็นต์")]
    public decimal DiscountPercent { get; set; }
    [Range(0, 365, ErrorMessage = "จองล่วงหน้าได้ 0 – 365 วัน")]
    public short? MinAdvanceDays { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool IsActive { get; set; }
    [StringLength(30)]
    public string? PromoCode { get; set; }
    [StringLength(300, ErrorMessage = "คำอธิบายยาวไม่เกิน 300 ตัวอักษร")]
    public string? Description { get; set; }
    [Range(1, 60, ErrorMessage = "จำนวนคืนขั้นต่ำต้องอยู่ระหว่าง 1 – 60 คืน")]
    public short? MinNights { get; set; }
    [Range(1, 100000, ErrorMessage = "จำนวนสิทธิ์ต้องอยู่ระหว่าง 1 – 100,000")]
    public int? MaxUses { get; set; }
    [Range(typeof(decimal), "1", "1000000", ErrorMessage = "ส่วนลดสูงสุดต้องอยู่ระหว่าง 1 – 1,000,000 บาท")]
    public decimal? MaxDiscount { get; set; }
    public bool NeedStaffConfirm { get; set; }
    [Range(1, 100, ErrorMessage = "จำนวนครั้งต่อลูกค้าต้องอยู่ระหว่าง 1 – 100")]
    public short? MaxPerUser { get; set; }
    public bool RecipientsOnly { get; set; }
    // Part 49
    public bool Claimable { get; set; }
    public DateOnly? ClaimFrom { get; set; }
    public DateOnly? ClaimUntil { get; set; }
    public bool Featured { get; set; }
    public string? AutoKey { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (StartDate.HasValue && EndDate.HasValue && EndDate < StartDate)
            yield return new ValidationResult("วันสิ้นสุดต้องไม่ก่อนวันเริ่มต้น");
        if (ClaimFrom is { } cf && ClaimUntil is { } cu && cu < cf)
            yield return new ValidationResult("วันปิดเก็บคูปองต้องไม่ก่อนวันเปิดเก็บ", new[] { nameof(ClaimUntil) });
        if (StartDate is { Year: < 2000 or > 2100 } || EndDate is { Year: < 2000 or > 2100 })
            yield return new ValidationResult("วันที่ของโปรโมชั่นไม่ถูกต้อง");
        if (PromoCode != null && !System.Text.RegularExpressions.Regex.IsMatch(PromoCode.Trim().ToUpperInvariant(), "^[A-Z0-9_-]{3,30}$"))
            yield return new ValidationResult("รหัสโปรโมชั่นใช้ได้เฉพาะ A–Z, 0–9, - และ _ ยาว 3–30 ตัว");
    }
}

public class PromoAdminRowViewModel : PromoEditViewModel
{
    public int Used { get; set; }               // การจองที่ใช้โปรนี้ (ไม่นับที่ยกเลิก)
    public decimal DiscountGiven { get; set; }  // ส่วนลดที่ให้ไปแล้วรวม
    public decimal Revenue { get; set; }        // ยอดสุทธิของการจองที่ใช้โปรนี้
    public bool HasBookings { get; set; }       // มีการจองอ้างถึง (รวมที่ยกเลิก) → ลบไม่ได้
    public int Recipients { get; set; }         // จำนวนลูกค้าที่ได้รับรหัส
}

// ---------- ข้อมูลที่แสดงในหน้าผู้จัดการ
public class RoomAdminRowViewModel
{
    public int RoomId { get; set; }
    public string RoomNo { get; set; } = "";
    public int TypeId { get; set; }
    public int? ThemeId { get; set; }
    public string Status { get; set; } = "";
}

public class RoomsAdminPageViewModel
{
    public List<RoomAdminRowViewModel> Rooms { get; set; } = new();
    public List<RoomTypeEditViewModel> Types { get; set; } = new();
    public List<ThemeEditViewModel> Themes { get; set; } = new();
    public List<pameka_7033_l005.Models.RateSeason> Seasons { get; set; } = new();
}

/// <summary>ฟอร์มช่วงราคาพิเศษ (Part 43) — ตรวจค่าที่ controller</summary>
public class SeasonEditViewModel
{
    public int SeasonId { get; set; }
    public string? Name { get; set; }
    public DateOnly? Start { get; set; }
    public DateOnly? End { get; set; }
    public decimal? Percent { get; set; }
}


public class AdminBookingRowViewModel
{
    public int BookingId { get; set; }
    public string Customer { get; set; } = "";
    public string Rooms { get; set; } = "";
    public DateOnly CheckIn { get; set; }
    public DateOnly CheckOut { get; set; }
    public DateTime BookedAt { get; set; }
    public decimal Net { get; set; }
    public string Status { get; set; } = "";
    public string PaymentLabel { get; set; } = "";
}

public class AdminBookingsPageViewModel
{
    public string? Status { get; set; }
    public string? Q { get; set; }
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
    public int TotalCount { get; set; }
    public List<AdminBookingRowViewModel> Rows { get; set; } = new();
    public Dictionary<string, int> StatusCounts { get; set; } = new();   // Part 42: ชิปกรองสถานะพร้อมจำนวน
}

public class DashboardViewModel
{
    public int Bookings { get; set; }
    public int Rooms { get; set; }
    public int Customers { get; set; }
    public decimal Revenue { get; set; }
    public int PaymentsToVerify { get; set; }
    public int RefundsPending { get; set; }
    public int OpenTasks { get; set; }
    public int ArrivalsToday { get; set; }
    public int DeparturesToday { get; set; }
    public int Reviews { get; set; }
    public List<(string Status, int Count)> RoomsByStatus { get; set; } = new();

    // ---- แดชบอร์ดแบบละเอียด (Part 22) — ทุกค่ามาจากฐานข้อมูลจริง ----
    /// <summary>รายรับ/การจองใหม่ 30 วันล่าสุด เทียบ 30 วันก่อนหน้า</summary>
    public decimal Revenue30 { get; set; }
    public decimal RevenuePrev30 { get; set; }
    public int NewBookings30 { get; set; }
    public int NewBookingsPrev30 { get; set; }
    /// <summary>รายรับรายวัน 30 วัน (วันเก่าสุดก่อน, ครบทุกวันแม้เป็น 0)</summary>
    public List<(DateOnly Date, decimal Amount)> DailyRevenue { get; set; } = new();
    public List<(DateOnly Date, int Count)> DailyBookings { get; set; } = new();
    /// <summary>ห้องที่ขายแล้วคืนนี้ / ทั้งหมด</summary>
    public int OccupiedTonight { get; set; }
    public decimal Adr30 { get; set; }
    public decimal OccupancyPercent30 { get; set; }
    /// <summary>คาดการณ์ 14 คืนข้างหน้า: ห้องที่ยืนยันแล้ว และห้องที่จองไว้แต่ยังรอชำระ</summary>
    public List<(DateOnly Date, int Sold, int Pending)> Forecast { get; set; } = new();
    public int PendingBookings { get; set; }
    public int OpenHousekeeping { get; set; }
    public int OpenDelivery { get; set; }
    public List<DashBookingRow> Arrivals { get; set; } = new();
    public List<DashBookingRow> Departures { get; set; } = new();
    public List<DashBookingRow> Recent { get; set; } = new();
    public List<(string Name, int Nights, decimal Revenue)> TopTypes { get; set; } = new();
    public decimal AvgRating { get; set; }
    public int[] RatingCounts { get; set; } = new int[6];   // index 1..5

    public static decimal? Change(decimal now, decimal prev) => prev == 0 ? null : Math.Round((now - prev) / prev * 100m, 1);
}

public record DashBookingRow(int BookingId, string Customer, string Rooms, DateOnly CheckIn, DateOnly CheckOut, int Guests, decimal Net, string Status, DateTime BookedAt);

public class ReportViewModel
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public string? Error { get; set; }
    public decimal Revenue { get; set; }
    public decimal Refunded { get; set; }
    public decimal Expenses { get; set; }
    public decimal Profit => Revenue - Expenses;
    public List<(string Category, decimal Amount)> ExpenseByCategory { get; set; } = new();
    public int NewBookings { get; set; }
    public int RoomNights { get; set; }
    public List<(DateOnly Date, decimal Amount)> Daily { get; set; } = new();
    public List<(string Status, int Count)> StatusCounts { get; set; } = new();
    public List<(string Name, int Qty, decimal Total)> Services { get; set; } = new();

    // อัตราการเข้าพัก: นับห้องที่มีผู้จองยืนยันแล้วในแต่ละคืน (ไม่รวมรอชำระ/ยกเลิก)
    public int TotalRooms { get; set; }
    public List<(DateOnly Date, int Occupied)> Occupancy { get; set; } = new();
    public decimal OccupancyPercent { get; set; }   // เฉลี่ยทั้งช่วง
    public decimal Adr { get; set; }                // ราคาห้องเฉลี่ยต่อคืนที่ขายได้ (ก่อนส่วนลด)
    public decimal RevPar { get; set; }             // รายได้ค่าห้องต่อห้องที่มีทั้งหมดต่อคืน (ก่อนส่วนลด)
}

public class LoginHistoryRowViewModel
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public DateTime LoggedInAtUtc { get; set; }
}

public class LoginHistoryPageViewModel
{
    public List<LoginHistoryRowViewModel> Items { get; set; } = new();
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
    public int TotalCount { get; set; }
}

// ======================================================================
// ปฏิทินห้องว่าง
// ======================================================================
public class AvailabilityRowViewModel
{
    public int RoomId { get; set; }
    public string RoomNo { get; set; } = "";
    public string TypeName { get; set; } = "";
    public decimal Price { get; set; }
    public bool Maintenance { get; set; }
    public bool[] Busy { get; set; } = Array.Empty<bool>();   // index 0 = วันที่ 1 ของเดือน
}

public class AvailabilityPageViewModel
{
    public DateOnly Month { get; set; }          // วันที่ 1 ของเดือนที่แสดง
    public DateOnly Today { get; set; }
    public int Days { get; set; }
    public bool CanPrev { get; set; }
    public bool CanNext { get; set; }
    public List<AvailabilityRowViewModel> Rows { get; set; } = new();
    public int FreeOn(int dayIndex) => Rows.Count(r => !r.Maintenance && !r.Busy[dayIndex]);
}

// ======================================================================
// หน้ารายละเอียดห้อง
// ======================================================================
public class RoomDetailsViewModel
{
    public RoomCardViewModel Room { get; set; } = new();
    public bool Maintenance { get; set; }
    public DateOnly Today { get; set; }
    /// <summary>วันที่ที่ส่งต่อมาจากหน้าค้นหา (ถ้าไม่มีหรือไม่ถูกต้อง ใช้วันนี้-พรุ่งนี้)</summary>
    public DateOnly CheckIn { get; set; }
    public DateOnly CheckOut { get; set; }
    public int Guests { get; set; } = 1;
    public int ReviewCount { get; set; }
    public double Average { get; set; }
    public List<ReviewItemViewModel> Reviews { get; set; } = new();
    public List<(DateOnly Month, bool[] Busy)> Months { get; set; } = new();
    public List<pameka_7033_l005.Helpers.RatePlan.Season> Seasons { get; set; } = new();
}

// ======================================================================
// หน้าเช็คอิน/เช็คเอาท์: ค้นหา + แท็บกรอง
// ======================================================================
public class FrontDeskPageViewModel
{
    public string View { get; set; } = "all";     // all / in (ต้องเช็คอินวันนี้) / out (ต้องเช็คเอาท์)
    public string? Q { get; set; }
    public int CountAll { get; set; }
    public int CountIn { get; set; }
    public int CountOut { get; set; }
    public int CountLate { get; set; }    // ยืนยันแล้ว เลยวันเข้าพัก ยังไม่เช็คอิน
    public List<FrontDeskRowViewModel> Rows { get; set; } = new();
}

// ======================================================================
// ExpenseViewModels — รายจ่าย (ฝ่ายบัญชี)
// ======================================================================
public class ExpenseFormViewModel
{
    public static readonly string[] Categories = { "เงินเดือน/ค่าแรง", "ค่าน้ำ ค่าไฟ", "ซ่อมบำรุง", "อาหาร/วัตถุดิบ", "ของใช้ในห้องพัก", "การตลาด", "อื่น ๆ" };

    [Required(ErrorMessage = "กรุณาเลือกหมวด")]
    public string Category { get; set; } = "";
    [Range(0.01, 10000000, ErrorMessage = "จำนวนเงินต้องมากกว่า 0 และไม่เกิน 10,000,000 บาท")]
    public decimal Amount { get; set; }
    [Required(ErrorMessage = "กรุณาเลือกวันที่จ่าย")]
    public DateOnly? SpentOn { get; set; }
    [StringLength(255, ErrorMessage = "บันทึกยาวไม่เกิน 255 ตัวอักษร")]
    public string? Note { get; set; }
}

public class ExpensePageViewModel
{
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Revenue { get; set; }
    public decimal Total { get; set; }
    public decimal Profit => Revenue - Total;
    public List<(string Category, decimal Amount)> ByCategory { get; set; } = new();
    public List<(int Id, DateOnly SpentOn, string Category, decimal Amount, string? Note, string? By)> Rows { get; set; } = new();
}

// ======================================================================
// AuditPageViewModel (บันทึกกิจกรรม)
// ======================================================================
public class AuditPageViewModel
{
    public string? Q { get; set; }
    public string? Group { get; set; }          // null=ทั้งหมด, staff, customer, failed, login
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
    public int TotalCount { get; set; }
    public List<pameka_7033_l005.Models.AuditLog> Rows { get; set; } = new();
}

// ======================================================================
// ติดต่อโรงแรม (Part 30)
// ======================================================================
public class ContactFormViewModel
{
    [Required(ErrorMessage = "กรุณากรอกชื่อ"), StringLength(100, ErrorMessage = "ชื่อยาวไม่เกิน 100 ตัวอักษร")]
    public string Name { get; set; } = "";
    [Required(ErrorMessage = "กรุณากรอกอีเมลสำหรับติดต่อกลับ"), EmailAddress(ErrorMessage = "รูปแบบอีเมลไม่ถูกต้อง"), StringLength(150)]
    public string Email { get; set; } = "";
    [RegularExpression(@"^\+?[0-9]{9,15}$", ErrorMessage = "เบอร์โทรใช้ตัวเลข 9–15 หลัก")]
    public string? Phone { get; set; }
    [Required]
    public string Topic { get; set; } = "GENERAL";
    [Required(ErrorMessage = "กรุณาพิมพ์ข้อความ"), StringLength(2000, MinimumLength = 10, ErrorMessage = "ข้อความต้องยาว 10–2,000 ตัวอักษร")]
    public string Message { get; set; } = "";
    /// <summary>ช่องดักบอท (ซ่อนจากผู้ใช้) — ถ้ามีค่า ไม่บันทึก</summary>
    public string? Website { get; set; }
}

public class AboutPageViewModel
{
    public HotelInfo Hotel { get; set; } = null!;
    public ContactFormViewModel Form { get; set; } = new();
}

public class AdminMessagesPageViewModel
{
    public string Status { get; set; } = "NEW";
    public string? Q { get; set; }
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
    public int TotalCount { get; set; }
    public int NewCount { get; set; }
    public List<pameka_7033_l005.Models.ContactMessage> Items { get; set; } = new();
    /// <summary>อีเมลผู้ส่งที่ตรงกับบัญชีในระบบ → user_id (ไว้ทำลิงก์ไปหน้าแก้ไขผู้ใช้/ออกรหัสใหม่)</summary>
    public Dictionary<string, int> Accounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

// ---------------- Part 44: พื้นที่สมาชิก
public class MyMessageViewModel
{
    public long Id { get; set; }
    public string Topic { get; set; } = "";
    public string Message { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string? Reply { get; set; }
    public DateTime? RepliedAt { get; set; }
}

public class MyReviewItem
{
    public int BookingId { get; set; }
    public int Rating { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateOnly CheckIn { get; set; }
    public DateOnly CheckOut { get; set; }
}

public class MyReviewsViewModel
{
    public List<MyReviewItem> Written { get; set; } = new();
    public List<MyBookingItemViewModel> ToReview { get; set; } = new();
}

/// <summary>Part 51: ลืมรหัสผ่าน — กรอกอีเมลเพื่อรับลิงก์ตั้งรหัสใหม่</summary>
public class ForgotPasswordViewModel
{
    [Required(ErrorMessage = "กรุณากรอกอีเมล")]
    [EmailAddress(ErrorMessage = "รูปแบบอีเมลไม่ถูกต้อง")]
    [StringLength(100)]
    public string Email { get; set; } = "";
    public bool Sent { get; set; }
    public string? Error { get; set; }
}

/// <summary>Part 51: ตั้งรหัสผ่านใหม่จากลิงก์ในอีเมล</summary>
public class ResetPasswordViewModel
{
    public string Token { get; set; } = "";
    public string? Email { get; set; }

    [Required(ErrorMessage = "กรุณากรอกรหัสผ่านใหม่")]
    [StringLength(100, ErrorMessage = "รหัสผ่านยาวไม่เกิน 100 ตัวอักษร"), PasswordPolicy]
    public string NewPassword { get; set; } = "";

    [Required(ErrorMessage = "กรุณายืนยันรหัสผ่านใหม่")]
    [Compare(nameof(NewPassword), ErrorMessage = "ยืนยันรหัสผ่านใหม่ไม่ตรงกัน")]
    public string ConfirmPassword { get; set; } = "";
}

/// <summary>Part 51: รายการรอห้องว่างของลูกค้า (หน้าสมาชิก)</summary>
public class WaitlistItemViewModel
{
    public int WaitId { get; set; }
    public string What { get; set; } = "";
    public DateOnly CheckIn { get; set; }
    public DateOnly CheckOut { get; set; }
    public int Guests { get; set; }
    public string Status { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

// ---------------- Part 52: กิจกรรมของโรงแรม
public class ActivityCardViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public int DurationMin { get; set; }
    public int Capacity { get; set; }
    public List<string> Times { get; set; } = new();
    public string Days { get; set; } = "";
    public string? Location { get; set; }
    /// <summary>รอบใน 7 วันข้างหน้า (วันที่, เวลา, ที่นั่งเหลือ)</summary>
    public List<(DateOnly Date, string Time, int Left)> Sessions { get; set; } = new();
    /// <summary>วันที่ลูกค้าจองได้ (อยู่ในช่วงเข้าพัก + เปิดวันนั้น)</summary>
    public List<DateOnly> BookableDates { get; set; } = new();
}

public class ActivityBookingRowViewModel
{
    public int AbId { get; set; }
    public int ActivityId { get; set; }
    public string ActivityName { get; set; } = "";
    public string? Location { get; set; }
    public DateOnly Date { get; set; }
    public string Time { get; set; } = "";
    public int People { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = "";
    public bool CanCancel { get; set; }
    public string GuestName { get; set; } = "";
    public string? Phone { get; set; }
    public string? Rooms { get; set; }
}

public class ActivitiesPageViewModel
{
    public DateOnly Today { get; set; }
    public bool IsCustomer { get; set; }
    public bool HasStay { get; set; }
    public string? Category { get; set; }
    public List<ActivityCardViewModel> Cards { get; set; } = new();
    public List<ActivityBookingRowViewModel> Mine { get; set; } = new();
}

public class ActivityEditViewModel
{
    public int ActivityId { get; set; }
    [Required(ErrorMessage = "กรุณากรอกชื่อกิจกรรม"), StringLength(100, ErrorMessage = "ชื่อกิจกรรมยาวไม่เกิน 100 ตัวอักษร")]
    public string Name { get; set; } = "";
    [Required] public string Category { get; set; } = "CULTURE";
    [StringLength(500, ErrorMessage = "คำอธิบายยาวไม่เกิน 500 ตัวอักษร")]
    public string? Description { get; set; }
    [Range(0, 100000, ErrorMessage = "ราคา 0–100,000 บาท")]
    public decimal Price { get; set; }
    [Range(15, 720, ErrorMessage = "ระยะเวลา 15–720 นาที")]
    public int DurationMin { get; set; } = 60;
    [Range(1, 200, ErrorMessage = "ที่นั่งต่อรอบ 1–200")]
    public int Capacity { get; set; } = 10;
    [Required(ErrorMessage = "กรุณาระบุรอบเวลา เช่น 09:00,14:00"), StringLength(60)]
    public string Times { get; set; } = "09:00";
    public List<int> Days { get; set; } = new();
    [StringLength(100)]
    public string? Location { get; set; }
    public bool IsActive { get; set; } = true;
    public int Upcoming { get; set; }
    public int TotalBooked { get; set; }
}

public class ActivityRosterViewModel
{
    public DateOnly Date { get; set; }
    public List<(int ActivityId, string Name, string Time, string? Location, int Capacity, List<ActivityBookingRowViewModel> People)> Sessions { get; set; } = new();
}
