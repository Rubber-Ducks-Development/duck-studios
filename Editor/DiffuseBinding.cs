namespace DuckStudio;

// Streamed PED variations override DiffuseSampler; the internal image name need
// not match the model's reference. Never fall back to an arbitrary dictionary row.
public static class DiffuseBinding
{
    public static string? Select(IEnumerable<string> textures, IEnumerable<string> diffuseNames, IEnumerable<string> auxiliaryNames)
    {
        var names = textures.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        var diffuse = diffuseNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var exact = names.FirstOrDefault(diffuse.Contains);
        if (exact != null) return exact;
        var auxiliary = auxiliaryNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = names.Where(n => !auxiliary.Contains(n) && !IsDataMap(n)).ToList();
        return candidates.Count == 1 ? candidates[0] : null;
    }

    private static bool IsDataMap(string name)
    {
        var tokens = name.ToLowerInvariant().Split(['_', '-', '.', ' '], StringSplitOptions.RemoveEmptyEntries);
        return tokens.Any(t => t is "normal" or "normalmap" or "norm" or "nrm" or "bump" or "bumpmap" or "spec" or "specmap" or "specular" or "specularmap" or "roughness" or "metallic" or "ao");
    }
}
