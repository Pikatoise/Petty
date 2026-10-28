namespace Petty.Core.Domain;

public static class PetNameRules
{
    public const int MinLength = 1;
    public const int MaxLength = 24;
    public const string DefaultName = "Kitty";

    /// <summary>Trims the input and rejects empty, over-long or control-character names.</summary>
    public static bool TryNormalize(string? input, out string name, out string? error)
    {
        name = (input ?? string.Empty).Trim();
        error = null;

        if (name.Length < MinLength)
        {
            error = "Please enter a name for your pet.";
            return false;
        }

        if (name.Length > MaxLength)
        {
            error = $"The name must be at most {MaxLength} characters long.";
            return false;
        }

        foreach (var ch in name)
        {
            if (char.IsControl(ch))
            {
                error = "The name must not contain control characters.";
                return false;
            }
        }

        return true;
    }
}
