#!/usr/bin/env bash
# =====================================================================
# AurelleStay — รันทีเดียวจบ (macOS / Linux) : ./run.sh
#   ต้องมี Docker (Docker Desktop หรือ docker engine + compose plugin) เท่านั้น
#   สร้าง .env (รหัสสุ่ม) → build + เปิดเว็บ + MySQL 8 → รอพร้อม → แสดงบัญชีผู้จัดการ → เปิดเบราว์เซอร์
#   คำสั่งอื่น: ./run.sh down | logs | status
# =====================================================================
set -euo pipefail
cd "$(dirname "$0")"
compose() { docker compose -p aurellestay "$@"; }
command -v docker >/dev/null || { echo "X ไม่พบ Docker — ติดตั้ง Docker ก่อน"; exit 1; }
docker info >/dev/null 2>&1 || { echo "X Docker ยังไม่ทำงาน — เปิด Docker แล้วรันใหม่"; exit 1; }

case "${1:-up}" in
  down)   compose down; exit 0 ;;
  logs)   compose logs --tail 100 -f web; exit 0 ;;
  status) compose ps; exit 0 ;;
  up) ;;
  *) echo "ใช้: ./run.sh [up|down|logs|status]"; exit 1 ;;
esac

pw() { LC_ALL=C tr -dc 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789' </dev/urandom | head -c "${1:-24}"; }
if [ ! -f .env ]; then
  echo "== สร้างไฟล์ตั้งค่า .env (รหัสผ่านสุ่ม เก็บในเครื่องนี้เท่านั้น)"
  sed -e "s/^DB_ROOT_PASSWORD=.*/DB_ROOT_PASSWORD=$(pw)/" \
      -e "s/^DB_PASSWORD=.*/DB_PASSWORD=$(pw)/" \
      -e "s/^ADMIN_PASSWORD=.*/ADMIN_PASSWORD=Adm1$(pw 12)/" .env.example > .env
elif ! grep -q '^ADMIN_EMAIL=' .env; then
  printf '\nHTTPS_ONLY_COOKIES=false\nADMIN_EMAIL=admin@aurellestay.local\nADMIN_PASSWORD=Adm1%s\n' "$(pw 12)" >> .env
fi
val() { grep -E "^$1=" .env | head -1 | cut -d= -f2-; }

echo "== build และเปิดระบบ (ครั้งแรกดาวน์โหลด .NET + MySQL ใช้เวลาหลายนาที)"
compose up -d --build
port="$(val WEB_PORT)"; port="${port:-8088}"
echo "== รอระบบพร้อม http://localhost:$port"
for _ in $(seq 1 90); do
  if curl -fsS "http://localhost:$port/health" >/dev/null 2>&1; then
    echo; echo "พร้อมใช้งาน: http://localhost:$port"
    echo "ผู้จัดการ: $(val ADMIN_EMAIL)  รหัสผ่าน: $(val ADMIN_PASSWORD)  (ใช้ตอนเปิดครั้งแรก ดู/แก้ได้ใน .env)"
    (command -v open >/dev/null && open "http://localhost:$port") || (command -v xdg-open >/dev/null && xdg-open "http://localhost:$port" >/dev/null 2>&1) || true
    exit 0
  fi
  sleep 2
done
compose logs --tail 40 web; echo "X ระบบยังไม่ตอบ ดู log ด้านบน"; exit 1
