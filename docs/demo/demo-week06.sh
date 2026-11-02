#!/usr/bin/env bash
# Demo tuần 06: Customer -> Contract Draft -> Submit thiếu dữ liệu (thất bại) -> bổ sung -> Submit (thành công) -> xem DB
# Yêu cầu: API đang chạy (mặc định http://localhost:5084), có curl và jq.
# Chạy lại nhiều lần được: mỗi lần tạo mã số thuế / số hợp đồng mới.
set -euo pipefail

API="${API:-http://localhost:5084}"
RUN=$(date +%s)
TAX="03${RUN: -8}"
CONTRACT_NO="HD-2026-${RUN: -5}"

step() { printf '\n\033[1;34m=== %s\033[0m\n' "$*"; }
# call METHOD PATH [BODY] [extra curl args...] — in HTTP status + body, lưu body vào $BODY
call() {
  local method=$1 path=$2 body=${3:-}; shift 3 || shift $#
  local args=(-s -o /tmp/qtkd_body.json -w '%{http_code}' -X "$method" "$API$path" -H 'X-User: nv.kinhdoanh')
  [[ -n "$body" ]] && args+=(-H 'Content-Type: application/json' -d "$body")
  STATUS=$(curl "${args[@]}" "$@")
  BODY=$(cat /tmp/qtkd_body.json)
  echo "> $method $path"
  [[ -n "$body" ]] && echo "$body" | jq -c .
  echo "< HTTP $STATUS"
  echo "$BODY" | jq . 2>/dev/null || echo "$BODY"
}

step "1a. Tạo Customer thiếu thông tin bắt buộc -> 400"
call POST /api/customers '{"name":"","taxCode":"12AB"}'

step "1b. Tạo Customer hợp lệ -> 201"
call POST /api/customers "{\"name\":\"Công ty TNHH Minh Phát Logistics\",\"taxCode\":\"$TAX\",\"address\":\"Q.4, TP.HCM\"}"
CUSTOMER_ID=$(echo "$BODY" | jq -r .id)

step "2a. Tạo Contract với ngày hiệu lực sai (kết thúc trước bắt đầu) -> 400"
call POST /api/contracts "{\"contractNo\":\"$CONTRACT_NO\",\"customerId\":\"$CUSTOMER_ID\",\"startDate\":\"2026-12-01\",\"endDate\":\"2026-11-01\"}"

step "2b. Tạo Contract Draft (chưa có nội dung, chưa có tệp) -> 201"
call POST /api/contracts "{\"contractNo\":\"$CONTRACT_NO\",\"customerId\":\"$CUSTOMER_ID\",\"startDate\":\"2026-12-01\",\"endDate\":\"2027-11-30\",\"serviceGroup\":\"Dịch vụ cảng\"}"
CONTRACT_ID=$(echo "$BODY" | jq -r .id)

step "3. Gửi trình duyệt khi thiếu dữ liệu -> 400 CTR02_NOT_SATISFIED, vẫn Draft"
call POST "/api/contracts/$CONTRACT_ID/submit" '{"comment":"Gửi lần 1"}'

step "4. Thử chuyển thẳng Draft -> Approved -> 409 CTR03_INVALID_TRANSITION"
call POST "/api/contracts/$CONTRACT_ID/approve" ''

step "5a. Bổ sung nội dung hợp đồng (Draft -> sửa được theo CTR-01) -> 200"
call PUT "/api/contracts/$CONTRACT_ID" "{\"contractNo\":\"$CONTRACT_NO\",\"customerId\":\"$CUSTOMER_ID\",\"startDate\":\"2026-12-01\",\"endDate\":\"2027-11-30\",\"serviceGroup\":\"Dịch vụ cảng\",\"content\":\"Cung cấp dịch vụ bốc xếp và lưu bãi container theo bảng giá dịch vụ cảng 2026.\"}"

step "5b. Bổ sung tệp bắt buộc (bản dự thảo hợp đồng) -> 201"
call POST "/api/contracts/$CONTRACT_ID/attachments" "{\"fileName\":\"$CONTRACT_NO-du-thao.pdf\",\"documentType\":\"DuThaoHopDong\"}"

