# Tài liệu API — QTKD (Tuần 06)

Base URL khi chạy cục bộ: `http://localhost:5084`. Swagger UI: `/swagger`.
Mọi request/response dùng JSON; ngày dạng `yyyy-MM-dd`.

## Quy ước chung

**Header tùy chọn**

| Header | Ý nghĩa |
|---|---|
| `X-User` | Tên người thực hiện, ghi vào lịch sử hợp đồng. Chưa có xác thực nên mặc định `anonymous`. |
| `Idempotency-Key` | Chỉ dùng cho `POST /api/contracts/{id}/submit`; gửi lặp cùng key trả lại kết quả lần trước. |

**Mẫu phản hồi lỗi (thống nhất cho mọi API)**

```json
{
  "code": "MA_LOI",
  "message": "Mô tả lỗi cho người dùng",
  "errors": [ { "field": "tenTruong", "code": "MA_LOI_TRUONG", "message": "..." } ]
}
```

| HTTP | Khi nào | `code` |
|---|---|---|
| 400 | Thiếu/sai dữ liệu đầu vào | `VALIDATION_FAILED`, `INVALID_REQUEST` (JSON sai định dạng) |
| 400 | Không đạt CTR-02 khi gửi trình duyệt | `CTR02_NOT_SATISFIED` — `errors[]` liệt kê điều kiện chưa đạt |
| 404 | Không tìm thấy | `CUSTOMER_NOT_FOUND`, `CONTRACT_NOT_FOUND`, `PRICELIST_NOT_FOUND` |
| 409 | Vi phạm CTR-01 (sửa khi không ở Draft/RevisionRequested) | `CTR01_NOT_EDITABLE` |
| 409 | Vi phạm CTR-03 (chuyển trạng thái không hợp lệ) | `CTR03_INVALID_TRANSITION` |
| 409 | Gửi trình duyệt sai trạng thái | `CONTRACT_ALREADY_SUBMITTED`, `INVALID_STATUS` |
| 409 | Hai yêu cầu cùng sửa/gửi một hợp đồng | `CONCURRENT_UPDATE` |
| 409 | Trùng dữ liệu duy nhất | `CUSTOMER_TAXCODE_EXISTS`, `CONTRACT_NO_EXISTS`, `PRICEITEM_EXISTS` |

