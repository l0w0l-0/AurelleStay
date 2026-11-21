# =====================================================================
# AurelleStay — ตั้งค่าเครื่องครั้งแรก (รันซ้ำได้ ไม่ลบข้อมูลเดิม)
# วิธีใช้: เปิด PowerShell ในโฟลเดอร์โปรเจกต์ แล้วพิมพ์
#   powershell -ExecutionPolicy Bypass -File .\setup.ps1
# ใส่ค่าล่วงหน้าได้ (ไม่ต้องตอบคำถาม) เช่น
#   powershell -ExecutionPolicy Bypass -File .\setup.ps1 -DbUser root -AdminEmail you@mail.com
# สิ่งที่ทำ: ตรวจ .NET SDK 10 และ MySQL → สร้าง/อัปเกรดฐานข้อมูล → เก็บรหัสผ่าน (user-secrets ไม่เขียนลงไฟล์)
#           → (ถ้าต้องการ) ตั้งผู้จัดการคนแรก → build → เปิดระบบ
# ไฟล์นี้บันทึกเป็น UTF-8 มี BOM เพื่อให้ Windows PowerShell 5.1 อ่านภาษาไทยถูก — ถ้าแก้ไฟล์ ให้คงการเข้ารหัสนี้ไว้
# =====================================================================
param(
    [string]$DbHost, [string]$DbPort, [string]$DbUser, [string]$DbPassword,
    [string]$AdminEmail,
    [switch]$SkipSample,   # ไม่ใส่ห้อง/บริการตัวอย่าง
    [switch]$NoRun         # ตั้งค่า + build เสร็จแล้วไม่ต้องเปิดระบบ
)
$ErrorActionPreference = 'Stop'
Set-Location -Path $PSScriptRoot
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }
$unattended = $PSBoundParameters.Count -gt 0   # ใส่ค่ามาทาง parameter = ไม่ถามคำถามเพิ่ม (ยกเว้นค่าที่ขาด)

function Step($t) { Write-Host "`n== $t" -ForegroundColor Cyan }
function Fail($t) { Write-Host "X $t" -ForegroundColor Red; exit 1 }
function Ask($prompt, $default) { $v = Read-Host "$prompt [$default]"; if ([string]::IsNullOrWhiteSpace($v)) { $default } else { $v.Trim() } }
function ProjectFile([string]$folder, [string]$name) { [IO.Path]::Combine($PSScriptRoot, $folder, $name) }

if ($PSScriptRoot -match '[\\/]Downloads[\\/]') {
    Write-Host "! โปรเจกต์อยู่ในโฟลเดอร์ Downloads — Windows บางเครื่อง (Smart App Control) จะบล็อกไฟล์ที่ build จากที่นี่" -ForegroundColor Yellow
    Write-Host "  ถ้าเปิดระบบแล้วขึ้น 'Application Control policy has blocked this file' ให้ย้ายโฟลเดอร์ไปที่อื่น เช่น C:\dev\pameka_7033_l005" -ForegroundColor Yellow
}

# ---------------------------------------------------------------- 1) .NET SDK
Step "ตรวจ .NET SDK"
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { Fail "ไม่พบ dotnet — ติดตั้ง .NET SDK 10 จาก https://dotnet.microsoft.com/download แล้วรันใหม่" }
$sdks = (& dotnet --list-sdks) -join "`n"
if ($sdks -notmatch '(?m)^10\.') { Fail "ยังไม่มี .NET SDK 10 (เครื่องมี: $sdks) — ติดตั้ง .NET SDK 10 แล้วรันใหม่" }
Write-Host "OK .NET SDK 10"

# ---------------------------------------------------------------- 2) MySQL client
Step "ตรวจ MySQL"
$mysql = (Get-Command mysql -ErrorAction SilentlyContinue).Source
if (-not $mysql) {
    $mysql = Get-ChildItem 'C:\Program Files\MySQL\MySQL Server *\bin\mysql.exe' -ErrorAction SilentlyContinue |
             Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $mysql) { Fail "ไม่พบโปรแกรม mysql.exe — ติดตั้ง MySQL Server 8 (หรือเพิ่มโฟลเดอร์ bin ของ MySQL ใน PATH) แล้วรันใหม่" }
Write-Host "OK $mysql"

if (-not $DbHost) { $DbHost = Ask "MySQL host" 'localhost' }
if (-not $DbPort) { $DbPort = Ask "MySQL port" '3306' }
if (-not $DbUser) { $DbUser = Ask "MySQL user" 'root' }
if (-not $PSBoundParameters.ContainsKey('DbPassword')) {
    $sec = Read-Host "MySQL password" -AsSecureString
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec)
    try { $DbPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}

