using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DuckStudio;

public static class PreviewTexture
{
    public enum AlphaMode { Opaque, Cutout, Blend }

    // RAGE materials choose alpha behavior. A diffuse alpha channel alone
    // does not make solid PED clothing a translucent WPF surface.
    public static AlphaMode ModeForShader(uint shader, int bucket = 0)
    {
        if (CutoutShaders.Contains(shader)) return AlphaMode.Cutout;
        if (BlendShaders.Contains(shader) || bucket != 0) return AlphaMode.Blend;
        return AlphaMode.Opaque;
    }

    private static readonly HashSet<uint> CutoutShaders = Hashes(
        "cutout", "cutout_um", "cutout_tnt", "cutout_fence", "cutout_fence_normal", "cutout_hard",
        "cutout_spec_tnt", "normal_cutout", "normal_cutout_tnt", "normal_cutout_um",
        "normal_spec_cutout", "normal_spec_cutout_tnt", "ped_hair", "ped_hair_spiked", "vehicle_cutout");
    private static readonly HashSet<uint> BlendShaders = Hashes(
        "alpha", "normal_alpha", "normal_spec_alpha", "normal_spec_reflect_alpha", "normal_reflect_alpha",
        "spec_alpha", "spec_reflect_alpha", "reflect_alpha", "cloth_spec_alpha", "cloth_normal_spec_alpha",
        "emissive_alpha", "emissive_alpha_tnt", "emissive_additive_alpha", "emissivenight_alpha",
        "emissivestrong_alpha", "normal_spec_reflect_emissivenight_alpha", "ped_decal",
        "decal", "decal_normal", "decal_spec", "glass", "glass_normal", "glass_spec");

    private static HashSet<uint> Hashes(params string[] names) => names.Select(name => Hash(name + ".sps")).ToHashSet();
    private static uint Hash(string name)
    {
        uint hash = 0;
        foreach (var c in name) { hash += c; hash += hash << 10; hash ^= hash >> 6; }
        hash += hash << 3; hash ^= hash >> 11; hash += hash << 15;
        return hash;
    }

    public static BitmapSource ForMaterial(BitmapSource source, AlphaMode mode)
    {
        if (mode == AlphaMode.Blend) return source;
        // Convert to straight BGRA before changing alpha, preserving RGB detail
        // even when the decoder supplied premultiplied pixels.
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        for (var i = 3; i < pixels.Length; i += 4)
            pixels[i] = mode == AlphaMode.Cutout && pixels[i] <= 84 ? (byte)0 : (byte)255;
        var result = BitmapSource.Create(source.PixelWidth, source.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }
}
