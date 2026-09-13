using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

// Generates a demo JWT for the Core MVP - Plataforma de Eventos (Fase 1: sin IdP real, Constitucion §5.1).
// Usage: dotnet run --project tools/generate-demo-token -- <Admin|User> [signingKey]
// The signing key must match the EventService `Jwt__SigningKey` and be at least 32 bytes long (spec FR-022).

var role = args.Length > 0 ? args[0] : "Admin";
if (role != "Admin" && role != "User")
{
    Console.Error.WriteLine("El rol debe ser 'Admin' o 'User'.");
    return 1;
}

var signingKeyValue = args.Length > 1
    ? args[1]
    : Environment.GetEnvironmentVariable("Jwt__SigningKey");

if (string.IsNullOrEmpty(signingKeyValue) || Encoding.UTF8.GetByteCount(signingKeyValue) < 32)
{
    Console.Error.WriteLine(
        "Se necesita un signing key de al menos 32 bytes: pasalo como segundo argumento o " +
        "configura la variable de entorno Jwt__SigningKey (debe ser el mismo valor que usa EventService).");
    return 1;
}

var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKeyValue));
var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

var claims = new[]
{
    new Claim(JwtRegisteredClaimNames.Sub, $"demo-{role.ToLowerInvariant()}"),
    new Claim("role", role),
};

var token = new JwtSecurityToken(
    claims: claims,
    expires: DateTime.UtcNow.AddHours(24),
    signingCredentials: credentials);

Console.WriteLine(new JwtSecurityTokenHandler().WriteToken(token));
return 0;
