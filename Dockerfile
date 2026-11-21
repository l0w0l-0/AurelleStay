# =====================================================================
# AurelleStay — Docker image (Part 45)
# สร้าง: docker compose build   ·   ใช้คู่กับ docker-compose.yml (เว็บ + MySQL 8)
# =====================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY pameka_7033_l005.csproj ./
RUN dotnet restore pameka_7033_l005.csproj
COPY . .
RUN dotnet publish pameka_7033_l005.csproj -c Release -o /out --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    TZ=Asia/Bangkok \
    DOTNET_RUNNING_IN_CONTAINER=true
COPY --from=build /out ./
# โฟลเดอร์ที่ระบบเขียนไฟล์ (สลิป รูปห้อง รูปบริการ กุญแจคุกกี้) — ให้ผู้ใช้ app (ไม่ใช่ root) เขียนได้
RUN mkdir -p /app/App_Data/slips /app/wwwroot/images/rooms /app/wwwroot/images/services /home/app/.aspnet/DataProtection-Keys \
    && chown -R app:app /app/App_Data /app/wwwroot/images /home/app/.aspnet
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "pameka_7033_l005.dll"]
