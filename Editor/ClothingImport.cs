using System.IO;

namespace DuckStudio;

public static class ClothingImport
{
    public static int NextPosition(IEnumerable<ClothingAsset> items, string ped, string slot)
        => checked(items.Where(x => x.Ped == ped && x.Slot == slot).Select(x => x.Drawable).DefaultIfEmpty(-1).Max() + 1);

    public static void Append(List<ClothingAsset> items, ClothingAsset asset)
    {
        asset.Drawable = NextPosition(items, asset.Ped, asset.Slot);
        asset.IsNew = true;
        items.Add(asset);
    }

    public static ClothingAsset? TextureTarget(IEnumerable<ClothingAsset> items, string path, string? requiredPed, ClothingAsset? selected)
    {
        if (!ProjectIO.TryParseTexture(path, out var slot, out var number))
            return selected != null && (requiredPed == null || selected.Ped == requiredPed) ? selected : null;

        var namedPed = Path.GetFileName(path).Contains("mp_f_freemode_01", StringComparison.OrdinalIgnoreCase) ? "mp_f_freemode_01"
            : Path.GetFileName(path).Contains("mp_m_freemode_01", StringComparison.OrdinalIgnoreCase) ? "mp_m_freemode_01" : null;
        var ped = requiredPed ?? namedPed;
        var candidates = items.Where(x => (ped == null || x.Ped == ped) && x.Slot == slot &&
            (ProjectIO.TryParseDrawable(x.ModelPath, out _, out var original) ? original : x.Drawable) == number).ToList();
        var sameCollection = candidates.Where(x => ProjectIO.SourceCollection(x.ModelPath) == ProjectIO.SourceCollection(path)).ToList();
        if (sameCollection.Count > 0) candidates = sameCollection;
        if (selected != null && candidates.Contains(selected)) return selected;
        return candidates.Count == 1 ? candidates[0] : null;
    }
}
