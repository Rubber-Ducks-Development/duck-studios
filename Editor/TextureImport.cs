using System.IO;
using ImageMagick;

namespace DuckStudio;

public sealed class TextureSourceLink
{
    public string SourcePath { get; set; } = "";
    public string TextureName { get; set; } = "";
    public int MaxDimension { get; set; } = 8192;
    public bool CompressDiffuse { get; set; }
}

public static class TextureImport
{
    public const string Filter = "Textures and images|*.ytd;*.png;*.jpg;*.jpeg;*.jpe;*.jfif;*.psd;*.psb;*.webp;*.bmp;*.tif;*.tiff;*.gif;*.tga;*.dds;*.heic;*.heif;*.avif;*.ico;*.jxl;*.jp2;*.j2k;*.pcx;*.ppm;*.pgm;*.pbm;*.pnm;*.pam;*.qoi;*.exr;*.hdr|GTA textures (*.ytd)|*.ytd|Photoshop (*.psd;*.psb)|*.psd;*.psb|All files (*.*)|*.*";
    private static readonly HashSet<string> Extensions = new(".png .jpg .jpeg .jpe .jfif .psd .psb .webp .bmp .tif .tiff .gif .tga .dds .heic .heif .avif .ico .jxl .jp2 .j2k .pcx .ppm .pgm .pbm .pnm .pam .qoi .exr .hdr".Split(' '), StringComparer.OrdinalIgnoreCase);
    private static readonly object ImportGate = new();
    public static string CacheRoot => Path.Combine(Environment.GetEnvironmentVariable("WEFT_STUDIO_DATA_HOME")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WeftStudio"), "LinkedTextures");
    public static bool IsSupported(string path) => Path.GetExtension(path).Equals(".ytd", StringComparison.OrdinalIgnoreCase) || Extensions.Contains(Path.GetExtension(path));
    public static string NewDestination(string source) => Path.Combine(CacheRoot, Guid.NewGuid().ToString("N"), Path.GetFileNameWithoutExtension(source) + ".ytd");

    // Read the flattened composite (frame zero for PSD), then create lossless BGRA DDS
    // data with a full mip chain. CodeWalker wraps it in a real GTA texture dictionary.
    public static string Convert(string source, string destination, string core, string model, string? template = null, string? textureName = null, int maxDimension = 8192, bool compress = false, int fixedDimension = 0)
    {
        lock (ImportGate)
        {
            if(maxDimension<1||maxDimension>8192||(maxDimension&(maxDimension-1))!=0||fixedDimension<0||fixedDimension>8192)throw new InvalidDataException("Texture size must be a power of two from 1 to 8192.");
            var original = new FileInfo(source);
            var originalStamp = (original.LastWriteTimeUtc.Ticks, original.Length);
            using var stream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var input = new MemoryStream(); stream.CopyTo(input); input.Position = 0;
            var read = new MagickReadSettings { FrameIndex = 0, FrameCount = 1 };
            var extension = Path.GetExtension(source).ToLowerInvariant();
            if (extension == ".tga") read.Format = MagickFormat.Tga;
            if (extension == ".j2k") read.Format = MagickFormat.J2k;
            if (extension == ".ico") read.Format = MagickFormat.Ico;
            using var image = new MagickImage(input, read);
            image.AutoOrient(); image.Depth = 8;
            if (image.GetColorProfile() != null) image.TransformColorSpace(ColorProfiles.SRGB);
            else image.ColorSpace = ColorSpace.sRGB;
            if (image.Width == 0 || image.Height == 0 || image.Width > 16384 || image.Height > 16384)
                throw new InvalidDataException("Texture dimensions must be between 1 and 16384 pixels.");
            uint width = PowerOfTwo(image.Width), height = PowerOfTwo(image.Height);
            if(fixedDimension>0) width=height=(uint)fixedDimension;
            else if(Math.Max(width,height)>maxDimension) { var ratio=(double)maxDimension/Math.Max(width,height); width=Math.Max(1,(uint)(width*ratio)); height=Math.Max(1,(uint)(height*ratio)); }
            if (image.Width != width || image.Height != height) image.Resize(new MagickGeometry(width, height) { IgnoreAspectRatio = true });
            int levels = 1;
            for (uint w = width, h = height; w > 1 || h > 1; w = Math.Max(1, w / 2), h = Math.Max(1, h / 2)) levels++;
            using var data = new MemoryStream();
            using var writer = new BinaryWriter(data, System.Text.Encoding.UTF8, leaveOpen: true);
            writer.Write(0x20534444u); writer.Write(124u); writer.Write(0x2100Fu);
            writer.Write(height); writer.Write(width); writer.Write(width * 4); writer.Write(0u); writer.Write((uint)levels);
            for (int i = 0; i < 11; i++) writer.Write(0u);
            writer.Write(32u); writer.Write(0x41u); writer.Write(0u); writer.Write(32u);
            writer.Write(0x00FF0000u); writer.Write(0x0000FF00u); writer.Write(0x000000FFu); writer.Write(0xFF000000u);
            writer.Write(levels > 1 ? 0x401008u : 0x1000u);
            for (int i = 0; i < 4; i++) writer.Write(0u);
            for (int level = 0; level < levels; level++)
            {
                writer.Write(image.ToByteArray(MagickFormat.Bgra));
                if (level + 1 < levels) image.Resize(new MagickGeometry(Math.Max(1, image.Width / 2), Math.Max(1, image.Height / 2)) { IgnoreAspectRatio = true });
            }
            writer.Flush();
            var dds = data.ToArray();
            if(compress)
            {
                using var compressed = new MagickImage(dds, new MagickReadSettings { FrameCount=1, FrameIndex=0 });
                compressed.Settings.SetDefine(MagickFormat.Dds,"compression","dxt5");
                compressed.Settings.SetDefine(MagickFormat.Dds,"mipmaps","fromlist");
                // The DDS writer generates its own complete mip chain.
                compressed.Settings.SetDefine(MagickFormat.Dds,"mipmaps",(levels-1).ToString(System.Globalization.CultureInfo.InvariantCulture));
                dds = compressed.ToByteArray(MagickFormat.Dds);
            }
            var result = CodeWalkerPreview.CreateImageTexture(dds, core, model, template, textureName);
            var current = new FileInfo(source);
            if (!current.Exists || (current.LastWriteTimeUtc.Ticks, current.Length) != originalStamp)
                throw new IOException("The image is still being saved. It will reload when the save finishes.");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
            var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllBytes(temporary, result.Bytes); File.Move(temporary, destination, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return result.Name;
        }
    }
    private static uint PowerOfTwo(uint value)
    {
        uint size = 1; while (size < value && size < 8192) size *= 2; return size;
    }

    // Editing a reopened exported project or recovery point must not mutate its
    // shared backup textures. Its generated working textures belong in our cache.
    public static void RestoreWorkingCopies(ClothingProject project)
    {
        foreach(var tattoo in project.Tattoos)
        {
            if(string.IsNullOrWhiteSpace(tattoo.TexturePath)||Path.GetFullPath(tattoo.TexturePath).StartsWith(Path.GetFullPath(CacheRoot)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))continue;
            var old=tattoo.TexturePath;var path=NewDestination(tattoo.TextureName+".png");if(File.Exists(old)){Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.Copy(old,path);}tattoo.TexturePath=path;
        }
        foreach (var item in project.Items)
            foreach (var pair in item.TextureSources.ToArray())
            {
                var old = pair.Key;
                if (!item.TexturePaths.Contains(old, StringComparer.OrdinalIgnoreCase)) { item.TextureSources.Remove(old); continue; }
                var prefix = Path.GetFullPath(CacheRoot) + Path.DirectorySeparatorChar;
                if (Path.GetFullPath(old).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                var path = NewDestination(pair.Value.SourcePath);
                if (File.Exists(old)) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.Copy(old, path); }
                for (int i = 0; i < item.TexturePaths.Count; i++) if (item.TexturePaths[i].Equals(old, StringComparison.OrdinalIgnoreCase)) item.TexturePaths[i] = path;
                item.TextureSources.Remove(old); item.TextureSources[path] = pair.Value;
                if (item.TextureLabels.Remove(old, out var label)) item.TextureLabels[path] = label;
            }
    }
}
