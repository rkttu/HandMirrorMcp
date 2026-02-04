namespace HandMirrorMcp.Constants;

/// <summary>
/// Unicode emoji constants for consistent output formatting.
/// Using escape sequences for better cross-platform compatibility.
/// </summary>
public static class Emoji
{
    // Status indicators
    public const string CheckMark = "\u2705";           // ✅
    public const string CrossMark = "\u274C";           // ❌
    public const string Warning = "\u26A0\uFE0F";       // ⚠️
    public const string Info = "\u2139\uFE0F";          // ℹ️
    public const string Question = "\u2753";            // ❓
    public const string Bulb = "\U0001F4A1";            // 💡
    public const string Fire = "\U0001F525";            // 🔥
    public const string Sparkles = "\u2728";            // ✨
    public const string Star = "\u2B50";                // ⭐
    public const string Celebration = "\U0001F389";     // 🎉

    // Severity indicators
    public const string RedCircle = "\U0001F534";       // 🔴
    public const string OrangeCircle = "\U0001F7E0";    // 🟠
    public const string YellowCircle = "\U0001F7E1";    // 🟡
    public const string GreenCircle = "\U0001F7E2";     // 🟢
    public const string WhiteCircle = "\u26AA";         // ⚪

    // Objects and tools
    public const string Package = "\U0001F4E6";         // 📦
    public const string Folder = "\U0001F4C1";          // 📁
    public const string FolderOpen = "\U0001F4C2";      // 📂
    public const string File = "\U0001F4C4";            // 📄
    public const string FileText = "\U0001F4DD";        // 📝
    public const string Clipboard = "\U0001F4CB";       // 📋
    public const string Books = "\U0001F4DA";           // 📚
    public const string Book = "\U0001F4D6";            // 📖
    public const string Scroll = "\U0001F4DC";          // 📜
    public const string Gear = "\u2699\uFE0F";          // ⚙️
    public const string Wrench = "\U0001F527";          // 🔧
    public const string Hammer = "\U0001F528";          // 🔨
    public const string HammerAndWrench = "\U0001F6E0\uFE0F"; // 🛠️
    public const string MagnifyingGlass = "\U0001F50D"; // 🔍
    public const string MagnifyingGlassLeft = "\U0001F50E"; // 🔎
    public const string Link = "\U0001F517";            // 🔗
    public const string Pin = "\U0001F4CC";             // 📌
    public const string Pushpin = "\U0001F4CD";         // 📍
    public const string Key = "\U0001F511";             // 🔑
    public const string Lock = "\U0001F512";            // 🔒
    public const string Unlock = "\U0001F513";          // 🔓

    // Computing
    public const string Computer = "\U0001F4BB";        // 💻
    public const string Desktop = "\U0001F5A5\uFE0F";   // 🖥️
    public const string Globe = "\U0001F310";           // 🌐
    public const string Shuffle = "\U0001F500";         // 🔀
    public const string Ruler = "\U0001F4D0";           // 📐

    // UI elements
    public const string Megaphone = "\U0001F4E2";       // 📢
    public const string Palette = "\U0001F3A8";         // 🎨
    public const string Copyright = "\u00A9\uFE0F";     // ©️
    public const string Window = "\U0001FA9F";          // 🪟
    public const string Siren = "\U0001F6A8";           // 🚨
    public const string Shield = "\U0001F6E1\uFE0F";    // 🛡️
    public const string Target = "\U0001F3AF";          // 🎯

    // Bullets and markers
    public const string Bullet = "\u2022";              // •
    public const string TreeBranch = "\u251C\u2500";    // ├─
    public const string TreeCorner = "\u2514\u2500";    // └─
    public const string TreeVertical = "\u2502";        // │

    // Format helpers
    public static string Severity(string level) => level.ToUpperInvariant() switch
    {
        "CRITICAL" => RedCircle,
        "HIGH" => OrangeCircle,
        "MODERATE" or "MEDIUM" => YellowCircle,
        "LOW" => GreenCircle,
        _ => WhiteCircle
    };

    public static string DiagnosticSeverity(string severity) => severity switch
    {
        "Error" => CrossMark,
        "Warning" => Warning,
        "Info" => Info,
        _ => Bulb
    };
}
