# Tuần 07 — Báo cáo kiểm thử QTKD

Kiểm thử chính mã nguồn tuần 06 (Customer → Contract → Submit, CTR-01/02/03). Không xây thêm chức năng mới.

## 1. Tổ chức test

| Project | Loại | Phạm vi | Database |
|---|---|---|---|
| [tests/QTKD.UnitTests](../../tests/QTKD.UnitTests) | Unit test | `ContractRules` (CTR-01/02/03, ngày hiệu lực), `CustomerService.Validate` (trường bắt buộc) | Không dùng |
| [tests/QTKD.IntegrationTests](../../tests/QTKD.IntegrationTests) | Integration test | Gọi HTTP vào API thật (`WebApplicationFactory<Program>`) → Service → EF Core → PostgreSQL | PostgreSQL 16 **tạm thời** do Testcontainers tạo, chạy cả 2 migration từ DB trống, xóa sau khi chạy |

Vì sao không dùng EF InMemory/SQLite cho integration test: code dựa vào tính năng riêng của PostgreSQL
(cột `xmin` làm concurrency token, unique index có điều kiện `"Status" = 'Pending'`, mã lỗi `23505`).
Thay DB khác sẽ không kiểm tra được TC08 (gửi lặp/đồng thời).

Mỗi integration test tự tạo Customer (mã số thuế ngẫu nhiên) và Contract (số hợp đồng ngẫu nhiên) của riêng nó, nên các test độc lập
và không đụng tới dữ liệu mẫu. Sau mỗi lần gọi API, test kiểm tra **3 thứ**: HTTP status, nội dung body (mã lỗi, danh sách điều kiện…)
và dữ liệu đọc trực tiếp từ DB bằng một `QtkdDbContext` riêng. Với các trường hợp bị từ chối, test so sánh
"ảnh chụp" (`SnapshotAsync`) của hợp đồng trước và sau: trạng thái, các trường, `UpdatedAt`, `xmin`, số `ApprovalRequests`,
số dòng `ContractHistory`, số tệp. Nhờ vậy chứng minh được "dữ liệu không đổi", không chỉ dựa vào việc API trả 4xx.

## 2. Kết quả `dotnet test`

```
Passed!  - Failed: 0, Passed: 47, Skipped: 0, Total: 47 - QTKD.UnitTests.dll (net10.0)
Passed!  - Failed: 0, Passed: 33, Skipped: 0, Total: 33 - QTKD.IntegrationTests.dll (net10.0)
```

- Toàn bộ output console: [dotnet-test-output.txt](dotnet-test-output.txt)
- File TRX: [trx/](trx/)

## 3. Bảng TC01–TC08

Tất cả đều **tự động** (chạy bằng `dotnet test`); không có test nào phải chạy thủ công.