# รหัสผ่านส่งผ่านตัวแปรแวดล้อม (ไม่โผล่ในประวัติคำสั่ง)
$env:MYSQL_PWD = $DbPassword
function RunSql([string]$sqlFile) {
    if (-not (Test-Path $sqlFile)) { Fail "ไม่พบไฟล์ $sqlFile" }
    $p = (Resolve-Path $sqlFile).Path -replace '\\', '/'
    & $mysql --default-character-set=utf8mb4 -h $DbHost -P $DbPort -u $DbUser -e "source $p"
    if ($LASTEXITCODE -ne 0) { Fail "รัน $sqlFile ไม่สำเร็จ (ตรวจ user/password และสิทธิ์สร้างฐานข้อมูล)" }
}
& $mysql -h $DbHost -P $DbPort -u $DbUser -e "SELECT 1" | Out-Null
if ($LASTEXITCODE -ne 0) { Fail "เชื่อมต่อ MySQL ไม่ได้ — ตรวจว่า MySQL เปิดอยู่ และ user/password ถูกต้อง" }
Write-Host "OK เชื่อมต่อ MySQL ได้"

# ---------------------------------------------------------------- 3) ฐานข้อมูล
Step "สร้าง/อัปเกรดฐานข้อมูล csi402db (เพิ่มอย่างเดียว ไม่ลบข้อมูลเดิม)"
RunSql (ProjectFile 'Database' 'aurellestay_mysql.sql')
if (-not $SkipSample) {
    $ans = if ($unattended) { 'y' } else { Read-Host "ใส่ห้อง/บริการตัวอย่าง (เฉพาะรายการที่ยังไม่มี)? [Y/n]" }
    if ($ans -ne 'n') { RunSql (ProjectFile 'Database' 'aurellestay_sample_catalog.sql') }
}
Write-Host "OK ฐานข้อมูลพร้อม"

# ---------------------------------------------------------------- 4) Connection string → user-secrets
Step "บันทึก connection string (user-secrets เก็บนอกโปรเจกต์ ไม่ติดไปกับ ZIP/Git)"
# ใส่รหัสผ่านในเครื่องหมายคำพูด เผื่อรหัสมี ; หรือช่องว่าง
$quoted = '"' + $DbPassword.Replace('"', '""') + '"'
$conn = "server=$DbHost;port=$DbPort;database=csi402db;user=$DbUser;password=$quoted;SslMode=None;AllowPublicKeyRetrieval=True"
& dotnet user-secrets set "ConnectionStrings:DefaultConnection" $conn | Out-Null
if ($LASTEXITCODE -ne 0) { Fail "บันทึก user-secrets ไม่สำเร็จ" }
Write-Host "OK"

# ---------------------------------------------------------------- 5) ผู้จัดการคนแรก (ไม่บังคับ)
Step "ตั้งผู้จัดการคนแรก (ข้ามได้)"
if (-not $AdminEmail -and -not $unattended) {
    $AdminEmail = Read-Host "อีเมลบัญชีที่สมัครไว้แล้วและจะให้เป็นผู้จัดการ (เว้นว่าง = ข้าม)"
}
if ($AdminEmail) {
    $safe = $AdminEmail.Trim().ToLowerInvariant().Replace("\", "\\").Replace("'", "''")
    $updated = & $mysql --default-character-set=utf8mb4 -N -h $DbHost -P $DbPort -u $DbUser csi402db `
        -e "UPDATE users SET role_id=(SELECT role_id FROM roles WHERE role_name='admin') WHERE email='$safe'; SELECT ROW_COUNT();"
    if ("$updated".Trim() -eq '1') { Write-Host "OK $AdminEmail เป็นผู้จัดการแล้ว (เข้าสู่ระบบโดยเลือกบทบาท 'ผู้จัดการ')" }
    else { Write-Host "! ยังไม่พบบัญชี $AdminEmail หรือเป็นผู้จัดการอยู่แล้ว — ถ้ายังไม่มี ให้สมัครสมาชิกในเว็บก่อน แล้วรันสคริปต์นี้อีกครั้ง" -ForegroundColor Yellow }
}
Remove-Item Env:\MYSQL_PWD -ErrorAction SilentlyContinue

# ---------------------------------------------------------------- 6) Build & Run
Step "Build"
& dotnet build
if ($LASTEXITCODE -ne 0) { Fail "build ไม่ผ่าน" }
if ($NoRun) { Write-Host "`nเสร็จแล้ว — เปิดระบบด้วยคำสั่ง: dotnet run" -ForegroundColor Green; exit 0 }

Step "เปิดระบบ (กด Ctrl+C เพื่อหยุด) — เปิดเบราว์เซอร์ตามลิงก์ที่ขึ้นด้านล่าง"
& dotnet run
if ($LASTEXITCODE -ne 0) {
    Write-Host "`nเปิดระบบไม่สำเร็จ" -ForegroundColor Red
    Write-Host "ถ้าขึ้น 'Application Control policy has blocked this file' = Windows บล็อกไฟล์ที่ build (ไม่ใช่บั๊กของโค้ด):" -ForegroundColor Yellow
    Write-Host "  1) ย้ายโฟลเดอร์ออกจาก Downloads เช่น C:\dev\pameka_7033_l005   2) Get-ChildItem -Recurse | Unblock-File" -ForegroundColor Yellow
    Write-Host "  3) ลบโฟลเดอร์ bin และ obj แล้วรันใหม่   4) ถ้ายังไม่ได้ ปิด Smart App Control ใน Windows Security" -ForegroundColor Yellow
    exit $LASTEXITCODE
}
