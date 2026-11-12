namespace DrgSave.Core;

/// <summary>
/// Names and GUIDs for Deep Rock Galactic save fields.
///
/// UNVERIFIED. These come from the open-source Steam editor DRG-Save-Editor
/// (https://github.com/AnthonyMichaelTDM/DRG-Save-Editor), which finds them by byte search.
/// GUIDs are written in file byte order (the 16 bytes as they appear in the save). Each one
/// must be confirmed against a real PS4 save before a page is trusted to edit it.
/// </summary>
public static class DrgKnown
{
    public const string Credits = "Credits";
    public const string PerkPoints = "PerkPoints";

    /// <summary>Map&lt;Guid, Float&gt; of everything the player owns: minerals, brewing, cubes, cores.</summary>
    public const string OwnedResources = "OwnedResources";

    public sealed record Resource(string Name, string Group, string Guid);

    public static readonly Resource[] Resources =
    {
        new("Bismor", "Minerals", "AF0DC4FE8361BB48B32C92CC97E21DE7"),
        new("Croppa", "Minerals", "8AA7FB43293A0B49B8BE42FFE068A44C"),
        new("Enor Pearl", "Minerals", "488D05146F5F754BA3D4610D08C0603E"),
        new("Jadiz", "Minerals", "22BC4F7D07D13E43BFCA81BD9C14B1AF"),
        new("Magnite", "Minerals", "AADED8766C227D408032AFD18D63561E"),
        new("Umanite", "Minerals", "5F2BCF8347760A42A23B6EDC07C0941D"),
        new("Barley Bulb", "Brewing", "22DAA757AD7A8049891B17EDCC2FE098"),
        new("Malt Star", "Brewing", "41EA550C1D46C54BBE2E9CA5A7ACCB06"),
        new("Starch Nut", "Brewing", "72312204E287BC41815540A0CF881280"),
        new("Yeast Cone", "Brewing", "078548B93232C04085F892E084A74100"),
        new("Error Cube", "Special", "5828652C9A5DE845A9E2E1B8B463C516"),
        new("Blank Matrix Core", "Special", "A10CB2853871FB499AC854A1CDE2202C"),
        new("Data Cell", "Special", "99FA526AD87748459498905A278693F6"),
        new("Phazyonite", "Special", "67668AAE828FDB48A9111E1B912DBFA4"),
    };

    public sealed record Dwarf(string Name, string Guid);

    /// <summary>
    /// Root array of class records (confirmed on PS4). Each item holds SavegameID (the class
    /// GUID below), XP, TimesRetired (promotions) and RetiredCharacterLevels.
    /// </summary>
    public const string CharacterSaves = "CharacterSaves";

    public static readonly Dwarf[] Dwarves =
    {
        new("Driller", "9EDD56F1EEBCC5488D5B5E5B80B62DB4"),
        new("Engineer", "85EF626C65F1024A8DFEB5D0F3909D2E"),
        new("Gunner", "AE56E180FEC0C44D96FA29C28366B97B"),
        new("Scout", "30D8EA17D8FBBA4C95306DE9655C2F8C"),
    };

    /// <summary>SeasonSave.Seasons: Map&lt;Guid, struct&gt; with XP and Tokens (scrip) per season.</summary>
    public const string Seasons = "Seasons";

    public const int XpPerSeasonLevel = 5000;

    public static readonly Dwarf[] SeasonIds =
    {
        new("Season 1", "A47D407EC0E4364892CE2E03DE7DF0B3"),
        new("Season 2", "B860B55F1D1BB54D8EE2E41FDA9F5838"),
        new("Season 3", "D8810F6C76D374419AE6A18EF5B3BA26"),
        new("Season 4", "0A3AE2198CA5B649B56E4E11D6762AC6"),
        new("Season 5", "82F3091744BBDE4DB1A0C6766C0716EF"),
        new("Season 6", "E1BF9763D4582E4FB4F6DE7F40554632"),
    };

    public static string? ResourceName(string guidHex) =>
        Resources.FirstOrDefault(r => Same(r.Guid, guidHex))?.Name
        ?? Dwarves.FirstOrDefault(d => Same(d.Guid, guidHex))?.Name
        ?? SeasonIds.FirstOrDefault(s => Same(s.Guid, guidHex))?.Name
        ?? DrgItems.NameOf(guidHex);

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
