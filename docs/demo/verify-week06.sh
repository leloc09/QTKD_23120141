#!/usr/bin/env bash
# Kiểm thử lại yêu cầu tuần 06 bằng API + đối chiếu dữ liệu PostgreSQL. Mỗi kiểm tra in PASS/FAIL.
# Không chỉ xem HTTP status: kiểm tra cả body và dữ liệu trong DB (đặc biệt "dữ liệu không đổi" khi bị từ chối).
# Dùng: API=http://localhost:5085 DB=qtkd_verify ./docs/demo/verify-week06.sh   (nên dùng database kiểm thử riêng)
set -uo pipefail

API="${API:-http://localhost:5084}"
DB="${DB:-qtkd}"
PG_CONTAINER="${PG_CONTAINER:-qtkd-postgres}"
RUN=$(date +%s)
PASS=0; FAIL=0; FAILED=()
TMP=$(mktemp)

section() { printf '\n## %s\n' "$*"; }
ok()   { PASS=$((PASS+1)); printf '  PASS  %s\n' "$1"; }
ko()   { FAIL=$((FAIL+1)); FAILED+=("$1"); printf '  FAIL  %s\n        %s\n' "$1" "$2"; }

# req METHOD PATH [BODY] [curl args...] -> $STATUS, $BODY
req() {
  local method=$1 path=$2 body=${3:-}; shift 3 2>/dev/null || shift $#
  local args=(-s -o "$TMP" -w '%{http_code}' -X "$method" "$API$path" -H 'X-User: tester')
  [[ -n "$body" ]] && args+=(-H 'Content-Type: application/json' -d "$body")
  STATUS=$(curl "${args[@]}" "$@"); BODY=$(cat "$TMP")
}
db() { docker exec "$PG_CONTAINER" psql -U postgres -d "$DB" -tAc "$1"; }

