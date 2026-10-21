#!/usr/bin/env bash
# OqTish end-to-end smoke test (Development only: uses demo seed logins and the Fake SMS mailbox).
#
#   deploy/smoke-test.sh                  # against http://localhost:8080
#   BASE=http://localhost:5173 deploy/smoke-test.sh
#
# Prerequisites: stack running in Development mode and `… run --rm api seed` done once; bash, curl, python 3
# (python3, or python on Windows where python3 may be only the Microsoft Store stub; override with PY=…).
# Prints one line per step: ✅ expected result, ❌ unexpected (exit code 1 if any step fails).
set -u
BASE="${BASE:-http://localhost:8080}"; B="$BASE/api"; H='X-OqTish-Client: web'; T=$(mktemp -d); FAIL=0
trap 'rm -rf "$T"' EXIT
PY="${PY:-$(for c in python3 python; do "$c" -c "" >/dev/null 2>&1 && { echo "$c"; break; }; done)}"
[ -n "$PY" ] || { echo "python 3 not found (set PY=…)" >&2; exit 1; }
j() { "$PY" -c "import sys,json; d=json.load(sys.stdin); print(eval(sys.argv[1]))" "$1" 2>/dev/null; }
check() { if [ "$2" = "$3" ]; then printf '✅ %-44s %s\n' "$1" "$2"; else printf '❌ %-44s got %s, expected %s\n' "$1" "$2" "$3"; FAIL=1; fi; }
post() { curl -s -o "$T/out" -w '%{http_code}' -b "$T/$1" -c "$T/$1" -H "$H" -H 'Content-Type: application/json' ${3:+-H "Idempotency-Key: $3"} -d "$4" "$B$2"; }
login() { post "$1" /auth/password/login "" "{\"email\":\"$2\",\"password\":\"$3\"}"; }
money() { printf '{"amount":"%s","currency":"UZS"}' "$1"; }

check "API ready" "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/health/ready")" 200
check "reception login" "$(login rec reception@demo.oqtish.local 'Demo123!reception')" 200
check "finance login" "$(login fin finance@demo.oqtish.local 'Demo123!finance')" 200
check "platform admin login" "$(login adm admin@oqtish.local 'Admin123!demo')" 200
CL=$(curl -s -b "$T/rec" "$B/me" | j 'd["memberships"][0]["clinicId"]')

# Public catalogue + availability
curl -s "$B/public/clinics/$CL" > "$T/cd"
BR=$(j '[b["id"] for b in d["branches"] if b["services"]][0]' < "$T/cd")
SV=$(j '[b for b in d["branches"] if b["services"]][0]["services"][0]["serviceId"]' < "$T/cd")
for k in 2 3 4 5 6 7; do DAY=$(date -d "+$k day" +%F); curl -s "$B/public/branches/$BR/availability?serviceId=$SV&date=$DAY" > "$T/av"; [ "$(j 'len(d["slots"])' < "$T/av")" -gt 0 ] 2>/dev/null && break; done
ST=$(j 'd["slots"][0]["startsAt"]' < "$T/av")
check "free slot found" "$([ -n "$ST" ] && echo yes || echo no)" yes

# Patient: OTP sign-in via the Development Fake SMS mailbox
P="+99890$(shuf -i 1000000-9999999 -n1)"; PE=$("$PY" -c "import urllib.parse,sys;print(urllib.parse.quote(sys.argv[1]))" "$P")
post pat /auth/otp/request "" "{\"phone\":\"$P\"}" > /dev/null; CH=$(j 'd["challengeId"]' < "$T/out")
OTP=$(curl -s "$B/dev/sms?phone=$PE" | "$PY" -c "import sys,json,re; d=json.load(sys.stdin); d=d if isinstance(d,list) else d.get('items',[]); print(re.search(r'\b(\d{6})\b', json.dumps(d[-1])).group(1))")
check "patient OTP sign-in" "$(post pat /auth/otp/verify "" "{\"challengeId\":\"$CH\",\"code\":\"$OTP\"}")" 200