| TC | Dữ liệu / thao tác | Kết quả mong đợi | Test tự động | Loại | Kết quả |
|---|---|---|---|---|---|
| TC01 | Tạo Customer hợp lệ | 201, body đúng (đã trim), đọc lại qua `GET` và từ bảng `Customers` | `CustomerApiTests.TC01_TaoCustomerHopLe_TaoDuocVaDocLaiTuDb` | Integration | Pass |
| TC02 | Tạo Customer thiếu tên + mã số thuế; body không phải JSON | 400 `VALIDATION_FAILED` có lỗi cho `name`, `taxCode`; 400 `INVALID_REQUEST`; số Customer trong DB không đổi | `CustomerApiTests.TC02_…`, `TC02b_…`; `CustomerValidationTests.Validate_Thieu…` | Integration + Unit | Pass |
| TC03 | Sửa Contract ở Draft (đổi khách hàng, ngày, nội dung); sửa với ngày sai | 200, DB đã đổi + lịch sử `Updated`; dữ liệu sai → 400, DB không đổi | `ContractApiTests.TC03_…`, `TC03b_…`; `ContractRulesTests.CTR01_DuocSua_…` | Integration + Unit | Pass |
| TC04 | Sửa Contract / thêm tệp khi PendingApproval | 409 `CTR01_NOT_EDITABLE`, snapshot DB giữ nguyên | `ContractApiTests.TC04_…`, `TC04b_…`; `ContractRulesTests.CTR01_KhongDuocSua_…` | Integration + Unit | Pass |
| TC05 | Submit khi thiếu **từng** điều kiện CTR-02: nội dung, tệp dự thảo, khách hàng Inactive, hết hiệu lực, thiếu nhiều điều kiện | 400 `CTR02_NOT_SATISFIED` liệt kê **đúng** các điều kiện chưa đạt; vẫn Draft, 0 ApprovalRequest, snapshot không đổi | `ContractSubmitTests.TC05a…TC05e`; `ContractRulesTests.CTR02_…` (12 case) | Integration + Unit | Pass |
| TC06 | Submit hợp lệ (có comment) | 200 `PendingApproval`; DB: `Contracts.Status`, 1 `ApprovalRequests` Pending đúng `approvalId`/người gửi/comment, lịch sử `Submitted` Draft→PendingApproval | `ContractSubmitTests.TC06_…` | Integration | Pass |
| TC07 | `POST /approve` khi Draft (dù đủ CTR-02) | 409 `CTR03_INVALID_TRANSITION`, vẫn Draft; trường hợp đúng: sau Submit thì approve được | `ContractSubmitTests.TC07_…`, `TC07b_…`; `ContractRulesTests.CTR03_…` | Integration + Unit | Pass |
| TC08 | Submit lặp: cùng `Idempotency-Key`; không key; 5 yêu cầu đồng thời khác key; 5 yêu cầu đồng thời cùng key | Cùng key → 200 `replayed=true`, cùng `approvalId`/`submittedAt`; không key → 409; đồng thời → đúng 1×200; luôn chỉ **1** ApprovalRequest và 1 lịch sử `Submitted` | `ContractSubmitTests.TC08a…TC08d` | Integration | Pass |

Test bổ sung ngoài TC01–TC08: luồng demo tuần 06 end-to-end (`DemoFlowTests.LuongDemoTuan06_…`), dữ liệu mẫu tuần 05 đọc được sau migration,
Pricing (đơn giá âm bị từ chối, liên kết PriceItem–PriceList đúng), Customer trùng mã số thuế, Contract trùng số / customer không tồn tại / Inactive,
404 cho id không tồn tại, và 2 test hồi quy cho BUG-02.

## 4. Lỗi tìm thấy và đã sửa

### BUG-01 — Project khác không build được khi tham chiếu `QTKD.Api`

