# =====================================================================
# AurelleStay — เปิดระบบด้วย Docker (Part 45)
# วิธีใช้ (PowerShell ในโฟลเดอร์โปรเจกต์):
#   powershell -ExecutionPolicy Bypass -File .\docker.ps1 up                  เปิดระบบ (ครั้งแรกสร้างฐานข้อมูลให้)
#   powershell -ExecutionPolicy Bypass -File .\docker.ps1 admin you@mail.com  ตั้งบัญชีนี้เป็นผู้จัดการ (สมัครสมาชิกในเว็บก่อน)
#   powershell -ExecutionPolicy Bypass -File .\docker.ps1 moredata              เพิ่มห้อง/พนักงาน/บริการเสริม/กิจกรรมตัวอย่าง (รันซ้ำได้ ไม่ลบของเดิม)
#   powershell -ExecutionPolicy Bypass -File .\docker.ps1 status | logs | down | backup
# ทำงานเฉพาะโปรเจกต์ Docker ชื่อ "aurellestay" เท่านั้น ไม่แตะโปรเจกต์อื่น
# ไฟล์นี้บันทึกเป็น UTF-8 มี BOM เพื่อให้ Windows PowerShell 5.1 อ่านภาษาไทยถูก
# =====================================================================
param(
    [Parameter(Position = 0)][ValidateSet('up', 'down', 'status', 'logs', 'admin', 'backup', 'moredata')][string]$Command = 'up',
    [Parameter(Position = 1)][string]$Arg
)
$ErrorActionPreference = 'Stop'
Set-Location -Path $PSScriptRoot
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }
function Step($t) { Write-Host "`n== $t" -ForegroundColor Cyan }
function Fail($t) { Write-Host "X $t" -ForegroundColor Red; exit 1 }
function Compose { & docker compose -p aurellestay @args; if ($LASTEXITCODE -ne 0) { Fail "docker compose $($args -join ' ') ไม่สำเร็จ" } }

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { Fail 'ไม่พบ Docker — ติดตั้ง Docker Desktop ก่อน' }
& docker info *> $null
if ($LASTEXITCODE -ne 0) { Fail 'Docker ยังไม่ทำงาน — เปิด Docker Desktop แล้วรอจนขึ้น Engine running จากนั้นรันใหม่' }

$envFile = Join-Path $PSScriptRoot '.env'
function EnvValue($name) {
    if (-not (Test-Path $envFile)) { return $null }
    $line = Get-Content $envFile | Where-Object { $_ -match "^\s*$name\s*=" } | Select-Object -First 1
    if ($line) { return ($line -split '=', 2)[1].Trim() } else { return $null }
}