# check "mô tả" <status mong đợi> [jq-expr giá trị-mong-đợi]...
check() {
  local name=$1 want=$2; shift 2
  if [[ "$STATUS" != "$want" ]]; then ko "$name" "HTTP $STATUS (mong đợi $want): $BODY"; return; fi
  while (($#)); do
    local got; got=$(echo "$BODY" | jq -r "$1" 2>/dev/null)
    if [[ "$got" != "$2" ]]; then ko "$name" "$1 = '$got' (mong đợi '$2')"; return; fi
    shift 2
  done
  ok "$name"
}
check_db() { local name=$1 sql=$2 want=$3 got; got=$(db "$sql"); [[ "$got" == "$want" ]] && ok "$name" || ko "$name" "DB: '$got' (mong đợi '$want') — $sql"; }

contract_body() { # contractNo customerId start end [serviceGroup] [content]
  jq -cn --arg no "$1" --arg c "$2" --arg s "$3" --arg e "$4" --arg g "${5:-}" --arg t "${6:-}" \
    '{contractNo:$no, customerId:$c, startDate:$s, endDate:$e} + (if $g=="" then {} else {serviceGroup:$g} end) + (if $t=="" then {} else {content:$t} end)'
}
new_customer() { req POST /api/customers "{\"name\":\"KH $1 $RUN\",\"taxCode\":\"$2\"}"; echo "$BODY" | jq -r .id; }
# Tạo hợp đồng Draft đủ điều kiện CTR-02 (trừ khi truyền cờ bỏ bớt)
new_contract() { # no customerId [nocontent] [nofile] [start] [end]
  req POST /api/contracts "$(contract_body "$1" "$2" "${5:-2026-12-01}" "${6:-2027-11-30}" "Dịch vụ cảng" "$([[ "${3:-}" == nocontent ]] || echo 'Nội dung hợp đồng')")"
  local id; id=$(echo "$BODY" | jq -r .id)
  [[ "${4:-}" == nofile ]] || req POST "/api/contracts/$id/attachments" '{"fileName":"du-thao.pdf","documentType":"DuThaoHopDong"}'
  echo "$id"
}
# Ảnh chụp trạng thái hợp đồng trong DB để chứng minh "dữ liệu không đổi"
snapshot() { db "SELECT c.\"Status\"||'|'||coalesce(c.\"Content\",'')||'|'||c.\"EndDate\"||'|'||c.\"UpdatedAt\"||'|'||(SELECT count(*) FROM \"ApprovalRequests\" a WHERE a.\"ContractId\"=c.\"Id\")||'|'||(SELECT count(*) FROM \"ContractHistory\" h WHERE h.\"ContractId\"=c.\"Id\")||'|'||(SELECT count(*) FROM \"ContractAttachments\" t WHERE t.\"ContractId\"=c.\"Id\") FROM \"Contracts\" c WHERE c.\"Id\"='$1'"; }
check_unchanged() { local now; now=$(snapshot "$2"); [[ "$now" == "$3" ]] && ok "$1" || ko "$1" "trước: $3 / sau: $now"; }

T1="04${RUN: -8}"; T2="05${RUN: -8}"; T3="06${RUN: -8}"

section "0. Nền tảng tuần 05 + Swagger"
req GET /api/customers;                                         check "GET /api/customers" 200
req GET /api/contracts/44444444-4444-4444-4444-444444444444;    check "GET /api/contracts/{id} (seed HD-2026-018)" 200 .contractNo HD-2026-018
req GET /swagger/v1/swagger.json;                               check "Swagger có endpoint submit" 200 '.paths | has("/api/contracts/{id}/submit")' true

section "1. Customer — tạo / xem / cập nhật, trường bắt buộc"
before=$(db 'SELECT count(*) FROM "Customers"')
req POST /api/customers '{"taxCode":"0101010101"}';             check "Thiếu tên -> 400" 400 .code VALIDATION_FAILED '.errors[0].field' name
req POST /api/customers '{"name":"Công ty A"}';                 check "Thiếu mã số thuế -> 400" 400 '.errors[0].field' taxCode
req POST /api/customers '{"name":"   ","taxCode":"abc"}';       check "Tên toàn khoảng trắng + MST sai -> 400, 2 lỗi" 400 '.errors|length' 2
req POST /api/customers '{"name":"X","taxCode":"0101010101","status":"Deleted"}'; check "Status không hợp lệ -> 400" 400 '.errors[0].field' status
check_db "Các request lỗi không tạo Customer" 'SELECT count(*) FROM "Customers"' "$before"
req POST /api/customers "{\"name\":\"Công ty Kiểm Thử $RUN\",\"taxCode\":\"$T1\",\"address\":\"HCM\"}"
check "Tạo Customer hợp lệ -> 201" 201 .status Active .taxCode "$T1"
CUS=$(echo "$BODY" | jq -r .id)
check_db "Customer đọc lại được từ DB" "SELECT \"Name\" FROM \"Customers\" WHERE \"Id\"='$CUS'" "Công ty Kiểm Thử $RUN"
req POST /api/customers "{\"name\":\"Trùng\",\"taxCode\":\"$T1\"}"; check "Trùng mã số thuế -> 409" 409 .code CUSTOMER_TAXCODE_EXISTS
req GET "/api/customers/$CUS";                                  check "Xem chi tiết Customer" 200 .id "$CUS"
req GET /api/customers;                                         check "Danh sách có Customer mới" 200 "[.[]|select(.id==\"$CUS\")]|length" 1
req GET /api/customers/00000000-0000-0000-0000-000000000000;    check "Customer không tồn tại -> 404" 404 .code CUSTOMER_NOT_FOUND
req PUT "/api/customers/$CUS" "{\"name\":\"Công ty Kiểm Thử Mới $RUN\",\"taxCode\":\"$T1\"}"; check "Cập nhật Customer -> 200" 200
check_db "Tên mới đã lưu DB" "SELECT \"Name\" FROM \"Customers\" WHERE \"Id\"='$CUS'" "Công ty Kiểm Thử Mới $RUN"
req PUT "/api/customers/$CUS" "{\"name\":\"\",\"taxCode\":\"$T1\"}"; check "Cập nhật thiếu tên -> 400" 400
check_db "Cập nhật lỗi không đổi DB" "SELECT \"Name\" FROM \"Customers\" WHERE \"Id\"='$CUS'" "Công ty Kiểm Thử Mới $RUN"

section "2. Contract — tạo / xem / cập nhật, ngày hiệu lực, Customer tham chiếu"
INACTIVE=$(new_customer inactive "$T2")
req PUT "/api/customers/$INACTIVE" "{\"name\":\"KH inactive\",\"taxCode\":\"$T2\",\"status\":\"Inactive\"}"
before=$(db 'SELECT count(*) FROM "Contracts"')
req POST /api/contracts '{}';                                   check "Thiếu mọi trường -> 400, 4 lỗi" 400 '[.errors[].field]|sort|join(",")' "contractNo,customerId,endDate,startDate"
req POST /api/contracts "$(contract_body "HD-V-$RUN-0" 00000000-0000-0000-0000-000000000000 2026-12-01 2027-01-01)"; check "Customer không tồn tại -> 400" 400 '.errors[0].code' NOT_FOUND
req POST /api/contracts "$(contract_body "HD-V-$RUN-0" "$INACTIVE" 2026-12-01 2027-01-01)"; check "Customer Inactive -> 400" 400 '.errors[0].code' CUSTOMER_INACTIVE
req POST /api/contracts "$(contract_body "HD-V-$RUN-0" "$CUS" 2027-01-01 2026-12-01)"; check "Ngày kết thúc trước ngày bắt đầu -> 400" 400 '.errors[0].code' INVALID_PERIOD
req POST /api/contracts "$(contract_body "HD-V-$RUN-0" "$CUS" 2027-01-01 2027-01-01)"; check "Ngày kết thúc = ngày bắt đầu -> 400" 400 '.errors[0].code' INVALID_PERIOD
req POST /api/contracts '{"contractNo":"X","customerId":"abc","startDate":"2026-13-45"}'; check "JSON sai kiểu -> 400 INVALID_REQUEST" 400 .code INVALID_REQUEST
check_db "Các request lỗi không tạo Contract" 'SELECT count(*) FROM "Contracts"' "$before"
req POST /api/contracts "$(contract_body "HD-V-$RUN-1" "$CUS" 2026-12-01 2027-11-30 | jq -c '. + {status:"Approved"}')"
check "Tạo Contract -> 201, luôn Draft dù gửi status=Approved" 201 .status Draft .submitCheck.canSubmit false
K1=$(echo "$BODY" | jq -r .id)
check_db "Contract lưu DB ở Draft" "SELECT \"Status\" FROM \"Contracts\" WHERE \"Id\"='$K1'" Draft
check_db "Ghi lịch sử Created" "SELECT \"Action\"||'>'||\"ToStatus\" FROM \"ContractHistory\" WHERE \"ContractId\"='$K1'" "Created>Draft"
req POST /api/contracts "$(contract_body "HD-V-$RUN-1" "$CUS" 2026-12-01 2027-11-30)"; check "Trùng số hợp đồng -> 409" 409 .code CONTRACT_NO_EXISTS
req GET "/api/contracts/$K1";                                   check "Xem chi tiết Contract" 200 .contractNo "HD-V-$RUN-1" '.submitCheck.conditions|length' 4
req GET /api/contracts;                                         check "Danh sách Contract" 200 "[.[]|select(.id==\"$K1\")]|length" 1
req GET /api/contracts/00000000-0000-0000-0000-000000000000;    check "Contract không tồn tại -> 404" 404 .code CONTRACT_NOT_FOUND
req POST "/api/contracts/$K1/attachments" '{"fileName":"","documentType":"Abc"}'; check "Tệp đính kèm sai -> 400, 2 lỗi" 400 '.errors|length' 2

section "3. CTR-01 — chỉ sửa khi Draft / RevisionRequested (TC03, TC04)"
req PUT "/api/contracts/$K1" "$(contract_body "HD-V-$RUN-1" "$CUS" 2026-12-01 2027-12-31 "Dịch vụ cảng" "Nội dung v2" | jq -c '. + {status:"Approved"}')"
check "TC03 Sửa Contract ở Draft -> 200" 200 .content "Nội dung v2" .endDate 2027-12-31 .status Draft
check_db "TC03 Dữ liệu sửa đã lưu DB, vẫn Draft" "SELECT \"Content\"||'|'||\"EndDate\"||'|'||\"Status\" FROM \"Contracts\" WHERE \"Id\"='$K1'" "Nội dung v2|2027-12-31|Draft"
req PUT "/api/contracts/$K1" "$(contract_body "HD-V-$RUN-1" "$CUS" 2027-12-31 2026-12-01)"; check "Sửa Draft với ngày sai -> 400" 400
req POST "/api/contracts/$K1/attachments" '{"fileName":"du-thao.pdf","documentType":"DuThaoHopDong"}'; check "Thêm tệp khi Draft -> 201" 201
req POST "/api/contracts/$K1/submit";                           check "Gửi K1 (đủ điều kiện) -> 200" 200 .status PendingApproval
snap=$(snapshot "$K1")
req PUT "/api/contracts/$K1" "$(contract_body "HD-V-$RUN-1" "$CUS" 2026-12-01 2028-12-31 "Dịch vụ cảng" "Sửa trái phép")"
check "TC04 Sửa Contract ở PendingApproval -> 409" 409 .code CTR01_NOT_EDITABLE
check_unchanged "TC04 Dữ liệu giữ nguyên" "$K1" "$snap"
req POST "/api/contracts/$K1/attachments" '{"fileName":"x.pdf","documentType":"Khac"}'; check "Thêm tệp khi PendingApproval -> 409" 409 .code CTR01_NOT_EDITABLE
check_unchanged "Thêm tệp bị từ chối, dữ liệu giữ nguyên" "$K1" "$snap"

section "4. CTR-02 — thiếu từng điều kiện: gửi thất bại, dữ liệu không đổi (TC05)"
ctr02_case() { # tên id mã-điều-kiện-mong-đợi
  local s; s=$(snapshot "$2")
  req POST "/api/contracts/$2/submit" '{"comment":"thử"}'
  check "TC05 $1 -> 400 chỉ báo $3" 400 .code CTR02_NOT_SATISFIED '[.errors[].field]|join(",")' "$3"
  check_unchanged "TC05 $1 -> vẫn Draft, không tạo approval/lịch sử" "$2" "$s"
}
ctr02_case "Thiếu nội dung"        "$(new_contract "HD-V-$RUN-2" "$CUS" nocontent)" REQUIRED_CONTENT
ctr02_case "Thiếu tệp bắt buộc"    "$(new_contract "HD-V-$RUN-3" "$CUS" "" nofile)" REQUIRED_FILES
K4=$(new_contract "HD-V-$RUN-4" "$CUS" "" nofile)
req POST "/api/contracts/$K4/attachments" '{"fileName":"phu-luc.pdf","documentType":"PhuLuc"}'
ctr02_case "Chỉ có tệp sai loại"   "$K4" REQUIRED_FILES
ctr02_case "Hết hiệu lực (2025)"   "$(new_contract "HD-V-$RUN-5" "$CUS" "" "" 2025-01-01 2025-12-31)" EFFECTIVE_PERIOD_VALID
CUS3=$(new_customer later-inactive "$T3")
K6=$(new_contract "HD-V-$RUN-6" "$CUS3")
req PUT "/api/customers/$CUS3" "{\"name\":\"KH later inactive\",\"taxCode\":\"$T3\",\"status\":\"Inactive\"}"
ctr02_case "Khách hàng đã Inactive" "$K6" CUSTOMER_VALID
ctr02_case "Thiếu nội dung + tệp"  "$(new_contract "HD-V-$RUN-7" "$CUS" nocontent nofile)" "REQUIRED_CONTENT,REQUIRED_FILES"

section "5. US03 Submit thành công (TC06) và gửi lặp (TC08)"
K8=$(new_contract "HD-V-$RUN-8" "$CUS")
req GET "/api/contracts/$K8";                                   check "Trước khi gửi: canSubmit=true" 200 .submitCheck.canSubmit true
req POST "/api/contracts/$K8/submit" '{"comment":"gửi"}' -H "Idempotency-Key: k-$RUN"
check "TC06 Submit hợp lệ -> 200 PendingApproval" 200 .status PendingApproval .replayed false
AP=$(echo "$BODY" | jq -r .approvalId)
check_db "TC06 DB: Contract PendingApproval" "SELECT \"Status\" FROM \"Contracts\" WHERE \"Id\"='$K8'" PendingApproval
check_db "TC06 DB: 1 ApprovalRequest Pending đúng approvalId" "SELECT \"Id\"||'|'||\"Status\"||'|'||\"RequestedBy\" FROM \"ApprovalRequests\" WHERE \"ContractId\"='$K8'" "$AP|Pending|tester"
check_db "TC06 DB: lịch sử Submitted Draft->PendingApproval" "SELECT \"FromStatus\"||'>'||\"ToStatus\"||'|'||\"Note\" FROM \"ContractHistory\" WHERE \"ContractId\"='$K8' AND \"Action\"='Submitted'" "Draft>PendingApproval|gửi"
snap=$(snapshot "$K8")
req POST "/api/contracts/$K8/submit" '{"comment":"gửi"}' -H "Idempotency-Key: k-$RUN"
check "TC08 Gửi lặp cùng key -> 200 replayed, cùng approvalId" 200 .replayed true .approvalId "$AP"
req POST "/api/contracts/$K8/submit"
check "TC08 Gửi lặp không key -> 409" 409 .code CONTRACT_ALREADY_SUBMITTED
req POST "/api/contracts/$K8/submit" '' -H "Idempotency-Key: other-$RUN"
check "TC08 Gửi lặp key khác -> 409" 409 .code CONTRACT_ALREADY_SUBMITTED
check_unchanged "TC08 Không tạo thêm approval/lịch sử" "$K8" "$snap"
req POST /api/contracts/00000000-0000-0000-0000-000000000000/submit; check "Submit hợp đồng không tồn tại -> 404" 404

section "6. AC05 — gửi đồng thời"
K9=$(new_contract "HD-V-$RUN-9" "$CUS")
codes=$(for i in $(seq 1 10); do curl -s -o /dev/null -w '%{http_code}\n' -X POST "$API/api/contracts/$K9/submit" & done; wait)
n200=$(grep -c 200 <<<"$codes"); n409=$(grep -c 409 <<<"$codes")
[[ $n200 == 1 && $n409 == 9 ]] && ok "10 yêu cầu đồng thời -> 1x200, 9x409" || ko "10 yêu cầu đồng thời" "$(sort <<<"$codes" | uniq -c | tr '\n' ' ')"
check_db "Chỉ 1 ApprovalRequest" "SELECT count(*) FROM \"ApprovalRequests\" WHERE \"ContractId\"='$K9'" 1
check_db "Chỉ 1 lịch sử Submitted" "SELECT count(*) FROM \"ContractHistory\" WHERE \"ContractId\"='$K9' AND \"Action\"='Submitted'" 1
K10=$(new_contract "HD-V-$RUN-10" "$CUS")
codes=$(for i in $(seq 1 5); do curl -s -o /dev/null -w '%{http_code}\n' -X POST "$API/api/contracts/$K10/submit" -H "Idempotency-Key: same-$RUN" & done; wait)
check_db "5 yêu cầu đồng thời cùng key -> chỉ 1 ApprovalRequest ($(sort <<<"$codes" | uniq -c | tr '\n' ' '))" "SELECT count(*) FROM \"ApprovalRequests\" WHERE \"ContractId\"='$K10'" 1
[[ $(grep -c 200 <<<"$codes") == 5 ]] && ok "5 yêu cầu đồng thời cùng key -> đều 200 (cùng một kết quả)" || ko "5 yêu cầu đồng thời cùng key -> đều 200" "$(sort <<<"$codes" | uniq -c | tr '\n' ' ')"
check_db "Cả 5 phản hồi trỏ cùng approvalId (chỉ 1 bản ghi có key)" "SELECT count(DISTINCT \"Id\") FROM \"ApprovalRequests\" WHERE \"IdempotencyKey\"='same-$RUN'" 1
grep -q 500 <<<"$codes" && ko "Không có lỗi 500 khi đồng thời" "$codes" || ok "Không có lỗi 500 khi đồng thời"

section "7. CTR-03 — không chuyển Draft -> Approved (TC07)"
K11=$(new_contract "HD-V-$RUN-11" "$CUS")
snap=$(snapshot "$K11")
req POST "/api/contracts/$K11/approve";                         check "TC07 Approve khi Draft -> 409" 409 .code CTR03_INVALID_TRANSITION
check_unchanged "TC07 Vẫn Draft, dữ liệu không đổi" "$K11" "$snap"
req POST "/api/contracts/$K11/submit";                          check "Submit chỉ chuyển sang PendingApproval (không Approved)" 200 .status PendingApproval
req POST "/api/contracts/$K11/approve";                         check "Approve sau khi PendingApproval -> 200" 200 .status Approved
check_db "Approval chuyển Approved" "SELECT \"Status\" FROM \"ApprovalRequests\" WHERE \"ContractId\"='$K11'" Approved
req POST "/api/contracts/$K11/approve";                         check "Approve lần 2 (Approved -> Approved) -> 409" 409 .code CTR03_INVALID_TRANSITION
req POST "/api/contracts/$K11/submit";                          check "Submit hợp đồng đã Approved -> 409" 409 .code INVALID_STATUS
req PUT "/api/contracts/$K11" "$(contract_body "HD-V-$RUN-11" "$CUS" 2026-12-01 2027-11-30 "Dịch vụ cảng" "x")"; check "Sửa hợp đồng Approved -> 409" 409 .code CTR01_NOT_EDITABLE

section "8. Pricing — PriceList / PriceItem"
before=$(db 'SELECT count(*) FROM "PriceLists"')
req POST /api/pricelists '{}';                                   check "PriceList thiếu tên + ngày -> 400, 2 lỗi" 400 '.errors|length' 2
req POST /api/pricelists '{"name":"BG","effectiveDate":"2027-01-01","items":[{"serviceCode":"A1","unitPrice":1},{"serviceCode":"A1","unitPrice":2}]}'; check "Mã trùng trong request -> 400" 400 '.errors[0].code' DUPLICATE_SERVICE_CODE
req POST /api/pricelists '{"name":"BG","effectiveDate":"2027-01-01","items":[{"serviceCode":"A1","unitPrice":0}]}'; check "Item đơn giá 0 khi tạo -> 400" 400 '.errors[0].field' 'items[0].unitPrice'
check_db "Request lỗi không tạo PriceList" 'SELECT count(*) FROM "PriceLists"' "$before"
req POST /api/pricelists "{\"name\":\"BG kiểm thử $RUN\",\"effectiveDate\":\"2027-01-01\",\"items\":[{\"serviceCode\":\"CANG-BOC-XEP\",\"unitPrice\":160000.5}]}"
check "Tạo PriceList kèm item -> 201" 201 '.items|length' 1
PL=$(echo "$BODY" | jq -r .id)
req POST "/api/pricelists/$PL/items" '{"serviceCode":"CANG-LUU-BAI","unitPrice":48000}'; check "Thêm PriceItem -> 201" 201 .serviceCode CANG-LUU-BAI
check_db "PriceItem liên kết đúng PriceList" "SELECT \"PriceListId\"||'|'||\"UnitPrice\" FROM \"PriceItems\" WHERE \"PriceListId\"='$PL' AND \"ServiceCode\"='CANG-LUU-BAI'" "$PL|48000.00"
for bad in '{"serviceCode":"X1","unitPrice":0}' '{"serviceCode":"X1","unitPrice":-1}' '{"serviceCode":"X1","unitPrice":1.234}' '{"serviceCode":"X1"}' '{"serviceCode":"x y","unitPrice":1}' '{"unitPrice":1}'; do
  req POST "/api/pricelists/$PL/items" "$bad"; check "PriceItem không hợp lệ $bad -> 400" 400 .code VALIDATION_FAILED
done
req POST "/api/pricelists/$PL/items" '{"serviceCode":"X1","unitPrice":1e40}'; check "Đơn giá tràn số -> 400" 400
req POST "/api/pricelists/$PL/items" '{"serviceCode":"CANG-LUU-BAI","unitPrice":1}'; check "Trùng mã dịch vụ -> 409" 409 .code PRICEITEM_EXISTS
check_db "Sau các lỗi chỉ còn 2 PriceItem, đơn giá cũ giữ nguyên" "SELECT count(*)||'|'||max(\"UnitPrice\") FILTER (WHERE \"ServiceCode\"='CANG-LUU-BAI') FROM \"PriceItems\" WHERE \"PriceListId\"='$PL'" "2|48000.00"
req GET "/api/pricelists/$PL/items";                            check "Xem PriceItem" 200 'length' 2
req GET "/api/pricelists/$PL";                                  check "Xem PriceList" 200 .name "BG kiểm thử $RUN"
req GET /api/pricelists;                                        check "Danh sách PriceList" 200 "[.[]|select(.id==\"$PL\")]|.[0].itemCount" 2
req POST /api/pricelists/00000000-0000-0000-0000-000000000000/items '{"serviceCode":"A","unitPrice":1}'; check "Thêm item vào PriceList không tồn tại -> 404" 404 .code PRICELIST_NOT_FOUND
req GET /api/pricelists/00000000-0000-0000-0000-000000000000/items; check "Xem item PriceList không tồn tại -> 404" 404

section "9. Ví dụ xuyên suốt của đề: HD-2026-018 (Công ty Minh Phát)"
SEED=44444444-4444-4444-4444-444444444444
if [[ $(db "SELECT \"Status\" FROM \"Contracts\" WHERE \"Id\"='$SEED'") == Draft ]]; then
  snap=$(snapshot $SEED)
  req POST "/api/contracts/$SEED/submit";                       check "HD-2026-018 gửi khi thiếu dữ liệu -> 400" 400 '[.errors[].field]|join(",")' "REQUIRED_CONTENT,REQUIRED_FILES"
  check_unchanged "HD-2026-018 vẫn Draft, dữ liệu không đổi" $SEED "$snap"
  req PUT "/api/contracts/$SEED" "$(contract_body HD-2026-018 11111111-1111-1111-1111-111111111111 2026-10-01 2027-09-30 "Dịch vụ cảng" "Cung cấp dịch vụ cảng cho Công ty Minh Phát")"; check "Bổ sung nội dung -> 200" 200
  req POST "/api/contracts/$SEED/attachments" '{"fileName":"HD-2026-018-du-thao.pdf","documentType":"DuThaoHopDong"}'; check "Bổ sung tệp -> 201" 201
  req POST "/api/contracts/$SEED/submit";                       check "HD-2026-018 gửi lại -> 200 PendingApproval" 200 .status PendingApproval
  check_db "DB: HD-2026-018 PendingApproval" "SELECT \"Status\" FROM \"Contracts\" WHERE \"Id\"='$SEED'" PendingApproval
else
  echo "  (bỏ qua: HD-2026-018 không còn ở Draft trong DB $DB)"
fi

rm -f "$TMP"
printf '\n== Tổng kết: %d PASS, %d FAIL ==\n' "$PASS" "$FAIL"
for f in ${FAILED[@]+"${FAILED[@]}"}; do echo "  - $f"; done
((FAIL == 0))
