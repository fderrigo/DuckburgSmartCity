<#
    genera-chiavi-rp.ps1

    Genera un set di chiavi private nuovo per il Relying Party, nel formato
    atteso da Duckburg.Identity (Rp:PrivateKeysFile).

    Perche' serve: secrets/rp_private_keys.sample.json contiene chiavi demo ed e'
    versionato, quindi pubblico. Vanno bene finche' la federazione gira solo in
    locale. Nel momento in cui il RP e' raggiungibile da Internet, quelle chiavi
    firmano il token SSO che apre l'area personale: chiunque le legga da GitHub
    puo' emetterne uno valido. Per il deploy pubblico servono chiavi nuove.

    Non richiede dipendenze: usa RSA di .NET, gia' presente su Windows.

    Uso:
        .\genera-chiavi-rp.ps1 -Destinazione ..\secrets\rp_private_keys.prod.json

    Poi il file va copiato sul server e passato all'app con Rp__PrivateKeysFile.
    NON va committato: tienilo fuori dal repository.
#>

[CmdletBinding()]
param(
    [string]$Destinazione = "rp_private_keys.prod.json",
    [int]$Bit = 2048
)

$ErrorActionPreference = "Stop"

# Base64 URL-safe senza padding, come richiede il formato JWK (RFC 7515).
function ToBase64Url([byte[]]$dati) {
    [Convert]::ToBase64String($dati).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

# Thumbprint RFC 7638: SHA-256 del JWK canonico con le sole chiavi e, kty, n,
# in ordine alfabetico e senza spazi. E' il modo standard di derivare un kid.
function CalcolaKid([string]$e, [string]$n) {
    $canonico = '{"e":"' + $e + '","kty":"RSA","n":"' + $n + '"}'
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        ToBase64Url $sha.ComputeHash([Text.Encoding]::ASCII.GetBytes($canonico))
    } finally {
        $sha.Dispose()
    }
}

function NuovaChiave([string]$uso, [string]$alg) {
    $rsa = [System.Security.Cryptography.RSA]::Create($Bit)
    try {
        $p = $rsa.ExportParameters($true)
        $nB64 = ToBase64Url $p.Modulus
        $eB64 = ToBase64Url $p.Exponent

        $chiave = [ordered]@{}
        if ($uso) { $chiave["use"] = $uso }
        # La chiave di cifratura porta alg, come nel formato atteso dal RP.
        if ($alg) { $chiave["alg"] = $alg }
        $chiave["kty"] = "RSA"
        $chiave["n"]   = $nB64
        $chiave["e"]   = $eB64
        $chiave["d"]   = ToBase64Url $p.D
        $chiave["p"]   = ToBase64Url $p.P
        $chiave["q"]   = ToBase64Url $p.Q
        $chiave["kid"] = CalcolaKid $eB64 $nB64
        return $chiave
    } finally {
        $rsa.Dispose()
    }
}

Write-Host "Genero tre chiavi RSA da $Bit bit..." -ForegroundColor Cyan

# Stessa struttura del sample: una chiave di federazione, due core (firma e cifratura).
$documento = [ordered]@{
    jwks_fed  = [ordered]@{ keys = @( (NuovaChiave $null $null) ) }
    jwks_core = [ordered]@{ keys = @( (NuovaChiave "sig" $null), (NuovaChiave "enc" "RSA-OAEP") ) }
}

$json = $documento | ConvertTo-Json -Depth 6

# Percorso assoluto senza richiedere che il file esista gia'.
# Join-Path su un percorso gia' assoluto produrrebbe "C:\qui\C:\la": va escluso.
if ([IO.Path]::IsPathRooted($Destinazione)) {
    $percorso = [IO.Path]::GetFullPath($Destinazione)
} else {
    $percorso = [IO.Path]::GetFullPath((Join-Path (Get-Location).Path $Destinazione))
}
$cartella = Split-Path -Parent $percorso
if ($cartella -and -not (Test-Path $cartella)) {
    throw "Cartella inesistente: $cartella"
}
# UTF8 senza BOM: il parser JSON di .NET non gradisce il BOM.
[IO.File]::WriteAllText($percorso, $json, (New-Object Text.UTF8Encoding($false)))
$Destinazione = $percorso

Write-Host "Scritto: $Destinazione" -ForegroundColor Green
Write-Host ""
Write-Host "kid generati:" -ForegroundColor Cyan
Write-Host ("  jwks_fed        {0}" -f $documento.jwks_fed.keys[0].kid)
Write-Host ("  jwks_core sig   {0}" -f $documento.jwks_core.keys[0].kid)
Write-Host ("  jwks_core enc   {0}" -f $documento.jwks_core.keys[1].kid)
Write-Host ""
Write-Host "Passi successivi:" -ForegroundColor Yellow
Write-Host "  1. Copia il file sul server, fuori dal repository."
Write-Host "  2. Puntaci Rp__PrivateKeysFile."
Write-Host "  3. Rigenera l'onboarding del RP sul Trust Anchor: il JWKS pubblico e' cambiato."
Write-Host "  4. Non committare questo file."
