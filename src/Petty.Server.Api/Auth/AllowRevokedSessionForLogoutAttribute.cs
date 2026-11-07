namespace Petty.Server.Api.Auth;

[AttributeUsage(AttributeTargets.Method)]
public sealed class AllowRevokedSessionForLogoutAttribute : Attribute;
