﻿using Microsoft.AspNetCore.Identity;

namespace Identity.API.Domain.Entities;

public class User : IdentityUser
{
    public string Provider { get; set; } = null!;
    public string? RefreshToken { get; set; }
    public DateTimeOffset RefreshTokenExpiryTime { get; set; }
}