switch ($Command) {
    'up' {
        if (-not (Test-Path $envFile)) {
            Step 'สร้างไฟล์ตั้งค่า .env (รหัสผ่านฐานข้อมูลสุ่มใหม่ เก็บในเครื่องนี้เท่านั้น)'
            $chars = [char[]]'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789'
            function NewPw { -join (1..24 | ForEach-Object { $chars | Get-Random }) }
            $text = (Get-Content (Join-Path $PSScriptRoot '.env.example') -Raw) `
                -replace 'DB_ROOT_PASSWORD=.*', ("DB_ROOT_PASSWORD=" + (NewPw)) `
                -replace 'DB_PASSWORD=.*', ("DB_PASSWORD=" + (NewPw))
            [IO.File]::WriteAllText($envFile, $text, (New-Object Text.UTF8Encoding $false))
        }
        Step 'สร้าง image และเปิดระบบ (ครั้งแรกดาวน์โหลด .NET + MySQL ใช้เวลาหลายนาที)'
        Compose up -d --build
        $port = EnvValue 'WEB_PORT'; if (-not $port) { $port = '8088' }
        Step "รอระบบพร้อม (http://localhost:$port)"
        $ok = $false
        for ($i = 0; $i -lt 90; $i++) {
            try { $r = Invoke-WebRequest "http://localhost:$port/health" -UseBasicParsing -TimeoutSec 3; if ($r.StatusCode -eq 200) { $ok = $true; break } } catch { }
            Start-Sleep -Seconds 2
        }
        if (-not $ok) { Compose logs --tail 40 web; Fail 'ระบบยังไม่ตอบ ดู log ด้านบน' }
        Write-Host "`nพร้อมใช้งาน: http://localhost:$port" -ForegroundColor Green
        Write-Host "ยังไม่มีผู้จัดการ? สมัครสมาชิกในเว็บก่อน แล้วรัน: .\docker.ps1 admin อีเมลของคุณ"
    }
    'down' { Step 'ปิดระบบ (ข้อมูลยังอยู่ใน volume ไม่ถูกลบ)'; Compose down }
    'status' { Compose ps }
    'logs' { Compose logs --tail 100 -f web }
    'admin' {
        if (-not $Arg -or $Arg -notmatch '^[^@\s]+@[^@\s]+\.[^@\s]+$') { Fail 'ระบุอีเมล เช่น .\docker.ps1 admin you@mail.com' }
        $pw = EnvValue 'DB_ROOT_PASSWORD'
        $email = $Arg.Replace("'", "''")
        $sql = "UPDATE users SET role_id=(SELECT role_id FROM roles WHERE role_name='admin') WHERE email='$email'; SELECT ROW_COUNT();"
        $n = & docker compose -p aurellestay exec -T -e "MYSQL_PWD=$pw" db mysql -N -uroot csi402db -e $sql
        if ("$n".Trim() -eq '1') { Write-Host "ตั้ง $Arg เป็นผู้จัดการแล้ว — ออกจากระบบแล้วเข้าใหม่ โดยเลือกบทบาทผู้จัดการ" -ForegroundColor Green }
        else { Fail "ไม่พบบัญชี $Arg (หรือเป็นผู้จัดการอยู่แล้ว) — สมัครสมาชิกในเว็บก่อน" }
    }
    'moredata' {
        # Part 59: เพิ่มข้อมูลจาก Database/aurellestay_more_data.sql (เพิ่มอย่างเดียว รันซ้ำได้)
        $file = Join-Path $PSScriptRoot 'Database\aurellestay_more_data.sql'
        if (-not (Test-Path $file)) { Fail 'ไม่พบไฟล์ Database\aurellestay_more_data.sql' }
        $pw = EnvValue 'DB_ROOT_PASSWORD'
        Step 'เพิ่มห้องพัก พนักงาน บริการเสริม และกิจกรรม'
        Compose cp $file db:/tmp/more_data.sql
        & docker compose -p aurellestay exec -T -e "MYSQL_PWD=$pw" db sh -c 'mysql -uroot --default-character-set=utf8mb4 csi402db < /tmp/more_data.sql'
        if ($LASTEXITCODE -ne 0) { Fail 'เพิ่มข้อมูลไม่สำเร็จ' }
        $n = (& docker compose -p aurellestay exec -T -e "MYSQL_PWD=$pw" db mysql -N -uroot csi402db -e "SELECT (SELECT COUNT(*) FROM rooms), (SELECT COUNT(*) FROM extra_services), (SELECT COUNT(*) FROM activities), (SELECT COUNT(*) FROM users u JOIN roles r USING(role_id) WHERE r.role_name<>'customer')") -split '\s+'
        Write-Host "เสร็จแล้ว: ห้อง $($n[0]) · บริการเสริม $($n[1]) · กิจกรรม $($n[2]) · พนักงาน $($n[3])" -ForegroundColor Green
    }
    'backup' {
        $dir = Join-Path $PSScriptRoot 'Backups'; New-Item -ItemType Directory -Force -Path $dir | Out-Null
        $file = Join-Path $dir ("docker-csi402db-" + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.sql')
        $pw = EnvValue 'DB_ROOT_PASSWORD'
        Step "สำรองฐานข้อมูลไปที่ $file"
        & docker compose -p aurellestay exec -T -e "MYSQL_PWD=$pw" db mysqldump -uroot --single-transaction --routines --default-character-set=utf8mb4 csi402db | Out-File -FilePath $file -Encoding utf8
        if ($LASTEXITCODE -ne 0) { Fail 'สำรองไม่สำเร็จ' }
        Write-Host "สำรองเสร็จ: $file" -ForegroundColor Green
    }
}
