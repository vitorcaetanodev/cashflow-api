using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace CashFlow.Api.Controllers;

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    private readonly IConfiguration _config;

    public AuthController(IConfiguration config)
    {
        _config = config;
    }

    [HttpPost("token")]
    public IActionResult GerarToken([FromBody] LoginRequest req)
    {
        var usuario = _config["Auth:Usuario"] ?? "postgres";
        var senha   = _config["Auth:Senha"]   ?? "admin";

        if (req.Usuario != usuario || req.Senha != senha)
            return Unauthorized(new { erro = "Credenciais inválidas" });

        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));

        var token = new JwtSecurityToken(
            issuer:             _config["Jwt:Issuer"],
            audience:           _config["Jwt:Audience"],
            claims:             new[] { new Claim(ClaimTypes.Name, req.Usuario) },
            expires:            DateTime.UtcNow.AddHours(8),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        );

        return Ok(new {
            token     = new JwtSecurityTokenHandler().WriteToken(token),
            expira_em = DateTime.UtcNow.AddHours(8)
        });
    }

    public record LoginRequest(string Usuario, string Senha);
}