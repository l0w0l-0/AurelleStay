using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;   
using Microsoft.EntityFrameworkCore;
using pameka_7033_l005.Helpers;
using pameka_7033_l005.Models;

// Windows ที่ตั้งภาษาไทยใช้ปฏิทินพุทธ (ปี 2569) เป็นค่าเริ่มต้นของโปรแกรม ทำให้วันที่ในฟอร์ม/ลิงก์/JSON กลายเป็น "2569-11-14"
// และอ่านวันที่ที่ส่งมาผิด 543 ปี → จองไม่ได้, ปฏิทินเพี้ยน. จึงล็อกวัฒนธรรมของระบบเป็นแบบกลาง (ค.ศ., ตัวเลขอารบิก) ทุกเครื่อง
// ข้อความที่ต้องการแสดง พ.ศ./ชื่อเดือนไทย ระบุ CultureInfo("th-TH") เองในหน้านั้น (เช่น หัวปฏิทิน, วันที่บนแดชบอร์ด)
var appCulture = System.Globalization.CultureInfo.InvariantCulture;
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = appCulture;
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = appCulture;
System.Globalization.CultureInfo.CurrentCulture = appCulture;
System.Globalization.CultureInfo.CurrentUICulture = appCulture;

var builder = WebApplication.CreateBuilder(args);

// Part 55: dotnet run คำสั่งเดียว — เปิด Docker Desktop + ฐานข้อมูลใน Docker + อัปเดตเว็บใน Docker ให้อัตโนมัติ (เฉพาะ Development)
DevDocker.Prepare(builder);

builder.Services.AddAppServices(builder.Configuration, builder.Environment);


var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// ตรวจว่าต่อ MySQL ได้ก่อนเริ่มระบบ: ถ้าต่อไม่ได้ แสดงสาเหตุและวิธีแก้เป็นภาษาไทยแทนข้อความ error ยาว ๆ
ServerVersion serverVersion;
try
{
    serverVersion = ServerVersion.AutoDetect(connectionString);
}
catch (MySqlConnector.MySqlException ex)
{
    DbSetup.PrintConnectionHelp(connectionString, ex);
    Environment.ExitCode = 1;
    return;
}

builder.Services.AddDbContext<Csi402dbContext>(options => options.UseMySql(connectionString, serverVersion,
    // โหลดหลายคอลเลกชันในคำสั่งเดียวจะคูณแถวกัน (cartesian) — แยกเป็นหลายคำสั่งให้เร็วและไม่บวม
    o => o.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));


var app = builder.Build();

// เตรียมฐานข้อมูล: อัปเกรดคอลัมน์ที่ขาด + บทบาท + ห้องตัวอย่างถ้ายังว่าง (ดู DbSetup ใน Helpers/AppHelpers.cs)
await app.PrepareDatabaseAsync();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseErrorPages();
app.UseSecurityHeaders();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets().AllowAnonymous();
app.MapHealthChecks("/health").AllowAnonymous();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

// URL ที่ไม่มีอยู่ → 404 (หน้าแจ้งเตือนของระบบ) แทนการเด้งไปหน้าเข้าสู่ระบบสำหรับผู้ที่ยังไม่ล็อกอิน
app.MapFallback("{**path}", ctx => { ctx.Response.StatusCode = StatusCodes.Status404NotFound; return Task.CompletedTask; }).AllowAnonymous();

app.Run();

