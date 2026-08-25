# Impostazioni da AGGIUNGERE in fondo a:
#   infra/federation_authority/federation_authority/settingslocal.py
#   infra/provider/provider/settingslocal.py
#
# Servono perche' in produzione Django sta dietro Caddy, che termina il TLS e
# inoltra in http. Senza queste righe Django si crede su http, genera URL http
# negli endpoint OIDC, e i redirect della federazione si rompono con errori che
# sembrano di trust chain ma sono di schema.
#
# Vanno bene identiche su entrambi i container: elencare tutti e tre gli host non
# fa danno ed evita di tenere due file diversi.

# Caddy dichiara lo schema originale qui: senza, request.is_secure() resta False.
SECURE_PROXY_SSL_HEADER = ("HTTP_X_FORWARDED_PROTO", "https")

# L'host da usare per costruire gli URL assoluti e' quello visto dal browser,
# non il nome del servizio interno a Docker.
USE_X_FORWARDED_HOST = True

# Django 4+ pretende l'origine https esplicita per le POST, altrimenti l'onboarding
# e il login falliscono con "CSRF verification failed".
CSRF_TRUSTED_ORIGINS = [
    "https://trust-anchor.paperopoli.derrigo.it",
    "https://cie-provider.paperopoli.derrigo.it",
    "https://identity.paperopoli.derrigo.it",
]

# Da ['*'] agli host effettivi: in produzione non c'e' motivo di accettare
# qualunque Host header.
ALLOWED_HOSTS = [
    "trust-anchor.paperopoli.derrigo.it",
    "cie-provider.paperopoli.derrigo.it",
    "identity.paperopoli.derrigo.it",
]

# I cookie di sessione e CSRF viaggiano solo su https.
SESSION_COOKIE_SECURE = True
CSRF_COOKIE_SECURE = True

# DEBUG a False in produzione: con True, una pagina di errore mostra la
# configurazione, le variabili d'ambiente e frammenti di codice a chiunque.
DEBUG = False
