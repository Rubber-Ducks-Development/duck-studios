using System.Windows.Media;

namespace DuckStudio;

// One 24px outline family for the GTA component and prop slots.
public static class SlotIcons
{
    private static readonly Geometry Male = G("M14,10 A5,5 0 1 1 4,10 A5,5 0 1 1 14,10 Z M13,6 L21,2 M16,2 L21,2 L21,7");
    private static readonly Geometry Female = G("M17,9 A5,5 0 1 1 7,9 A5,5 0 1 1 17,9 Z M12,14 L12,22 M8,18 L16,18");
    public static Geometry GenderFor(bool female) => female ? Female : Male;
    private static readonly Dictionary<string, Geometry> Icons = new()
    {
        ["head"] = G("M12,3 C7,3 5,7 5,12 C5,18 8,21 12,21 C16,21 19,18 19,12 C19,7 17,3 12,3 Z M9,11 L9.1,11 M15,11 L15.1,11 M9,16 Q12,18 15,16"),
        ["berd"] = G("M5,9 Q12,12 19,9 L18,16 Q12,20 6,16 Z M8,13 L10,13 M14,13 L16,13"),
        ["hair"] = G("M4,12 C4,5 8,2 13,3 C18,3 20,7 20,12 L18,18 L17,11 Q13,12 9,8 Q8,12 6,12 L6,18 Z"),
        ["uppr"] = G("M8,3 L16,3 L20,7 L22,16 L18,17 L16,11 L16,21 L8,21 L8,11 L6,17 L2,16 L4,7 Z"),
        ["lowr"] = G("M6,3 L18,3 L17,11 L16,21 L12,21 L12,11 L11,21 L7,21 L6,11 Z M6,8 L18,8"),
        ["hand"] = G("M5,8 L19,8 L20,21 L4,21 Z M9,8 L9,6 Q9,3 12,3 Q15,3 15,6 L15,8 M5,12 L19,12"),
        ["feet"] = G("M4,8 L9,8 L10,14 L17,16 L21,16 L21,20 L3,20 L3,15 Z M4,15 L10,15"),
        ["teef"] = G("M5,5 Q12,19 19,5 M9,13 L12,19 L15,13 M12,19 L12,21"),
        ["accs"] = G("M7,3 L17,3 L21,8 L18,11 L16,8 L16,21 L8,21 L8,8 L6,11 L3,8 Z M9,3 Q12,7 15,3"),
        ["task"] = G("M6,3 L10,3 L10,7 L14,7 L14,3 L18,3 L21,9 L18,11 L18,21 L6,21 L6,11 L3,9 Z M10,13 L14,13 M12,11 L12,15"),
        ["decl"] = G("M4,4 L20,4 L20,20 L4,20 Z M8,8 L16,8 M8,12 L16,12 M8,16 L13,16"),
        ["jbib"] = G("M8,3 L10,6 L14,6 L16,3 L21,7 L19,12 L17,10 L17,21 L7,21 L7,10 L5,12 L3,7 Z M10,6 L12,11 L14,6"),
        ["p_head"] = G("M4,13 Q5,6 12,5 Q19,6 20,13 Z M3,13 L21,13 M13,13 Q17,15 20,13"),
        ["p_eyes"] = G("M2,9 L8,9 L9,15 L4,15 Z M16,9 L22,9 L20,15 L15,15 Z M9,10 Q12,8 15,10 M2,9 L1,7 M22,9 L23,7"),
        ["p_ears"] = G("M13,4 Q20,4 20,11 Q20,16 16,17 Q14,17 14,20 Q14,22 11,22 Q8,22 8,19 M16,12 Q15,9 13,11 M8,6 Q10,4 13,4"),
        ["p_lwrist"] = G("M9,2 L15,2 L15,6 L9,6 Z M6,6 L18,6 L18,18 L6,18 Z M9,18 L15,18 L15,22 L9,22 Z M12,9 L12,13 L15,14"),
        ["p_rwrist"] = G("M4,9 Q12,2 20,9 L20,15 Q12,22 4,15 Z M4,12 L20,12 M10,9 L10,15 M14,9 L14,15")
    };
    private static Geometry G(string data) { var g=Geometry.Parse(data); g.Freeze(); return g; }
    public static Geometry For(string slot) => Icons.TryGetValue(slot,out var icon) ? icon : Icons["jbib"];
}
