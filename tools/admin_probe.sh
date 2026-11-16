#!/bin/sh
# Probe the Stalwart admin JMAP objects Barbican uses. Prints status and method results only,
# never the key. Usage: sh tools/admin_probe.sh [keyfile] [server]
KEYFILE=${1:-target/release/api.txt}
SERVER=${2:-https://mail.example.com}
KEY=$(tr -d ' \r\n' < "$KEYFILE")
for T in Domain Account QueuedMessage Log DmarcExternalReport; do
  BODY='{"using":["urn:ietf:params:jmap:core","urn:stalwart:jmap"],"methodCalls":[["x:'$T'/query",{"limit":2,"calculateTotal":true},"q"],["x:'$T'/get",{"#ids":{"resultOf":"q","name":"x:'$T'/query","path":"/ids"}},"g"]]}'
  OUT=$(curl -s -m 15 -w '\n%{http_code}' -H "Authorization: Bearer $KEY" -H 'Content-Type: application/json' -d "$BODY" "$SERVER/jmap/")
  CODE=$(printf '%s' "$OUT" | tail -n1)
  printf '%-22s HTTP %s  ' "$T" "$CODE"
  printf '%s' "$OUT" | sed '$d' | python -c "import json,sys
try:
  d=json.load(sys.stdin)
  for m in d.get('methodResponses',[]):
    a=m[1]; print(m[0], a.get('type','') or ('total=%s'%a.get('total','')) if m[0].endswith('query') or m[0]=='error' else 'keys=%s'%sorted((a.get('list') or [{}])[0].keys())[:12], end='  ')
  print()
except Exception as e: print('non-JSON response')"
done
