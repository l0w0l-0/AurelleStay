<p align="center"><img src="docs/logo.png" alt="AurelleStay" width="360"></p>

<h3 align="center">ระบบเว็บไซต์จองห้องพักโรงแรม — ASP.NET Core MVC + MySQL</h3>

<p align="center">
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10-512BD4">
  <img alt="ASP.NET Core MVC" src="https://img.shields.io/badge/ASP.NET%20Core-MVC-5C2D91">
  <img alt="MySQL 8" src="https://img.shields.io/badge/MySQL-8-4479A1">
  <img alt="EF Core 9" src="https://img.shields.io/badge/EF%20Core-9-6A1B9A">
  <img alt="Docker" src="https://img.shields.io/badge/Docker-ready-2496ED">
</p>

AurelleStay เป็นเว็บไซต์จองห้องพักโรงแรมแบบครบวงจร ลูกค้าค้นหาห้องว่าง ดูปฏิทินวันว่างพร้อมราคารายคืน จองหลายห้องพร้อมบริการเสริม รับส่วนลดและคูปองเทศกาล ชำระเงินด้วย QR พร้อมเพย์แล้วแนบสลิป ไปจนถึงเช็คอินออนไลน์และรีวิวหลังเข้าพัก
หลังบ้านแบ่งงานตามฝ่ายของโรงแรมจริง 5 ฝ่าย และทุกบทบาทถูกบังคับสิทธิ์ที่ฝั่งเซิร์ฟเวอร์

> โปรเจกต์รายวิชา CSI402 การเชื่อมต่อโปรแกรมส่วนหน้ากับโปรแกรมส่วนหลัง

<p align="center"><img src="docs/screenshots/home.jpg" alt="หน้าแรก" width="85%"></p>

## ฟีเจอร์หลัก

**ลูกค้า**
- ค้นหาห้องตามวันที่ จำนวนคน ราคา และธีม · ปฏิทินวันว่างรายห้องพร้อมราคารายคืน
- จองได้หลายห้องพร้อมบริการเสริม/เตียงเสริม · ระบบกันจองห้องซ้อน
- ราคาตามฤดูกาลและเทศกาลไทยอัตโนมัติ (สงกรานต์ ปีใหม่ ลอยกระทง ฯลฯ)
- ส่วนลดอัตโนมัติ (สมาชิก, Early Bird, วันเกิด, พักยาว) และ **คูปองกดเก็บ** — ระบบเลือกส่วนลดที่คุ้มที่สุดให้
- ชำระเงินด้วย **QR พร้อมเพย์ระบุยอด** (มาตรฐาน EMVCo) หรือโอนเข้าบัญชี แล้วแนบสลิป
- เช็คอินออนไลน์ (แจ้งเวลามาถึง/คำขอพิเศษ) · เพิ่มลงปฏิทิน (.ics / Google Calendar)
- ใบยืนยันการจอง/ใบเสร็จ · ยกเลิก/ขอคืนเงิน · รีวิว · การแจ้งเตือนแบบสด

**หลังบ้าน**
| บทบาท | ทำอะไรได้ |
|---|---|
| ฝ่ายบัญชี | ตรวจสลิป ยืนยัน/ปฏิเสธการชำระเงิน อนุมัติคืนเงิน บันทึกรายจ่าย |
| ฝ่ายบริการ (ฟรอนต์) | รายการเข้าพัก/ออกวันนี้ เช็คอิน เช็คเอาท์ ไม่มาเข้าพัก จองหน้าเคาน์เตอร์ |
| ฝ่ายส่งของ | งานนำส่งเตียงเสริมและบริการถึงห้อง |
| ฝ่ายทำความสะอาด | งานทำความสะอาดห้องหลังเช็คเอาท์ |
| ผู้จัดการ | Dashboard, ห้องและราคา, บริการ, โปรโมชั่น, พนักงาน, รายงาน + CSV, บันทึกกิจกรรม, สำรองข้อมูล, ข้อมูลตัวอย่างสำหรับสาธิต |

เข้าสู่ระบบด้วยอีเมล + รหัสผ่านอย่างเดียว ระบบพาไปหน้าของบทบาทบัญชีนั้นเอง

## ภาพหน้าจอ

