namespace Petty.Server.Domain.Accounts;

public sealed record AccountProfile(Guid UserId, string Login, string? PetName, string? PetAssetId, DateTimeOffset? ProfileUpdatedUtc);
