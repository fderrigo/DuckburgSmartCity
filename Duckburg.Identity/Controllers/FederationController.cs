using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Duckburg.Identity.Services;

namespace Duckburg.Identity.Controllers;

/// <summary>OpenID Federation endpoints: entity configuration and JWKS.</summary>
public sealed class FederationController(RpConfig rp, EntityConfigurationBuilder ecBuilder, JwtService jwt) : Controller
{
    [HttpGet("/.well-known/openid-federation")]
    public IActionResult EntityConfiguration() =>
        Content(ecBuilder.Build(), "application/entity-statement+jwt");

    [HttpGet("/oidc/rp/openid_relying_party/jwks.json")]
    public IActionResult Jwks() =>
        Json(rp.JwksCore.ToPublicJwks());

    [HttpGet("/oidc/rp/openid_relying_party/jwks.jose")]
    public IActionResult SignedJwks()
    {
        var payload = JsonSerializer.Deserialize<object>(
            JsonSerializer.Serialize(rp.JwksCore.ToPublicJwks()))!;
        return Content(jwt.Sign(payload, rp.FedSigningKey, typ: "jwk-set+jwt"), "application/jwk-set+jwt");
    }
}