403 (không có quyền — AC01) chưa áp dụng vì phiên bản này chưa có xác thực (xem [us03-traceability.md](us03-traceability.md#giới-hạn-của-phiên-bản-hiện-tại)).

## Danh sách endpoint

| Phương thức | Đường dẫn | Chức năng | Thành công |
|---|---|---|---|
| GET | `/api/customers` | Danh sách khách hàng | 200 |
| GET | `/api/customers/{id}` | Chi tiết khách hàng | 200 |
| POST | `/api/customers` | Tạo khách hàng | 201 |
| PUT | `/api/customers/{id}` | Cập nhật khách hàng | 200 |
| GET | `/api/contracts` | Danh sách hợp đồng | 200 |
| GET | `/api/contracts/{id}` | Chi tiết hợp đồng + điều kiện gửi trình duyệt | 200 |
| POST | `/api/contracts` | Tạo hợp đồng (luôn ở Draft) | 201 |
| PUT | `/api/contracts/{id}` | Cập nhật hợp đồng (CTR-01) | 200 |
| POST | `/api/contracts/{id}/attachments` | Thêm tệp đính kèm (CTR-01) | 201 |
| POST | `/api/contracts/{id}/submit` | **US03** — Gửi trình duyệt | 200 |
| POST | `/api/contracts/{id}/approve` | Mô phỏng phê duyệt (để kiểm tra CTR-03) | 200 |
| GET | `/api/contracts/{id}/history` | Lịch sử xử lý hợp đồng | 200 |
| GET | `/api/pricelists` | Danh sách bảng giá | 200 |
| GET | `/api/pricelists/{id}` | Chi tiết bảng giá kèm PriceItem | 200 |
| POST | `/api/pricelists` | Tạo bảng giá (có thể kèm items) | 201 |
| GET | `/api/pricelists/{id}/items` | Danh sách PriceItem của bảng giá | 200 |
| POST | `/api/pricelists/{id}/items` | Thêm PriceItem | 201 |

---

## Customer

Quy tắc kiểm tra: `name` bắt buộc (≤ 200 ký tự); `taxCode` bắt buộc, dạng `0123456789` hoặc `0123456789-001`, không trùng;
`address` ≤ 500; `status` ∈ {`Active`, `Inactive`}, mặc định `Active`.

### POST /api/customers

```http
POST /api/customers
Content-Type: application/json

{ "name": "Công ty TNHH Minh Phát Logistics", "taxCode": "0393599867", "address": "Q.4, TP.HCM" }
```

```http
HTTP/1.1 201 Created
Location: /api/customers/d13b88a8-a754-4d58-89bc-ac87ac906995

{ "id": "d13b88a8-a754-4d58-89bc-ac87ac906995", "name": "Công ty TNHH Minh Phát Logistics",
  "taxCode": "0393599867", "address": "Q.4, TP.HCM", "status": "Active" }
```

Thiếu thông tin bắt buộc:

```http
POST /api/customers
{ "name": "", "taxCode": "12AB" }

HTTP/1.1 400 Bad Request
{ "code": "VALIDATION_FAILED", "message": "Dữ liệu không hợp lệ.",
  "errors": [
    { "field": "name", "code": "REQUIRED", "message": "Tên khách hàng là bắt buộc." },
    { "field": "taxCode", "code": "INVALID_FORMAT", "message": "Mã số thuế gồm 10 chữ số hoặc 10 chữ số kèm '-' và 3 chữ số." } ] }
```

### PUT /api/customers/{id}

Body giống POST (thay toàn bộ). Ví dụ ngừng hoạt động khách hàng:
`{ "name": "Công ty Đông Á", "taxCode": "0309876543", "status": "Inactive" }` → 200.
Sau đó hợp đồng mới không tham chiếu được khách hàng này (`customerId` / `CUSTOMER_INACTIVE`) và hợp đồng cũ không gửi trình duyệt được (CTR-02 `CUSTOMER_VALID`).

---

## Contract

Quy tắc kiểm tra khi tạo/sửa: `contractNo` bắt buộc, ≤ 50, không trùng; `customerId` bắt buộc, phải tồn tại và `Active`;
`startDate`, `endDate` bắt buộc, `endDate > startDate`; `serviceGroup` ≤ 100; `content` ≤ 4000.
Trạng thái **không** nhận từ request: tạo mới luôn là `Draft`, chỉ đổi qua `submit`/`approve`.

### POST /api/contracts

```http
POST /api/contracts
X-User: nv.kinhdoanh
Content-Type: application/json

{ "contractNo": "HD-2026-99867", "customerId": "d13b88a8-a754-4d58-89bc-ac87ac906995",
  "startDate": "2026-12-01", "endDate": "2027-11-30", "serviceGroup": "Dịch vụ cảng" }
```

```http
HTTP/1.1 201 Created
{
  "id": "c062c605-f09c-4967-8b2f-1789389fca09",
  "contractNo": "HD-2026-99867",
  "customerId": "d13b88a8-a754-4d58-89bc-ac87ac906995",
  "customerName": "Công ty TNHH Minh Phát Logistics",
  "startDate": "2026-12-01",
  "endDate": "2027-11-30",
  "status": "Draft",
  "serviceGroup": "Dịch vụ cảng",
  "content": null,
  "updatedAt": "2026-11-02T06:11:07.779099+00:00",
  "attachments": [],
  "submitCheck": {
    "canSubmit": false,
    "conditions": [
      { "code": "CUSTOMER_VALID", "description": "Khách hàng hợp lệ", "passed": true, "detail": null },
      { "code": "EFFECTIVE_PERIOD_VALID", "description": "Thời gian hiệu lực hợp lệ", "passed": true, "detail": null },
      { "code": "REQUIRED_CONTENT", "description": "Đủ nội dung bắt buộc", "passed": false,
        "detail": "Thiếu nhóm dịch vụ (serviceGroup) hoặc nội dung hợp đồng (content)." },
      { "code": "REQUIRED_FILES", "description": "Đủ tệp bắt buộc", "passed": false,
        "detail": "Thiếu tệp loại DuThaoHopDong (bản dự thảo hợp đồng)." }
    ]
  }
}
```

`submitCheck` đáp ứng yêu cầu đã xác nhận ở Prototype: hiển thị trạng thái và điều kiện gửi ngay trên màn hình chi tiết.
`GET /api/contracts/{id}` trả về cùng cấu trúc.

Ngày hiệu lực sai → 400 `VALIDATION_FAILED`, `errors[0] = { "field": "endDate", "code": "INVALID_PERIOD" }`.

### PUT /api/contracts/{id}

Body giống POST. Thành công ở Draft → 200 (trả `ContractDetail`). Khi hợp đồng đang `PendingApproval`:

```http
HTTP/1.1 409 Conflict
{ "code": "CTR01_NOT_EDITABLE",
  "message": "Chỉ được sửa hợp đồng ở trạng thái Draft hoặc RevisionRequested (hiện tại: PendingApproval).",
  "errors": [] }
```

### POST /api/contracts/{id}/attachments

Chỉ lưu thông tin tệp (chưa upload nội dung). `documentType` ∈ {`DuThaoHopDong`, `PhuLuc`, `Khac`}. Áp dụng CTR-01.

```http
POST /api/contracts/c062c605-.../attachments
{ "fileName": "HD-2026-99867-du-thao.pdf", "documentType": "DuThaoHopDong" }

HTTP/1.1 201 Created
{ "id": "6a2b3f3b-7f2b-476e-889e-e3f5f993dfcc", "fileName": "HD-2026-99867-du-thao.pdf",
  "documentType": "DuThaoHopDong", "uploadedAt": "2026-11-02T06:11:07.967424+00:00" }
```

### POST /api/contracts/{id}/submit — US03

```http
POST /api/contracts/c062c605-f09c-4967-8b2f-1789389fca09/submit
X-User: nv.kinhdoanh
Idempotency-Key: submit-1793599867      (tùy chọn)
Content-Type: application/json

{ "comment": "Đã bổ sung đủ hồ sơ" }    (body tùy chọn)
```

Thành công:

```http
HTTP/1.1 200 OK
{ "contractId": "c062c605-f09c-4967-8b2f-1789389fca09", "contractNo": "HD-2026-99867",
  "status": "PendingApproval", "approvalId": "0dec0999-ee00-4931-8522-5bacf92936d2",
  "submittedAt": "2026-11-02T06:11:07.997902+00:00", "replayed": false }
```

Các phản hồi lỗi:

| Tình huống | HTTP | Phản hồi |
|---|---|---|
| Không đạt CTR-02 | 400 | `CTR02_NOT_SATISFIED`, `errors[].field` là mã điều kiện chưa đạt (`CUSTOMER_VALID`, `EFFECTIVE_PERIOD_VALID`, `REQUIRED_CONTENT`, `REQUIRED_FILES`) |
| Hợp đồng không tồn tại | 404 | `CONTRACT_NOT_FOUND` |
| Đã gửi (đang `PendingApproval`), không có key hoặc key khác | 409 | `CONTRACT_ALREADY_SUBMITTED` |
| Trạng thái khác Draft (Approved, Rejected, ...) | 409 | `INVALID_STATUS` |
| Hai yêu cầu gửi cùng lúc, yêu cầu đến sau | 409 | `CONCURRENT_UPDATE` |
| Gửi lặp **cùng** `Idempotency-Key` | 200 | Kết quả lần đầu, `replayed: true`, cùng `approvalId` |

Ví dụ không đạt CTR-02:

```json
{ "code": "CTR02_NOT_SATISFIED", "message": "Hợp đồng chưa đủ điều kiện gửi trình duyệt.",
  "errors": [
    { "field": "REQUIRED_CONTENT", "code": "NOT_SATISFIED", "message": "Thiếu nhóm dịch vụ (serviceGroup) hoặc nội dung hợp đồng (content)." },
    { "field": "REQUIRED_FILES", "code": "NOT_SATISFIED", "message": "Thiếu tệp loại DuThaoHopDong (bản dự thảo hợp đồng)." } ] }
```

### POST /api/contracts/{id}/approve (mô phỏng)

Chỉ để minh họa CTR-03. Từ `PendingApproval` → `Approved` (200). Từ `Draft`:

```json
{ "code": "CTR03_INVALID_TRANSITION",
  "message": "Không được chuyển hợp đồng từ Draft sang Approved; hợp đồng phải được gửi trình duyệt (PendingApproval) trước.",
  "errors": [] }
```

### GET /api/contracts/{id}/history

```json
[
  { "action": "Created", "fromStatus": null, "toStatus": "Draft", "performedBy": "nv.kinhdoanh", "note": null, "createdAt": "..." },
  { "action": "Updated", "fromStatus": null, "toStatus": null, "performedBy": "nv.kinhdoanh", "note": null, "createdAt": "..." },
  { "action": "AttachmentAdded", "fromStatus": null, "toStatus": null, "performedBy": "nv.kinhdoanh", "note": "DuThaoHopDong: HD-2026-99867-du-thao.pdf", "createdAt": "..." },
  { "action": "Submitted", "fromStatus": "Draft", "toStatus": "PendingApproval", "performedBy": "nv.kinhdoanh", "note": "Đã bổ sung đủ hồ sơ", "createdAt": "..." }
]
```

---

## Pricing

Quy tắc kiểm tra: `name` bắt buộc; `effectiveDate` bắt buộc; `serviceCode` bắt buộc, chỉ chữ in hoa/số/`-`, ≤ 50;
`unitPrice` bắt buộc, > 0, tối đa 2 chữ số thập phân; mỗi `serviceCode` chỉ xuất hiện một lần trong một bảng giá.
PriceItem luôn gắn với PriceList qua `{id}` trên URL (khóa chính `PriceListId + ServiceCode`).

```http
POST /api/pricelists
{ "name": "Bảng giá dịch vụ cảng 2027", "effectiveDate": "2027-01-01",
  "items": [ { "serviceCode": "CANG-BOC-XEP", "unitPrice": 160000 } ] }

HTTP/1.1 201 Created
{ "id": "65545656-b6d5-4db7-a6f3-1fa489f5c195", "name": "Bảng giá dịch vụ cảng 2027",
  "effectiveDate": "2027-01-01", "items": [ { "serviceCode": "CANG-BOC-XEP", "unitPrice": 160000 } ] }
```

```http
POST /api/pricelists/65545656-.../items
{ "serviceCode": "CANG-LUU-BAI", "unitPrice": 48000 }        → 201 { "serviceCode": "CANG-LUU-BAI", "unitPrice": 48000 }

{ "serviceCode": "CANG-CAN-XE", "unitPrice": -5 }            → 400 unitPrice / MUST_BE_POSITIVE
{ "serviceCode": "CANG-LUU-BAI", "unitPrice": 50000 }        → 409 PRICEITEM_EXISTS
POST /api/pricelists/<id không tồn tại>/items                → 404 PRICELIST_NOT_FOUND
```
