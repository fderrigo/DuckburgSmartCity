#!/bin/bash
# End-to-end driver: CIE profile login + refresh + logout. Runs against the docker stack.
set -e
cd "$(dirname "$0")"
PROFILE="${1:-cie}"
OPJ=op_jar.txt; RPJ=rp_jar.txt
rm -f $OPJ $RPJ
say(){ echo; echo "=== $* ==="; }

say "1. RP authorization ($PROFILE)"
LOC=$(curl -s -i "http://localhost:8001/oidc/rp/authorization?profile=$PROFILE" | grep -i "^location:" | sed 's/[Ll]ocation: //; s/\r//')
# OP authority (host:port) and hostname, derived from the redirect target
OP_AUTH=$(echo "$LOC" | sed -E 's#^https?://([^/]+)/.*#\1#')
OP_HOST=$(echo "$OP_AUTH" | cut -d: -f1)
OP_PORT=$(echo "$OP_AUTH" | cut -d: -f2)
URL=$(echo "$LOC" | sed "s#://$OP_AUTH#://localhost:$OP_PORT#")
echo "OP = $OP_AUTH"

say "2. OP login page"
curl -s -c $OPJ -H "Host: $OP_AUTH" "$URL" -o login.html
CSRF=$(python -c "import re;print(re.search(r'name=\"csrfmiddlewaretoken\" value=\"([^\"]+)\"',open('login.html',encoding='utf-8').read()).group(1))")
AZR=$(python -c "import re;print(re.search(r'name=\"authz_request_object\" value=\"([^\"]+)\"',open('login.html',encoding='utf-8').read()).group(1))")

say "3. POST credentials (paperino/paperopoli)"
curl -s -i -b $OPJ -c $OPJ -H "Host: $OP_AUTH" -H "Referer: http://$OP_AUTH/oidc/op/authorization" \
  --data-urlencode "csrfmiddlewaretoken=$CSRF" --data-urlencode "authz_request_object=$AZR" \
  --data-urlencode "username=paperino" --data-urlencode "password=paperopoli" \
  "http://localhost:$OP_PORT/oidc/op/authorization" -o /dev/null
echo "login posted"

say "4. Consent"
curl -s -b $OPJ -c $OPJ -H "Host: $OP_AUTH" "http://localhost:$OP_PORT/oidc/op/consent" -o consent.html
CSRF2=$(python -c "import re;print(re.search(r'name=\"csrfmiddlewaretoken\" value=\"([^\"]+)\"',open('consent.html',encoding='utf-8').read()).group(1))")
CB=$(curl -s -i -b $OPJ -c $OPJ -H "Host: $OP_AUTH" -H "Referer: http://$OP_AUTH/oidc/op/consent" \
  --data-urlencode "csrfmiddlewaretoken=$CSRF2" --data-urlencode "agree=True" \
  "http://localhost:$OP_PORT/oidc/op/consent" | grep -i "^location:" | sed 's/[Ll]ocation: //; s/\r//')
CBURL=$(echo "$CB" | sed 's#http://identity.paperopoli.test:8001#http://localhost:8001#')
echo "callback: code received"

say "5. RP callback (token + userinfo)"
curl -s -c $RPJ -H "Host: identity.paperopoli.test" "$CBURL" -o profile.html
grep -ioE "Autenticazione riuscita|Codice fiscale|refresh_token non emesso|scade tra [0-9]+s" profile.html | sort -u
python -c "
import re;h=open('profile.html',encoding='utf-8').read()
for l in ['Nome','Cognome','Email','Codice fiscale']:
    m=re.search(r'<th>'+l+r'</th><td>(?:<code>)?([^<]*)',h)
    if m: print(l,'=',m.group(1))
print('rp_state cookie set:', 'rp_state' in open('$RPJ',encoding='utf-8',errors='ignore').read())
"

say "6. RP refresh"
curl -s -b $RPJ -c $RPJ -H "Host: identity.paperopoli.test" "http://localhost:8001/oidc/rp/refresh" -o refresh.html
grep -ioE "Token aggiornati|Refresh failed|scade tra [0-9]+s" refresh.html | sort -u | head

say "7. RP logout (revocation)"
curl -s -b $RPJ -H "Host: identity.paperopoli.test" "http://localhost:8001/oidc/rp/logout" -o logout.html
grep -ioE "Logout effettuato|Token revocati" logout.html | sort -u

rm -f login.html consent.html profile.html refresh.html logout.html $OPJ $RPJ
echo; echo "DONE"
