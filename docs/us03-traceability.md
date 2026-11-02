# US03 — Gửi hợp đồng trình duyệt: đặc tả áp dụng và đối chiếu mã nguồn (Tuần 06)

## 1. US03 và điều kiện gửi (từ Phase 1)

> Là nhân viên Kinh doanh, tôi muốn gửi hợp đồng đã hoàn thiện vào quy trình phê duyệt,
> để hợp đồng được rà soát và phê duyệt theo quy trình.

| Mục | Nội dung áp dụng trong code |
|---|---|
| Trạng thái trước | `Draft` |
| Trạng thái sau (thành công) | `PendingApproval` + 1 bản ghi `ApprovalRequests` (Pending) + 1 dòng `ContractHistory` (Submitted) |
| Điều kiện gửi (CTR-02) | Khách hàng hợp lệ; thời gian hiệu lực hợp lệ; đủ nội dung bắt buộc; đủ tệp bắt buộc |
| Trường hợp từ chối | Không tìm thấy (404); sai trạng thái / đã gửi (409); không đạt CTR-02 (400, kèm danh sách điều kiện chưa đạt); xung đột đồng thời (409) |
| Khi từ chối | Không đổi `Contracts`, không tạo `ApprovalRequests`, không ghi `ContractHistory` |

### Giả định cho CTR-02 (cần xác nhận — không phải yêu cầu chính thức)

Phase 1 còn để mở Q2 / "Danh sách chính xác các tệp/nội dung bắt buộc theo từng loại hợp đồng". Bản demo dùng giả định sau,
tập trung tại `ContractRules.EvaluateSubmitConditions` để dễ sửa khi được xác nhận:

| Mã điều kiện | Giả định dùng trong demo | Cần xác nhận |
|---|---|---|
| `CUSTOMER_VALID` | Khách hàng tồn tại và `Status = Active` | Có cần kiểm tra thêm (MST, hồ sơ pháp lý...)? |
| `EFFECTIVE_PERIOD_VALID` | `EndDate > StartDate` và `EndDate` chưa qua ngày gửi | Có cho phép `StartDate` ở quá khứ không? |
| `REQUIRED_CONTENT` | `ServiceGroup` và `Content` không rỗng | Danh sách trường theo từng loại hợp đồng |
| `REQUIRED_FILES` | Có ít nhất 1 tệp loại `DuThaoHopDong` | Danh sách tệp theo từng loại hợp đồng |

## 2. Bảng đối chiếu quy tắc ↔ mã nguồn