# Booking → confirm
check "patient books" "$(post pat /appointments "smoke-$RANDOM" "{\"branchId\":\"$BR\",\"serviceId\":\"$SV\",\"startsAt\":\"$ST\",\"contactName\":\"Smoke Test\",\"contactPhone\":\"$P\"}")" 201
AP=$(j 'd["id"]' < "$T/out"); check "status is Pending, doctor hidden" "$(j 'd["status"], d["doctor"]' < "$T/out")" "('Pending', None)"
DOC=$(curl -s -b "$T/rec" "$B/clinics/$CL/appointments/$AP/eligible-doctors" | j '(d if isinstance(d,list) else d["items"])[0]["id"]')
check "reception confirms" "$(post rec "/clinics/$CL/appointments/$AP/confirm" "" "{\"doctorId\":\"$DOC\"}")" 200
check "second confirm → conflict" "$(post rec "/clinics/$CL/appointments/$AP/confirm" "" "{\"doctorId\":\"$DOC\"}")" 409
check "patient sees Confirmed" "$(curl -s -b "$T/pat" "$B/me/appointments/$AP" | j 'd["status"]')" Confirmed

# Isolation
check "patient → clinic console (404)" "$(curl -s -o /dev/null -w '%{http_code}' -b "$T/pat" "$B/clinics/$CL/appointments/$AP")" 404
check "reception → admin (403)" "$(curl -s -o /dev/null -w '%{http_code}' -b "$T/rec" "$B/admin/dashboard")" 403
check "POST without client header (403)" "$(curl -s -o /dev/null -w '%{http_code}' -b "$T/rec" -H 'Content-Type: application/json' -d '{}' "$B/auth/logout")" 403

# Finance
PAY="{\"amount\":$(money 50000.00),\"method\":\"Cash\"}"
check "record payment" "$(post rec "/clinics/$CL/appointments/$AP/payments" "pay-$AP" "$PAY")" 201
PID=$(j 'd["id"]' < "$T/out")
check "same payment replay (idempotent)" "$(post rec "/clinics/$CL/appointments/$AP/payments" "pay-$AP" "$PAY")" 201
check "reception refund (403)" "$(post rec "/clinics/$CL/payments/$PID/refund" "r-$PID" "{\"amount\":$(money 10000.00),\"reason\":\"smoke\"}")" 403
check "finance refund" "$(post fin "/clinics/$CL/payments/$PID/refund" "r-$PID" "{\"amount\":$(money 10000.00),\"reason\":\"smoke\"}")" 201
check "over-refund (422)" "$(post fin "/clinics/$CL/payments/$PID/refund" "r2-$PID" "{\"amount\":$(money 90000.00),\"reason\":\"x\"}")" 422
check "balance paid/refunded" "$(curl -s -b "$T/fin" "$B/clinics/$CL/appointments/$AP/balance" | j 'd["paid"]["amount"], d["refunded"]["amount"]')" "('50000.00', '10000.00')"
check "reception → reports (403)" "$(curl -s -o /dev/null -w '%{http_code}' -b "$T/rec" "$B/clinics/$CL/reports/daily?from=$(date +%F)&to=$(date +%F)")" 403
check "finance → reports" "$(curl -s -o /dev/null -w '%{http_code}' -b "$T/fin" "$B/clinics/$CL/reports/daily?from=$(date +%F)&to=$(date +%F)")" 200

# Admin + notifications
check "admin dashboard" "$(curl -s -o /dev/null -w '%{http_code}' -b "$T/adm" "$B/admin/dashboard")" 200
check "admin notifications" "$(curl -s -o /dev/null -w '%{http_code}' -b "$T/adm" "$B/admin/notifications?pageSize=5")" 200

[ "$FAIL" = 0 ] && echo "Hammasi muvaffaqiyatli (all steps passed)." || echo "Ba'zi qadamlar muvaffaqiyatsiz (see ❌ above)."
exit "$FAIL"
