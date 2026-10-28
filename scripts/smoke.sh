#!/bin/sh
set -eu

BASE="${1:-http://localhost:8280}"

status_of() {
  curl -fsS "$BASE/api/documents/$1" | grep -o '"status":"[A-Za-z]*"' | head -1 | cut -d'"' -f4
}

wait_status() {
  id="$1"
  expected="$2"
  current=""
  for _ in $(seq 1 90); do
    current="$(status_of "$id")"
    if [ "$current" = "$expected" ]; then
      echo "ok  $id -> $current"
      return 0
    fi
    sleep 2
  done
  echo "falhou: $id ficou em '$current' (esperado $expected)"
  return 1
}

create_sample() {
  curl -fsS -X POST "$BASE/api/samples" \
    -H 'Content-Type: application/json' \
    -d "{\"template\":\"$1\",\"scenario\":\"$2\"}" \
    | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4
}

curl -fsS "$BASE/health" | grep -q ok
curl -fsS "$BASE/" | grep -q '<title>DocFlow</title>'

valid="$(create_sample Classic Valid)"
wrong="$(create_sample Classic WrongTotal)"
invalid_cnpj="$(create_sample Classic InvalidCnpj)"

wait_status "$valid" Approved
wait_status "$wrong" NeedsReview
wait_status "$invalid_cnpj" NeedsReview

pdf="$(mktemp)"
curl -fsS "$BASE/api/documents/$valid/file" -o "$pdf"
duplicate="$(curl -fsS -X POST "$BASE/api/documents" -F "file=@$pdf;filename=reenvio.pdf;type=application/pdf"   | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)"
wait_status "$duplicate" Rejected

curl -fsS -X POST "$BASE/api/documents/$wrong/reject" \
  -H 'Content-Type: application/json' \
  -d '{"reason":"total divergente","reviewer":"smoke"}' > /dev/null
wait_status "$wrong" Rejected

sse_code="$(curl -sN --max-time 2 -o /dev/null -w '%{http_code}' "$BASE/api/documents/events" || true)"
[ "$sse_code" = "200" ] || { echo "SSE respondeu $sse_code"; exit 1; }

echo "smoke ok"
