# QTKD — Phase 2 (Tuần 05): Nền tảng dự án

ASP.NET Core Web API (.NET 10) + Entity Framework Core + PostgreSQL.

## Cấu trúc

```
src/QTKD.Api/
  Domain/        Entity: Customer, Contract, PriceList, PriceItem
  Data/          QtkdDbContext, cấu hình quan hệ, dữ liệu mẫu (seed)
  Controllers/   API: GET /api/customers, GET /api/contracts/{id}
  Migrations/    Migration InitialCreate
docs/            Sơ đồ thành phần, minh chứng
docker-compose.yml   PostgreSQL chạy cục bộ (tuỳ chọn)
```

Tuần 06 sẽ thêm lớp `Services/` (xử lý nghiệp vụ) giữa Controllers và Data.

## Yêu cầu

.NET SDK 10, PostgreSQL 14+ (hoặc Docker), công cụ `dotnet-ef`:

```
dotnet tool install --global dotnet-ef
```

## 1. Cấu hình database

Cách A — dùng Docker:

```
export QTKD_DB_PASSWORD=<mật khẩu tự chọn>
docker compose up -d
```

Cách B — PostgreSQL có sẵn: tạo database rỗng `qtkd`.

Cấu hình chuỗi kết nối bằng User Secrets (không đưa mật khẩu lên Git):

```
cd src/QTKD.Api
dotnet user-secrets set "ConnectionStrings:QtkdDb" "Host=localhost;Port=5433;Database=qtkd;Username=postgres;Password=<mật khẩu>"
```

Hoặc dùng biến môi trường `ConnectionStrings__QtkdDb`. Giá trị trong `appsettings.json` chỉ là mẫu (`CHANGE_ME`).

## 2. Chạy migration

```
cd src/QTKD.Api
dotnet ef database update
```

Tạo 4 bảng `Customers`, `Contracts`, `PriceLists`, `PriceItems` cùng khóa chính/khóa ngoại và dữ liệu mẫu:
2 Customer, 1 Contract `HD-2026-018` (Draft), 1 PriceList có 2 PriceItem.

## 3. Khởi động API

```
dotnet run --project src/QTKD.Api --launch-profile http
```

Swagger UI: http://localhost:5084/swagger

## 4. Thử hai endpoint

```
curl http://localhost:5084/api/customers
curl http://localhost:5084/api/contracts/44444444-4444-4444-4444-444444444444
```

Đối chiếu với DB:

```
docker exec -it qtkd-postgres psql -U postgres -d qtkd -c 'SELECT * FROM "Customers";'
docker exec -it qtkd-postgres psql -U postgres -d qtkd -c 'SELECT * FROM "Contracts";'
```

## Ghi chú thiết kế

- Trạng thái `Status` lưu dạng chuỗi (`Draft`, `PendingApproval`, ...) cho dễ đọc trong DB; enum khớp sơ đồ trạng thái Phase 1.
- `PriceItem` dùng khóa chính ghép (`PriceListId`, `ServiceCode`).
- Xóa Customer bị chặn nếu còn Contract (`Restrict`); xóa PriceList xóa luôn PriceItem (`Cascade`).