| Mã | Yêu cầu | Xử lý trong code | Kiểm chứng (demo) |
|---|---|---|---|
| **CTR-01** | Chỉ sửa khi Draft hoặc RevisionRequested | `ContractRules.IsEditable` — [src/QTKD.Api/Domain/ContractRules.cs:20](../src/QTKD.Api/Domain/ContractRules.cs#L20); gọi trong `UpdateAsync` [ContractService.cs:65](../src/QTKD.Api/Services/ContractService.cs#L65) và `AddAttachmentAsync` [ContractService.cs:83](../src/QTKD.Api/Services/ContractService.cs#L83) → 409 `CTR01_NOT_EDITABLE` | Bước 5a (Draft: 200), bước 8 (PendingApproval: 409) |
| **CTR-02** | Chỉ Submit khi khách hàng, ngày hiệu lực, nội dung/tệp bắt buộc hợp lệ | `ContractRules.EvaluateSubmitConditions` [ContractRules.cs:30](../src/QTKD.Api/Domain/ContractRules.cs#L30); gọi trong `SubmitAsync` [ContractService.cs:143](../src/QTKD.Api/Services/ContractService.cs#L143) → 400 `CTR02_NOT_SATISFIED` trước khi thay đổi bất kỳ dữ liệu nào | Bước 3 (thiếu nội dung + tệp: 400, vẫn Draft) |
| **CTR-03** | Không chuyển Draft → Approved | Bảng chuyển trạng thái `ContractRules.CanTransition` [ContractRules.cs:24](../src/QTKD.Api/Domain/ContractRules.cs#L24) (Draft chỉ đi được tới PendingApproval); `SubmitAsync` chỉ đặt `PendingApproval`; `ApproveAsync` [ContractService.cs:178](../src/QTKD.Api/Services/ContractService.cs#L178) → 409 `CTR03_INVALID_TRANSITION`; request tạo/sửa không có trường `status` | Bước 4 (approve khi Draft: 409), bước 6 (submit → PendingApproval) |

Bảng chuyển trạng thái (theo sơ đồ máy trạng thái Phase 1):

| Từ | Được chuyển tới |
|---|---|
| Draft | PendingApproval |
| PendingApproval | RevisionRequested, Rejected, Approved |
| RevisionRequested | Draft (hiệu chỉnh) |
| Approved | Active |

## 3. Bảng đối chiếu UC-CON-03 / AC ↔ mã nguồn

| Bước / AC | Xử lý | Vị trí |
|---|---|---|
| 1. Nhận yêu cầu | `POST /api/contracts/{id}/submit` | [ContractsController.cs](../src/QTKD.Api/Controllers/ContractsController.cs) `Submit` |
| 2. Kiểm tra quyền (AC01, A1) | **Chưa làm** — xem giới hạn | — |
| 2. Kiểm tra trạng thái (AC02, A2) | Khác Draft → 409 `CONTRACT_ALREADY_SUBMITTED` / `INVALID_STATUS` | [ContractService.cs:134-140](../src/QTKD.Api/Services/ContractService.cs#L134-L140) |
| 3. Kiểm tra CTR-02 (AC03, A3) | Trả danh sách điều kiện chưa đạt | [ContractService.cs:142-149](../src/QTKD.Api/Services/ContractService.cs#L142-L149) |
| 4. Khởi tạo quy trình phê duyệt (AC04) | Tạo `ApprovalRequest` trạng thái Pending (mô phỏng) | [ContractService.cs:151-163](../src/QTKD.Api/Services/ContractService.cs#L151-L163) |
| 5. Cập nhật trạng thái Chờ phê duyệt | `Status = PendingApproval` | [ContractService.cs:164-166](../src/QTKD.Api/Services/ContractService.cs#L164-L166) |
| 6. Ghi nhận lịch sử | `ContractHistory` action `Submitted` | [ContractService.cs:167](../src/QTKD.Api/Services/ContractService.cs#L167) |
| 7. Trả kết quả | 200 `SubmitResponse` | `ToSubmitResponse` |
| A4 / AC05 — gửi lặp | (1) cùng `Idempotency-Key` → trả kết quả cũ, không tạo bản ghi mới [ContractService.cs:126-132](../src/QTKD.Api/Services/ContractService.cs#L126-L132); (2) không key → 409 vì đã PendingApproval | Demo bước 7a, 7b |
| AC05 — đồng thời | (1) Concurrency token `xmin` trên `Contracts` [QtkdDbContext.cs:38](../src/QTKD.Api/Data/QtkdDbContext.cs#L38): yêu cầu đến sau bị `DbUpdateConcurrencyException`; (2) unique index lọc `Status = 'Pending'` trên `ApprovalRequests(ContractId)` [QtkdDbContext.cs:64](../src/QTKD.Api/Data/QtkdDbContext.cs#L64) chặn ở mức DB → cả hai trả 409 `CONCURRENT_UPDATE` [ContractService.cs:289-300](../src/QTKD.Api/Services/ContractService.cs#L289-L300); (3) nếu các yêu cầu đồng thời **cùng** `Idempotency-Key`, yêu cầu đến sau trả lại kết quả của yêu cầu thắng (200, `replayed: true`) [ContractService.cs:171-180](../src/QTKD.Api/Services/ContractService.cs#L171-L180) | Demo bước 11: 5 yêu cầu song song → 1×200, 4×409, 1 ApprovalRequest; verify mục 6: 10 yêu cầu → 1×200 / 9×409, 5 yêu cầu cùng key → 5×200, 1 ApprovalRequest |
| A5 — không khởi tạo được quy trình | Tạo ApprovalRequest, đổi trạng thái và ghi lịch sử nằm trong **một** `SaveChanges` (một transaction): lỗi ở bất kỳ phần nào → không phần nào được lưu | `SaveAsync` |

## 4. Kết quả kiểm thử lại

Script [demo/verify-week06.sh](demo/verify-week06.sh) chạy trên database kiểm thử riêng `qtkd_verify` (tạo mới, chạy cả 2 migration từ đầu),
kiểm tra HTTP status, nội dung trả về và dữ liệu trong PostgreSQL (kể cả "dữ liệu không đổi" khi bị từ chối):
**106 PASS / 0 FAIL** — [demo/verify-week06-output.txt](demo/verify-week06-output.txt). Chạy lại lần 2 trên cùng DB: 100 PASS / 0 FAIL
(phần HD-2026-018 tự bỏ qua vì hợp đồng đã được gửi ở lần 1).

| TC (tuần 07) | Kiểm tra tương ứng trong script | Kết quả |
|---|---|---|
| TC01 Tạo Customer hợp lệ | Mục 1: 201 + đọc lại từ DB | Pass |
| TC02 Customer thiếu thông tin | Mục 1: 400 + danh sách lỗi, không tạo bản ghi | Pass |
| TC03 Sửa Contract ở Draft | Mục 3: 200 + DB đã đổi | Pass |
| TC04 Sửa Contract ở PendingApproval | Mục 3: 409 `CTR01_NOT_EDITABLE`, snapshot DB không đổi | Pass |
| TC05 Submit thiếu điều kiện CTR-02 | Mục 4: thiếu **từng** điều kiện (nội dung, tệp, tệp sai loại, hết hiệu lực, khách hàng Inactive) → 400, vẫn Draft | Pass |
| TC06 Submit hợp lệ | Mục 5: 200, DB PendingApproval + 1 ApprovalRequest + lịch sử | Pass |
| TC07 Draft → Approved | Mục 7: 409 `CTR03_INVALID_TRANSITION`, vẫn Draft | Pass |
| TC08 Submit lặp | Mục 5–6: lặp tuần tự và đồng thời, chỉ 1 ApprovalRequest | Pass |

Lỗi phát hiện khi rà soát và đã sửa: 5 yêu cầu đồng thời **cùng** `Idempotency-Key` trước đây trả 1×200 + 4×409 (dữ liệu vẫn đúng,
chỉ 1 phiên phê duyệt) — không đúng ngữ nghĩa idempotency. Đã sửa để các yêu cầu cùng key nhận lại kết quả lần gửi thành công (5×200).

## 5. Cách mô phỏng việc gửi và giới hạn của phiên bản hiện tại

**Mô phỏng:** chưa có phân hệ Approval. "Khởi tạo quy trình phê duyệt" = ghi một dòng vào bảng `ApprovalRequests` với `Status = Pending`.
`approvalId` trả về chính là `ApprovalRequests.Id`. Endpoint `POST /api/contracts/{id}/approve` chỉ để kiểm tra CTR-03, không phải chức năng phê duyệt thật.

### Giới hạn của phiên bản hiện tại

1. **Chưa có xác thực/phân quyền** (AC01, Q1 chưa chốt): không trả 403; người thực hiện lấy từ header `X-User` chỉ để ghi lịch sử.
2. **Tệp đính kèm chỉ là metadata** (tên tệp, loại tài liệu), chưa upload/lưu nội dung tệp; chưa có API xóa tệp.
3. **Danh sách nội dung/tệp bắt buộc là giả định** (mục 1), chưa phân theo loại hợp đồng.
4. **Chưa có quy trình phê duyệt nhiều bước**, chưa hiển thị người duyệt kế tiếp (vấn đề mở Q4).
5. **Idempotency-Key gắn với hợp đồng**, không lưu thời hạn hết hạn; gửi cùng key nhưng body khác vẫn trả kết quả cũ.
6. Chưa có luồng RevisionRequested → Draft (thuộc User Story khác của quy trình phê duyệt).
