# wwwroot/lib — componenti grafici ufficiali (vendored)

Asset di terze parti per i pulsanti ufficiali di accesso, copiati così come distribuiti.

## spid-sp-access-button/
Pulsante ufficiale **"Entra con SPID"**.
- Origine: [`italia/spid-sp-access-button`](https://github.com/italia/spid-sp-access-button) (branch `master`).
- Licenza/attribuzioni: vedi `spid-sp-access-button/LICENSE.md` (include jQuery — MIT,
  font Titillium — SIL OFL 1.1).
- Contenuto: `css/spid-sp-access-button.min.css`, `js/jquery.min.js`,
  `js/spid-sp-access-button.min.js` (toggle del menu IdP), `img/` (icona SPID + loghi IdP).
- Le voci IdP sono renderizzate **server-side** (Razor) con ordine **randomizzato** ad ogni
  caricamento (regola AgID) e `href` verso `/oidc/rp/authorization`; non si usa
  `spid-idps.js` perché i link devono puntare a questo RP.

## cie-graphics/
Pulsante ufficiale **"Entra con CIE"**.
- Origine: [`italia/cie-graphics`](https://github.com/italia/cie-graphics) (branch `master`).
- Contenuto: `entra_con_cie.svg` (immagine del pulsante), `Logo_CIE_ID.svg`.