| รายละเอียดห้อง + ปฏิทินวันว่าง | จองห้อง |
|---|---|
| <img src="docs/screenshots/roomdetail.jpg" width="100%"> | <img src="docs/screenshots/create.jpg" width="100%"> |
| **ชำระเงิน (QR พร้อมเพย์)** | **โปรโมชั่นและคูปอง** |
| <img src="docs/screenshots/pay.jpg" width="100%"> | <img src="docs/screenshots/promotions.jpg" width="100%"> |
| **Dashboard ผู้จัดการ** | **ฝ่ายบริการ (ฟรอนต์)** |
| <img src="docs/screenshots/dashboard.jpg" width="100%"> | <img src="docs/screenshots/frontdesk.jpg" width="100%"> |

## เริ่มใช้งาน — รันทีเดียวจบ (Docker — แนะนำ)

ต้องมี [Docker Desktop](https://www.docker.com/products/docker-desktop/) เท่านั้น (เปิดไว้ก่อน)

**Windows** — ดับเบิลคลิก `run.bat` หรือพิมพ์
```powershell
.\run.bat
```
**macOS / Linux**
```bash
./run.sh
```

สคริปต์ทำให้ครบในคำสั่งเดียว: สร้าง `.env` (รหัสผ่านสุ่ม) → build เว็บ + MySQL 8 → สร้างฐานข้อมูลและห้องตัวอย่าง → **สร้างบัญชีผู้จัดการคนแรกให้อัตโนมัติ** → รอจนพร้อม → แสดงอีเมล/รหัสผู้จัดการ → เปิดเบราว์เซอร์ที่ http://localhost:8088

- บัญชีผู้จัดการอยู่ใน `.env` (`ADMIN_EMAIL`, `ADMIN_PASSWORD`) ใช้ตอนเปิดครั้งแรกเท่านั้น — เข้าแล้วเปลี่ยนรหัสได้ที่หน้าตั้งค่า
- (สาธิต) เมนู **สถานะระบบ / สำรองข้อมูล** → **สร้างข้อมูลตัวอย่าง** ได้ลูกค้า 30 คนพร้อมการจอง การชำระเงิน และรีวิว (`guest01`–`guest30@demo.aurellestay.local` รหัส `Demo@2026`) ลบออกได้ด้วยปุ่มเดียว
- เปิดจากมือถือ/เครื่องอื่นในวง LAN ได้ที่ `http://IP-ของเครื่อง:8088`

**อีเมล (ไม่บังคับ):** ใส่ใน `.env` แล้วรันใหม่ → ลูกค้ากด "ลืมรหัสผ่าน" รีเซ็ตเองได้ + การแจ้งเตือนส่งเป็นอีเมล + ตอบกลับข้อความติดต่อทางอีเมล
```
SMTP_HOST=smtp.gmail.com
SMTP_PORT=587
SMTP_USER=อีเมล-gmail-ของโรงแรม
SMTP_PASSWORD=App Password 16 ตัว   (สร้างที่ https://myaccount.google.com/apppasswords ต้องเปิด 2-Step Verification ก่อน)
PUBLIC_URL=http://localhost:8088     (หรือ IP/โดเมนที่ลูกค้าเปิดเว็บ ใช้ทำลิงก์ในอีเมล)
```
ทดสอบได้ที่เมนู **สถานะระบบ** → **ส่งอีเมลทดสอบถึงฉัน** · ไม่ตั้งค่า = ระบบทำงานแบบเดิมครบทุกอย่าง

คำสั่งอื่น: `.\docker.ps1 status | logs | backup | down | admin อีเมล` · `./run.sh status | logs | down`
ตั้งค่ารับเงินพร้อมเพย์ใน `.env`: `PROMPTPAY_ID`, `PAY_ACCOUNT_NAME`, `PAY_BANK_NAME`, `PAY_ACCOUNT_NO` (ดูตัวอย่างใน `.env.example`)

## เริ่มใช้งาน (ไม่ใช้ Docker)

ต้องมี .NET 10 SDK และ MySQL 8

```powershell
mysql -u root -p < Database/aurellestay_mysql.sql            # สร้างฐานข้อมูล csi402db (รันซ้ำได้ ไม่ลบข้อมูลเดิม)
mysql -u root -p < Database/aurellestay_sample_catalog.sql   # (ไม่บังคับ) ห้อง/ราคา/บริการตัวอย่าง
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "server=localhost;port=3306;database=csi402db;user=root;password=รหัสของคุณ"
dotnet run --launch-profile http                              # http://localhost:5290
```
หรือรัน `setup.ps1` ซึ่งทำทุกขั้นให้อัตโนมัติ — รหัสผ่านไม่ถูกเก็บในไฟล์โปรเจกต์ (`appsettings.json` เป็น `CHANGE_ME`)

## โครงสร้างโปรเจกต์

```
Controllers/   AccountControllers.cs   เข้าสู่ระบบ สมัคร ผู้ใช้ ตั้งค่า
               CustomerControllers.cs  หน้าแรก ห้องพัก จอง ชำระเงิน โปรโมชั่น รีวิว
               StaffControllers.cs     ฝ่ายบัญชี ฝ่ายบริการ ส่งของ ทำความสะอาด
               AdminControllers.cs     ผู้จัดการทุกหน้า
Helpers/       AppHelpers.cs           ความปลอดภัย บทบาท รหัสผ่าน พร้อมเพย์ บันทึกกิจกรรม
               BookingHelpers.cs       ราคา โปรโมชั่น ห้องว่าง สร้างการจอง กฎสถานะ ข้อมูลตัวอย่าง
Models/        Entities.cs, Csi402dbContext.cs   21 ตาราง (EF Core)
ViewModels/    ViewModels.cs           ฟอร์มและข้อมูลแสดงผลทุกหน้า
Views/         Razor views แยกตาม controller
Database/      สคริปต์สร้าง/อัปเกรดฐานข้อมูล MySQL
wwwroot/       CSS ธีมม่วง-ทอง, JS, ฟอนต์ และไลบรารี (ไม่โหลดจากอินเทอร์เน็ต)
```

## ฐานข้อมูล

MySQL 8 ฐานข้อมูล `csi402db` 21 ตาราง ศูนย์กลางคือ `users` และ `bookings`

<p align="center"><img src="docs/er-diagram.png" alt="ER Diagram" width="80%"></p>

## ความปลอดภัย
- สิทธิ์ตามบทบาทบังคับด้วย `[Authorize(Roles=…)]` ทุก controller และตรวจคุกกี้ซ้ำกับฐานข้อมูลทุกคำขอ (เปลี่ยนรหัส/บทบาท = เซสชันเดิมใช้ไม่ได้ทันที)
- รหัสผ่าน PBKDF2 · Anti-Forgery ทุกฟอร์ม · จำกัดการเดารหัสผ่าน · ตรวจชนิด/ขนาดไฟล์สลิป · หัว HTTP ความปลอดภัย
- ลูกค้าเห็นเฉพาะข้อมูลของตัวเอง · ผู้จัดการคืนเงินเองไม่ได้ (ต้องผ่านฝ่ายบัญชี) · บันทึกกิจกรรมสำคัญทุกอย่าง

## การทดสอบ
- ชุดทดสอบผ่าน HTTP จริง **605/605** (สมัคร → จอง → ชำระเงิน → ตรวจสลิป → เช็คอิน/เอาท์ → คืนเงิน, สิทธิ์ทุกหน้า × ทุกบทบาท, กันจองซ้อนพร้อมกัน, สำรอง-กู้คืนข้อมูล)
- ทดสอบในเบราว์เซอร์จริง: ไต่ทุกลิงก์ทุกบทบาท (ไม่มี error), ทุกปุ่มทำงาน, จอมือถือ 360px, ตรวจ accessibility ด้วย axe-core (0 ข้อผิดพลาด)

## เอกสารเพิ่มเติม
- [CHANGELOG.md](CHANGELOG.md) — รายละเอียดการพัฒนาทุกส่วน ตารางสิทธิ์เต็ม การติดตั้งบนเซิร์ฟเวอร์จริง และการสำรองข้อมูล

## ไลบรารีโอเพนซอร์สที่ใช้
Bootstrap, Chart.js, Notyf, GLightbox, Tom Select, SweetAlert2, qrcode-generator, instant.page และฟอนต์ Anuphan / Cormorant Garamond / Trirong (OFL) — เก็บไว้ใน `wwwroot/lib` และ `wwwroot/fonts` พร้อมไฟล์สัญญาอนุญาต