| | |
|---|---|
| Cách tái hiện | Tạo project xUnit, `dotnet add reference src/QTKD.Api`, `dotnet build` |
| Hiện tượng | `error CS1705: Assembly 'QTKD.Api' uses 'Microsoft.EntityFrameworkCore, Version=10.0.14.0' which has a higher version than referenced assembly ... Version=10.0.4.0` |
| Nguyên nhân | `QTKD.Api` có gói `Microsoft.EntityFrameworkCore.Design` 10.0.14 (đánh dấu `PrivateAssets=all`) nên API được biên dịch với EF Core 10.0.14. Tuy vậy, gói này không được truyền sang project tham chiếu; ở đó chỉ còn EF Core 10.0.4 mà `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 kéo theo. API chạy độc lập nên tuần 05–06 chưa lộ lỗi. |
| Cách sửa | Khai báo rõ `Microsoft.EntityFrameworkCore.Relational` 10.0.14 trong `QTKD.Api.csproj` |
| Commit | `075e871` — Sửa xung đột phiên bản EF Core khi project khác tham chiếu QTKD.Api |

### BUG-02 — Thời gian trả về trong response khác giá trị lưu trong PostgreSQL

| | |
|---|---|
| Cách tái hiện | Trên Windows/Linux (đồng hồ có độ chính xác 100ns): Submit với `Idempotency-Key: K` → ghi lại `submittedAt`. Submit lại với cùng `K`. Trên macOS, chạy test `TimestampPrecisionTests`, test này thay `TimeProvider` bằng đồng hồ 100ns. |
| Hiện tượng | Lần gửi đầu trả `submittedAt = …44.5659197`, lần gửi lặp (đọc từ DB) trả `…44.5659190`: cùng một lần gửi nhưng hai thời điểm khác nhau. Tương tự, `updatedAt` trong response của POST/PUT Contract khác giá trị khi GET lại. Output lúc chưa sửa: [bug-02-before-fix.txt](bug-02-before-fix.txt) |
| Nguyên nhân | `timestamptz` của PostgreSQL chỉ lưu đến micro giây (6 chữ số). Service trả về object `DateTimeOffset` còn trong bộ nhớ (7 chữ số), còn khi đọc lại thì nhận giá trị đã bị làm tròn. Trên máy dev (macOS) đồng hồ chỉ có độ chính xác micro giây nên lỗi không lộ ra. Đây đúng là trường hợp "API trả 200 nhưng dữ liệu không khớp". |
| Cách sửa | `ContractService.UtcNow()` cắt thời gian về micro giây trước khi gán vào entity và trả về |
| Commit | `d8f8c14` — Sửa lỗi thời gian trả về khác giá trị lưu trong PostgreSQL (BUG-02) |
| Kiểm chứng | `TimestampPrecisionTests` (2 test): Fail trước khi sửa, Pass sau khi sửa |

(Lỗi tìm thấy ở tuần 06 — idempotency khi gửi đồng thời cùng key — đã ghi trong [us03-traceability.md](../us03-traceability.md) và nay có test `TC08d`.)

## 5. Chạy lại luồng demo tuần 06 sau khi sửa lỗi

Trên database kiểm thử riêng `qtkd_verify` (xóa và tạo lại, chạy migration từ đầu), API đã có bản sửa lỗi:

- [verify-week06-after-week07.txt](verify-week06-after-week07.txt): `verify-week06.sh` cho **106 PASS / 0 FAIL**
- [demo-week06-after-week07.txt](demo-week06-after-week07.txt): `demo-week06.sh` trả đúng mọi mã HTTP mong đợi (400 → 409 → 200 → 409…)

## 6. Lỗi / hạn chế còn tồn tại

| # | Mô tả | Mức độ | Ghi chú |
|---|---|---|---|
| 1 | Lỗi do routing (id không phải GUID như `GET /api/contracts/abc`, route không tồn tại, sai method 405) trả **body rỗng**, không theo mẫu `ErrorResponse` | Thấp | Đã kiểm tra bằng curl. Có thể sửa bằng `UseStatusCodePages` |
| 2 | `Idempotency-Key` chỉ gồm khoảng trắng vẫn được coi là một key hợp lệ (không trim) | Thấp | Không gây trùng dữ liệu |
| 3 | Ngày "hôm nay" cho CTR-02 lấy theo múi giờ của máy chủ (`GetLocalNow`); nếu server chạy UTC thì từ 00:00 đến 07:00 giờ Việt Nam, ngày được tính là ngày hôm trước | Thấp | Cần chốt múi giờ nghiệp vụ |
| 4 | Các giới hạn phạm vi đã nêu ở tuần 06: chưa có xác thực/phân quyền, tệp chỉ là metadata, danh sách nội dung/tệp bắt buộc là giả định, chưa có luồng RevisionRequested → Draft | — | [us03-traceability.md §5](../us03-traceability.md) |

## 7. Các tình huống đã thử

- Trường bắt buộc: null, chuỗi rỗng, chỉ khoảng trắng, quá độ dài, sai định dạng mã số thuế và trạng thái (kể cả `"active"` viết thường, `"1"`)
- Ranh giới ngày: kết thúc = bắt đầu, kết thúc = hôm nay (hợp lệ), kết thúc = hôm qua (hết hiệu lực)
- Tệp sai loại (có `PhuLuc` nhưng không có `DuThaoHopDong`)
- Khách hàng bị chuyển sang Inactive **sau** khi đã tạo hợp đồng
- Gửi lặp tuần tự và đồng thời, cùng key và khác key
- Sửa và thêm tệp khi đã gửi trình duyệt; duyệt khi còn Draft
- Id không tồn tại (404), trùng số hợp đồng / mã số thuế (409), body JSON hỏng
