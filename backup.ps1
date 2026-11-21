# =====================================================================
# AurelleStay — สำรองข้อมูล (ฐานข้อมูล + หลักฐานการโอน + รูปห้องที่อัปโหลด) เป็น ZIP เดียวต่อครั้ง
# วิธีใช้ (เปิด PowerShell ในโฟลเดอร์โปรเจกต์):
#   powershell -ExecutionPolicy Bypass -File .\backup.ps1 -DbUser root
# ตั้งให้รันทุกคืนอัตโนมัติ (Task Scheduler) — ดูตัวอย่างคำสั่งใน README หัวข้อ "สำรองและกู้คืนข้อมูล"
# ผลลัพธ์: <OutDir>\AurelleStay_ปีเดือนวัน_เวลา.zip และลบไฟล์สำรองเก่าเกินจำนวน -Keep (ค่าเริ่มต้น 14 ชุด)
# รหัสผ่าน: ใส่ทาง -DbPassword, ตัวแปรแวดล้อม MYSQL_PWD หรือพิมพ์ตอนรัน (ไม่เขียนลงไฟล์)
# ไฟล์นี้บันทึกเป็น UTF-8 มี BOM เพื่อให้ Windows PowerShell 5.1 อ่านภาษาไทยถูก
# =====================================================================
param(
    [string]$DbHost = 'localhost', [string]$DbPort = '3306', [string]$DbUser = 'root', [string]$DbPassword,
    [string]$Database = 'csi402db',
    [string]$OutDir,
    [int]$Keep = 14
)
$ErrorActionPreference = 'Stop'
Set-Location -Path $PSScriptRoot
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }
function Fail($t) { Write-Host "X $t" -ForegroundColor Red; exit 1 }

if (-not $OutDir) { $OutDir = [IO.Path]::Combine([IO.Path]::GetDirectoryName($PSScriptRoot), 'AurelleStay-Backups') }   # เก็บนอกโฟลเดอร์โปรเจกต์
if ($Database -notmatch '^[A-Za-z0-9_]+$') { Fail "ชื่อฐานข้อมูลไม่ถูกต้อง" }

$dump = (Get-Command mysqldump -ErrorAction SilentlyContinue).Source
if (-not $dump) {
    $dump = Get-ChildItem 'C:\Program Files\MySQL\MySQL Server *\bin\mysqldump.exe' -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $dump) { Fail "ไม่พบ mysqldump — ติดตั้ง MySQL Server 8 หรือเพิ่มโฟลเดอร์ bin ของ MySQL ใน PATH" }

if ($PSBoundParameters.ContainsKey('DbPassword')) { $env:MYSQL_PWD = $DbPassword }
elseif (-not $env:MYSQL_PWD) {
    $sec = Read-Host "MySQL password" -AsSecureString
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec)
    try { $env:MYSQL_PWD = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$work = [IO.Path]::Combine([IO.Path]::GetTempPath(), "aurelle_backup_$stamp")
New-Item -ItemType Directory -Force -Path $work | Out-Null
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
try {
    # 1) ฐานข้อมูล (--single-transaction = สำรองได้ขณะระบบเปิดใช้งาน ไม่ล็อกตาราง)
    $sql = [IO.Path]::Combine($work, "$Database.sql")
    & $dump --single-transaction --routines --triggers --default-character-set=utf8mb4 --no-tablespaces `
        -h $DbHost -P $DbPort -u $DbUser "--result-file=$sql" $Database
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $sql) -or (Get-Item $sql).Length -lt 1000) { Fail "สำรองฐานข้อมูลไม่สำเร็จ (ตรวจ user/password และว่า MySQL เปิดอยู่)" }

    # 2) ไฟล์ที่ผู้ใช้อัปโหลด (ไม่มีในฐานข้อมูล): หลักฐานการโอน + รูปห้อง
    foreach ($pair in @(@('App_Data', 'slips'), @('wwwroot', 'images', 'rooms'))) {
        $src = [IO.Path]::Combine([string[]](@($PSScriptRoot) + $pair))
        if (Test-Path $src) {
            $dst = [IO.Path]::Combine([string[]](@($work, 'files') + $pair))
            New-Item -ItemType Directory -Force -Path $dst | Out-Null
            Copy-Item -Path ([IO.Path]::Combine($src, '*')) -Destination $dst -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    Set-Content -Path ([IO.Path]::Combine($work, 'README.txt')) -Encoding UTF8 -Value @(
        "AurelleStay backup $stamp (database: $Database)",
        "กู้คืนฐานข้อมูล: mysql -u root -p $Database < $Database.sql",
        "กู้คืนไฟล์: คัดลอกโฟลเดอร์ใน files\ ไปไว้ที่ตำแหน่งเดียวกันในโฟลเดอร์โปรเจกต์")

    $zip = [IO.Path]::Combine($OutDir, "AurelleStay_$stamp.zip")
    Compress-Archive -Path ([IO.Path]::Combine($work, '*')) -DestinationPath $zip -Force
    Write-Host "OK สำรองข้อมูลแล้ว: $zip ($([math]::Round((Get-Item $zip).Length / 1KB)) KB)" -ForegroundColor Green

    # 3) เก็บไว้แค่ $Keep ชุดล่าสุด
    if ($Keep -gt 0) {
        Get-ChildItem -Path $OutDir -Filter 'AurelleStay_*.zip' | Sort-Object Name -Descending | Select-Object -Skip $Keep |
            ForEach-Object { Remove-Item $_.FullName -Force; Write-Host "ลบไฟล์สำรองเก่า $($_.Name)" }
    }
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
    if ($PSBoundParameters.ContainsKey('DbPassword')) { Remove-Item Env:\MYSQL_PWD -ErrorAction SilentlyContinue }
}
