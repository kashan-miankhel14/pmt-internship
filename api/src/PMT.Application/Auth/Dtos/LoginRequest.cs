namespace PMT.Application.Auth.Dtos;

public sealed record LoginRequest(string UserNameOrEmail, string Password);
