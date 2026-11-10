# QTKD — Phase 2 (Tuần 05–07)

ASP.NET Core Web API (.NET 10) + Entity Framework Core + PostgreSQL.

## Cấu trúc

```
src/QTKD.Api/
  Controllers/   Tầng API: nhận request, gọi Service, đổi kết quả thành HTTP status
  Services/      Tầng nghiệp vụ: CustomerService, ContractService (US03), PricingService
  Domain/        Entity + ContractRules (CTR-01/02/03, không phụ thuộc DB)
  Dtos/          Request/response, mẫu lỗi ErrorResponse
  Data/          QtkdDbContext, cấu hình quan hệ, dữ liệu mẫu (seed)
  Migrations/    InitialCreate (tuần 05), Week06_ContractWorkflow (tuần 06)
tests/
  QTKD.UnitTests/         Unit test nghiệp vụ: CTR-01/02/03, trường bắt buộc Customer (không cần DB)
  QTKD.IntegrationTests/  Integration test: gọi API thật + PostgreSQL kiểm thử riêng (Testcontainers), TC01–TC08
docs/
  api.md                 Danh sách API, request/response mẫu, mẫu lỗi
  us03-traceability.md   Đối chiếu US03 / CTR-01–03 với code, giả định, giới hạn
  demo/                  Script demo tuần 06 và kết quả chạy
  testing/               Tuần 07: báo cáo kiểm thử, kết quả dotnet test (console + TRX), lỗi đã sửa
docker-compose.yml   PostgreSQL chạy cục bộ (tuỳ chọn)
```

## Yêu cầu

.NET SDK 10, PostgreSQL 14+ (hoặc Docker), công cụ `dotnet-ef`. Chạy integration test cần Docker đang chạy:

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

Tạo các bảng `Customers`, `Contracts`, `ContractAttachments`, `ApprovalRequests`, `ContractHistory`,
`PriceLists`, `PriceItems` cùng khóa chính/khóa ngoại và dữ liệu mẫu:
2 Customer, 1 Contract `HD-2026-018` (Draft, còn thiếu nội dung và tệp bắt buộc), 1 PriceList có 2 PriceItem.

## 3. Khởi động API

```
dotnet run --project src/QTKD.Api --launch-profile http
```

Swagger UI: http://localhost:5084/swagger

## 4. Thử endpoint đọc dữ liệu (tuần 05)

```
curl http://localhost:5084/api/customers
curl http://localhost:5084/api/contracts/44444444-4444-4444-4444-444444444444
```

Đối chiếu với DB:

```
docker exec -it qtkd-postgres psql -U postgres -d qtkd -c 'SELECT * FROM "Customers";'
docker exec -it qtkd-postgres psql -U postgres -d qtkd -c 'SELECT * FROM "Contracts";'
```

## 5. Demo tuần 06: Customer → Contract → Submit

Cần `curl` và `jq`. Khi API đang chạy:

```
./docs/demo/demo-week06.sh
```

Script chạy theo thứ tự: tạo Customer (sai → đúng) → tạo Contract Draft → gửi khi thiếu dữ liệu (400, CTR-02)
→ thử Draft → Approved (409, CTR-03) → bổ sung nội dung và tệp → gửi lại (200, PendingApproval)
→ gửi lặp (cùng key: trả lại kết quả cũ; không key: 409) → sửa khi PendingApproval (409, CTR-01)
→ xem lịch sử và dữ liệu trong PostgreSQL → 5 yêu cầu gửi đồng thời (chỉ 1 thành công) → Pricing.
Kết quả một lần chạy: [docs/demo/demo-week06-output.txt](docs/demo/demo-week06-output.txt).

Kiểm thử lại toàn bộ yêu cầu tuần 06 (có assert, đối chiếu DB) — nên dùng database riêng:

```
docker exec qtkd-postgres psql -U postgres -c 'CREATE DATABASE qtkd_verify'
cd src/QTKD.Api && dotnet ef database update --connection "Host=localhost;Port=5433;Database=qtkd_verify;Username=postgres;Password=<mật khẩu>" && cd ../..
ConnectionStrings__QtkdDb="Host=localhost;Port=5433;Database=qtkd_verify;Username=postgres;Password=<mật khẩu>" \
  dotnet run --project src/QTKD.Api --launch-profile http --urls http://localhost:5085
# terminal khác:
API=http://localhost:5085 DB=qtkd_verify ./docs/demo/verify-week06.sh
```