step "6. Gửi lại (có Idempotency-Key) -> 200 PendingApproval"
KEY="submit-$RUN"
call POST "/api/contracts/$CONTRACT_ID/submit" '{"comment":"Đã bổ sung đủ hồ sơ"}' -H "Idempotency-Key: $KEY"
APPROVAL_ID=$(echo "$BODY" | jq -r .approvalId)

step "7a. Gửi lặp cùng Idempotency-Key -> 200, replayed=true, cùng approvalId"
call POST "/api/contracts/$CONTRACT_ID/submit" '{"comment":"Đã bổ sung đủ hồ sơ"}' -H "Idempotency-Key: $KEY"

step "7b. Gửi lặp không có key -> 409 CONTRACT_ALREADY_SUBMITTED"
call POST "/api/contracts/$CONTRACT_ID/submit" ''

step "8. Sửa Contract khi đang PendingApproval -> 409 CTR01_NOT_EDITABLE"
call PUT "/api/contracts/$CONTRACT_ID" "{\"contractNo\":\"$CONTRACT_NO\",\"customerId\":\"$CUSTOMER_ID\",\"startDate\":\"2026-12-01\",\"endDate\":\"2028-11-30\",\"serviceGroup\":\"Dịch vụ cảng\",\"content\":\"Sửa sau khi gửi\"}"

step "9. Xem lại hợp đồng và lịch sử xử lý"
call GET "/api/contracts/$CONTRACT_ID" ''
call GET "/api/contracts/$CONTRACT_ID/history" ''

step "10. Đối chiếu trong PostgreSQL"
if command -v docker >/dev/null && docker ps --format '{{.Names}}' | grep -q '^qtkd-postgres$'; then
  docker exec qtkd-postgres psql -U postgres -d qtkd -c \
    "SELECT \"ContractNo\", \"Status\", \"ServiceGroup\", \"UpdatedAt\" FROM \"Contracts\" WHERE \"Id\" = '$CONTRACT_ID';" -c \
    "SELECT \"Id\", \"Status\", \"IdempotencyKey\", \"RequestedBy\" FROM \"ApprovalRequests\" WHERE \"ContractId\" = '$CONTRACT_ID';" -c \
    "SELECT \"Action\", \"FromStatus\", \"ToStatus\", \"PerformedBy\", \"CreatedAt\" FROM \"ContractHistory\" WHERE \"ContractId\" = '$CONTRACT_ID' ORDER BY \"CreatedAt\";"
else
  echo "(Không thấy container qtkd-postgres — tự chạy các câu SELECT trong README)"
fi
echo "approvalId lần gửi thành công: $APPROVAL_ID"

step "11. AC05 — 5 yêu cầu gửi đồng thời trên một hợp đồng hợp lệ mới: chỉ 1 thành công"
call POST /api/contracts "{\"contractNo\":\"$CONTRACT_NO-C\",\"customerId\":\"$CUSTOMER_ID\",\"startDate\":\"2026-12-01\",\"endDate\":\"2027-11-30\",\"serviceGroup\":\"Dịch vụ cảng\",\"content\":\"Hợp đồng kiểm tra gửi đồng thời\"}" >/dev/null
C2=$(echo "$BODY" | jq -r .id)
call POST "/api/contracts/$C2/attachments" '{"fileName":"du-thao.pdf","documentType":"DuThaoHopDong"}' >/dev/null
for i in 1 2 3 4 5; do
  curl -s -o /dev/null -w '%{http_code}\n' -X POST "$API/api/contracts/$C2/submit" &
done | sort | uniq -c
wait
curl -s "$API/api/contracts/$C2" | jq '{contractNo, status}'

step "12. Pricing: tạo PriceList, thêm PriceItem, đơn giá sai, mã trùng"
call POST /api/pricelists "{\"name\":\"Bảng giá demo $RUN\",\"effectiveDate\":\"2027-01-01\",\"items\":[{\"serviceCode\":\"CANG-BOC-XEP\",\"unitPrice\":160000}]}"
PL=$(echo "$BODY" | jq -r .id)
call POST "/api/pricelists/$PL/items" '{"serviceCode":"CANG-LUU-BAI","unitPrice":48000}'
call POST "/api/pricelists/$PL/items" '{"serviceCode":"CANG-CAN-XE","unitPrice":-5}'
call POST "/api/pricelists/$PL/items" '{"serviceCode":"CANG-LUU-BAI","unitPrice":50000}'
call GET "/api/pricelists/$PL/items" ''