Kết quả: [docs/demo/verify-week06-output.txt](docs/demo/verify-week06-output.txt) (106 PASS / 0 FAIL).

Danh sách API đầy đủ: [docs/api.md](docs/api.md). Đối chiếu US03/CTR-01–03: [docs/us03-traceability.md](docs/us03-traceability.md).

Xem trạng thái trong DB:

```
docker exec -it qtkd-postgres psql -U postgres -d qtkd -c 'SELECT "ContractNo", "Status" FROM "Contracts";'
docker exec -it qtkd-postgres psql -U postgres -d qtkd -c 'SELECT * FROM "ApprovalRequests";'
docker exec -it qtkd-postgres psql -U postgres -d qtkd -c 'SELECT * FROM "ContractHistory" ORDER BY "CreatedAt";'
```

## 6. Tuần 07: chạy test tự động

```
dotnet test src/QTKD.slnx
```

- **Unit test** (`tests/QTKD.UnitTests`) không cần database.
- **Integration test** (`tests/QTKD.IntegrationTests`) tự tạo một container PostgreSQL 16 tạm thời (Testcontainers),
  chạy migration từ đầu, gọi API qua HTTP rồi đối chiếu dữ liệu trong DB. Container bị xóa sau khi chạy xong,
  **không đụng** tới database `qtkd`. Yêu cầu: Docker đang chạy.
- Không có Docker: tạo một database rỗng có tên chứa `test` rồi trỏ test tới đó. Database này **bị xóa và tạo lại** mỗi lần chạy:

  ```
  export QTKD_TEST_DB="Host=localhost;Port=5433;Database=qtkd_test;Username=postgres;Password=<mật khẩu>"
  dotnet test src/QTKD.slnx
  ```

Lưu kết quả (console + TRX):

```
dotnet test src/QTKD.slnx --logger "trx;LogFilePrefix=qtkd" --logger "console;verbosity=normal" --results-directory docs/testing/trx
```

Kết quả hiện tại: 47/47 unit test, 33/33 integration test pass. Bảng TC01–TC08, lỗi đã sửa và lỗi còn tồn tại:
[docs/testing/test-report.md](docs/testing/test-report.md).

## 7. Demo cuối: clone → cấu hình → migration → chạy API → chạy test

```
git clone <url-repo> QTKD && cd QTKD
export QTKD_DB_PASSWORD=<mật khẩu tự chọn>
docker compose up -d                                   # PostgreSQL ở cổng 5433
cd src/QTKD.Api
dotnet user-secrets set "ConnectionStrings:QtkdDb" "Host=localhost;Port=5433;Database=qtkd;Username=postgres;Password=$QTKD_DB_PASSWORD"
dotnet ef database update                              # migration
cd ../..
dotnet run --project src/QTKD.Api --launch-profile http   # API: http://localhost:5084/swagger
# terminal khác:
./docs/demo/demo-week06.sh                             # luồng Customer → Contract → Submit
dotnet test src/QTKD.slnx                              # unit + integration test
```

## Ghi chú thiết kế

- Trạng thái `Status` lưu dạng chuỗi (`Draft`, `PendingApproval`, ...) cho dễ đọc trong DB; enum khớp sơ đồ trạng thái Phase 1.
- `PriceItem` dùng khóa chính ghép (`PriceListId`, `ServiceCode`).
- Xóa Customer bị chặn nếu còn Contract (`Restrict`); xóa PriceList xóa luôn PriceItem (`Cascade`).
- Lỗi nghiệp vụ không dùng exception: Service trả `Result<T>` (giá trị hoặc `ServiceError`), Controller đổi sang 400/404/409.
- Gửi trình duyệt: tạo `ApprovalRequest`, đổi trạng thái và ghi lịch sử trong cùng một `SaveChanges` (một transaction).
- Chống gửi trùng: `Idempotency-Key`, concurrency token `xmin` trên `Contracts`, unique index một phiên Pending cho mỗi hợp đồng.
- Giả định CTR-02 và giới hạn hiện tại (chưa phân quyền, tệp chỉ là metadata...): xem [docs/us03-traceability.md](docs/us03-traceability.md).
