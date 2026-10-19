using System.IO;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace DuckStudio;

public sealed record NativePreviewResult(Model3D Model, Point3D Center, double Extent);

public static partial class CodeWalkerPreview
{
    private static Assembly? core;
    private static string? coreDirectory;
    private static readonly ConcurrentDictionary<string, (long Ticks, long Length, bool HasDiffuse)> EmbeddedDiffuseCache = new(StringComparer.OrdinalIgnoreCase);

    public static bool HasEmbeddedDiffuseTexture(string yddPath, string coreDll)
    {
        if (!File.Exists(yddPath) || !File.Exists(coreDll)) return false;
        var file = new FileInfo(yddPath);
        var fullPath = file.FullName;
        if (EmbeddedDiffuseCache.TryGetValue(fullPath, out var cached) && cached.Ticks == file.LastWriteTimeUtc.Ticks && cached.Length == file.Length)
            return cached.HasDiffuse;
        try
        {
            EnsureCore(coreDll);
            var yddType = core!.GetType("CodeWalker.GameFiles.YddFile", true)!;
            var ydd = Activator.CreateInstance(yddType)!;
            yddType.GetMethod("Load", [typeof(byte[])])!.Invoke(ydd, [File.ReadAllBytes(fullPath)]);
            var drawables = (Array?)Member(ydd, "Drawables");
            var found = false;
            if (drawables != null)
                foreach (var drawable in drawables)
                {
                    if (drawable == null) continue;
                    var shaderGroup = Member(drawable, "ShaderGroup");
                    var shaderArray = shaderGroup == null ? null : Member(shaderGroup, "Shaders");
                    var shaders = shaderArray == null ? null : (Array?)Member(shaderArray, "data_items");
                    if (shaders == null) continue;
                    foreach (var shader in shaders)
                    {
                        if (shader == null) continue;
                        var list = Member(shader, "ParametersList");
                        if (list == null) continue;
                        var parameters = (Array?)Member(list, "Parameters");
                        var hashes = (Array?)Member(list, "Hashes");
                        if (parameters == null || hashes == null) continue;
                        for (var i = 0; i < Math.Min(parameters.Length, hashes.Length); i++)
                        {
                            if (!string.Equals(hashes.GetValue(i)?.ToString(), "diffusesampler", StringComparison.OrdinalIgnoreCase)) continue;
                            var parameter = parameters.GetValue(i);
                            var texture = parameter == null ? null : Member(parameter, "Data");
                            if (texture?.GetType().FullName == "CodeWalker.GameFiles.Texture") { found = true; break; }
                        }
                        if (found) break;
                    }
                    if (found) break;
                }
            EmbeddedDiffuseCache[fullPath] = (file.LastWriteTimeUtc.Ticks, file.Length, found);
            return found;
        }
        catch (Exception) { return false; }
    }

    public static NativePreviewResult Load(string yddPath, string? ytdPath, string coreDll, int lod = 0)
    {
        EnsureCore(coreDll);
        var yddType = core!.GetType("CodeWalker.GameFiles.YddFile", true)!;
        var ydd = Activator.CreateInstance(yddType)!;
        yddType.GetMethod("Load", [typeof(byte[])])!.Invoke(ydd, [File.ReadAllBytes(yddPath)]);
        var drawables = (Array)Member(ydd, "Drawables")!;
        if (drawables.Length == 0) throw new InvalidDataException("YDD has no drawable.");
        var drawable = drawables.GetValue(0)!;
        var modelsBlock = Member(drawable, "DrawableModels")!;
        var models = (Array?)Member(modelsBlock, new[]{"High","Med","Low","VLow"}[Math.Clamp(lod,0,3)]);
        if (models is not { Length: > 0 }) throw new InvalidDataException("YDD has no high-detail model.");

        var bitmap = LoadTexture(ytdPath, modelPath: yddPath);
        Brush brush = bitmap is not null
            ? new ImageBrush(PreviewTexture.ForMaterial(bitmap, PreviewTexture.AlphaMode.Opaque))
            {
                Stretch = Stretch.Fill,
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, 1, 1),
                TileMode = TileMode.Tile
            }
            : new SolidColorBrush(Color.FromRgb(105, 179, 167));
        var material = new DiffuseMaterial(brush);
        var group = new Model3DGroup();
        var min = new Point3D(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity);
        var max = new Point3D(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity);

        foreach (var model in models)
        {
            if (model == null) continue;
            var geometries = (Array?)Member(model, "Geometries");
            if (geometries == null) continue;
            foreach (var geometry in geometries)
            {
                if (geometry == null) continue;
                var data = Member(geometry, "VertexData");
                var buffer = Member(geometry, "VertexBuffer");
                var indexBuffer = Member(geometry, "IndexBuffer");
                if (data == null || buffer == null || indexBuffer == null) continue;
                var declaration = Member(buffer, "Info");
                if (declaration == null) continue;
                var dataType = data.GetType();
                var declType = declaration.GetType();
                var semantics=Convert.ToUInt32(Member(declaration,"Flags"));
                if((semantics&1)==0)continue;
                var positionOffset = (int)declType.GetMethod("GetComponentOffset")!.Invoke(declaration, [0])!;
                var uvOffset = (int)declType.GetMethod("GetComponentOffset")!.Invoke(declaration, [6])!;
                var vertexCount = (int)Member(data, "VertexCount")!;
                var indices = (ushort[]?)Member(indexBuffer, "Indices");
                if (indices == null || vertexCount <= 0 || vertexCount > 500_000) continue;
                var positions = dataType.GetMethod("GetVector3")!;
                var uvs = dataType.GetMethod("GetVector2")!;
                var stringReader=dataType.GetMethod("GetString")!;
                double[] Read(int vertex,int component)=>((string)stringReader.Invoke(data,[vertex,component," "])!).Split(' ',StringSplitOptions.RemoveEmptyEntries).Select(x=>double.Parse(x,System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                var mesh = new MeshGeometry3D();
                for (var i = 0; i < vertexCount; i++)
                {
                    var vector=Read(i,0);
                    var x = vector[0];
                    var y = vector[1];
                    var z = vector[2];
                    var point = new Point3D(x, z, -y);
                    mesh.Positions.Add(point);
                    min = new Point3D(Math.Min(min.X, point.X), Math.Min(min.Y, point.Y), Math.Min(min.Z, point.Z));
                    max = new Point3D(Math.Max(max.X, point.X), Math.Max(max.Y, point.Y), Math.Max(max.Z, point.Z));
                    if ((semantics & (1u<<6)) != 0)
                    {
                        // GetVector2/GetString take the semantic index, not a byte
                        // offset. Handle half and packed layouts through GetString.
                        var uv = Read(i,6);
                        mesh.TextureCoordinates.Add(new Point(uv[0],uv[1]));
                    }
                    if((semantics & (1u<<3)) != 0)
                    {
                        var normal=Read(i,3);mesh.Normals.Add(new Vector3D(normal[0],normal[2],-normal[1]));
                    }
                }
                foreach (var index in indices) if (index < vertexCount) mesh.TriangleIndices.Add(index);
                var geometryMaterial = material;
                if (bitmap != null && Member(geometry, "Shader") is { } shader)
                {
                    var fileName = Member(shader, "FileName")!;
                    var hash = Convert.ToUInt32(Member(fileName, "Hash") ?? fileName);
                    var mode = PreviewTexture.ModeForShader(hash, Convert.ToInt32(Member(shader, "RenderBucket")));
                    if (mode != PreviewTexture.AlphaMode.Opaque)
                        geometryMaterial = new DiffuseMaterial(new ImageBrush(PreviewTexture.ForMaterial(bitmap, mode))
                        { Stretch = Stretch.Fill, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 1, 1), TileMode = TileMode.Tile });
                }
                if (mesh.TriangleIndices.Count >= 3) group.Children.Add(new GeometryModel3D(mesh, geometryMaterial) { BackMaterial = geometryMaterial });
            }
        }
        if (group.Children.Count == 0) throw new InvalidDataException("No renderable geometry found in YDD.");
        var center = new Point3D((min.X + max.X) / 2, (min.Y + max.Y) / 2, (min.Z + max.Z) / 2);
        var extent = Math.Max(max.X - min.X, Math.Max(max.Y - min.Y, max.Z - min.Z));
        return new NativePreviewResult(group, center, Math.Max(extent, .2));
    }

    public static BitmapSource? LoadTextureFile(string ytdPath, string coreDll, string? textureName = null, string? modelPath = null)
    {
        EnsureCore(coreDll);
        return LoadTexture(ytdPath, textureName, modelPath);
    }

    public static (byte[] Bytes, string Name) CreateImageTexture(byte[] dds, string coreDll, string modelPath, string? templatePath, string? requestedName)
    {
        EnsureCore(coreDll);
        var ytdType = core!.GetType("CodeWalker.GameFiles.YtdFile", true)!;
        var textureType = core.GetType("CodeWalker.GameFiles.Texture", true)!;
        var dictType = core.GetType("CodeWalker.GameFiles.TextureDictionary", true)!;
        var ytd = Activator.CreateInstance(ytdType)!;
        var textures = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(textureType))!;
        if (File.Exists(templatePath))
        {
            ytdType.GetMethod("Load", [typeof(byte[])])!.Invoke(ytd, [File.ReadAllBytes(templatePath!)]);
            if (Member(ytd, "TextureDict") is { } dictionary && Member(dictionary, "Textures") is { } collection && Member(collection, "data_items") is Array existing)
                foreach (var texture in existing) if (texture != null) textures.Add(texture);
        }
        var names = DiffuseTextureNames(modelPath);
        var name = !string.IsNullOrWhiteSpace(requestedName) ? requestedName! : names.FirstOrDefault(n => textures.Cast<object>().Any(t => string.Equals(Member(t, "Name")?.ToString(), n, StringComparison.OrdinalIgnoreCase)))
            ?? DiffuseBinding.Select(textures.Cast<object>().Select(t => Member(t, "Name")?.ToString() ?? ""), names, MaterialTextureNames(modelPath, false))
            ?? names.FirstOrDefault()
            ?? throw new InvalidDataException("Could not identify the model's diffuse texture. Attach its original YTD first.");
        var imported = core.GetType("CodeWalker.Utils.DDSIO", false) ?? core.GetTypes().First(t => t.Name == "DDSIO");
        var decoded = imported.GetMethod("GetTexture", [typeof(byte[])])!.Invoke(null, [dds])
            ?? throw new InvalidDataException("Could not create the image texture.");
        var target = textures.Cast<object>().FirstOrDefault(t => string.Equals(Member(t, "Name")?.ToString(), name, StringComparison.OrdinalIgnoreCase));
        if (target == null)
        {
            target = decoded;
            textureType.GetProperty("Name")!.SetValue(target, name);
            var usage = textureType.GetProperty("Usage")!;
            usage.SetValue(target, Enum.Parse(usage.PropertyType, "DIFFUSE"));
            var hashType = core.GetTypes().First(t => t.Name == "JenkHash");
            textureType.GetProperty("NameHash")!.SetValue(target, hashType.GetMethod("GenHash", [typeof(string)])!.Invoke(null, [name.ToLowerInvariant()]));
            textures.Add(target);
        }
        else foreach (var property in new[] { "Width", "Height", "Depth", "Levels", "Format", "Stride", "Data" })
            textureType.GetProperty(property)!.SetValue(target, Member(decoded, property));
        var dict = Activator.CreateInstance(dictType)!;
        dictType.GetMethod("BuildFromTextureList")!.Invoke(dict, [textures]);
        ytdType.GetProperty("TextureDict")!.SetValue(ytd, dict);
        return ((byte[])ytdType.GetMethod("Save", Type.EmptyTypes)!.Invoke(ytd, null)!, name);
    }

    private static List<string> DiffuseTextureNames(string modelPath) => MaterialTextureNames(modelPath, true);

    private static List<string> MaterialTextureNames(string modelPath, bool diffuse)
    {
        var names = new List<string>();
        if (!File.Exists(modelPath)) return names;
        var yddType = core!.GetType("CodeWalker.GameFiles.YddFile", true)!;
        var ydd = Activator.CreateInstance(yddType)!;
        yddType.GetMethod("Load", [typeof(byte[])])!.Invoke(ydd, [File.ReadAllBytes(modelPath)]);
        if (Member(ydd, "Drawables") is not Array drawables) return names;
        foreach (var drawable in drawables)
        {
            if (drawable == null || Member(drawable, "ShaderGroup") is not { } group || Member(group, "Shaders") is not { } shaderList || Member(shaderList, "data_items") is not Array shaders) continue;
            foreach (var shader in shaders)
            {
                if (shader == null || Member(shader, "ParametersList") is not { } list || Member(list, "Parameters") is not Array parameters || Member(list, "Hashes") is not Array hashes) continue;
                for (int i = 0; i < Math.Min(parameters.Length, hashes.Length); i++)
                {
                    if (string.Equals(hashes.GetValue(i)?.ToString(), "diffusesampler", StringComparison.OrdinalIgnoreCase) != diffuse) continue;
                    if (parameters.GetValue(i) is { } parameter && Member(parameter, "Data") is { } data && Member(data, "Name") is string name && !string.IsNullOrWhiteSpace(name) && !names.Contains(name)) names.Add(name);
                }
            }
        }
        return names;
    }

    public static string ExportXmlFile(string assetPath, string outputFolder, string coreDll)
    {
        EnsureCore(coreDll);
        var isTexture = Path.GetExtension(assetPath).Equals(".ytd", StringComparison.OrdinalIgnoreCase);
        var fileType = core!.GetType(isTexture ? "CodeWalker.GameFiles.YtdFile" : "CodeWalker.GameFiles.YddFile", true)!;
        var xmlType = core.GetType(isTexture ? "CodeWalker.GameFiles.YtdXml" : "CodeWalker.GameFiles.YddXml", true)!;
        var file = Activator.CreateInstance(fileType)!;
        fileType.GetMethod("Load", [typeof(byte[])])!.Invoke(file, [File.ReadAllBytes(assetPath)]);
        return (string)xmlType.GetMethod("GetXml")!.Invoke(null, [file, outputFolder])!;
    }

    public static int ExportTexturePngs(string ytdPath, string outputFolder, string coreDll)
    {
        EnsureCore(coreDll);
        Directory.CreateDirectory(outputFolder);
        var ytdType = core!.GetType("CodeWalker.GameFiles.YtdFile", true)!;
        var ytd = Activator.CreateInstance(ytdType)!;
        ytdType.GetMethod("Load", [typeof(byte[])])!.Invoke(ytd, [File.ReadAllBytes(ytdPath)]);
        var dictionary = Member(ytd, "TextureDict");
        var textures = dictionary == null ? null : Member(dictionary, "Textures");
        var entries = textures == null ? null : (Array?)Member(textures, "data_items");
        if (entries == null) return 0;
        var count = 0;
        foreach (var entry in entries)
        {
            if (entry == null) continue;
            var width = Convert.ToInt32(Member(entry, "Width"));
            var height = Convert.ToInt32(Member(entry, "Height"));
            var format = Member(entry, "Format")?.ToString();
            var data = Member(entry, "Data");
            var bytes = data == null ? null : (byte[]?)Member(data, "FullData");
            if (bytes == null || width <= 0 || height <= 0 || width > 8192 || height > 8192) continue;
            byte[]? pixels = format switch
            {
                "D3DFMT_DXT1" => DecodeBlockTexture(bytes, width, height, 8, false),
                "D3DFMT_DXT3" => DecodeBlockTexture(bytes, width, height, 16, false, true),
                "D3DFMT_DXT5" => DecodeBlockTexture(bytes, width, height, 16, true),
                "D3DFMT_A8R8G8B8" => bytes.Take(width * height * 4).ToArray(),
                _ => null
            };
            if (pixels == null) continue;
            var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
            var name = Member(entry, "Name")?.ToString() ?? $"texture_{count:000}";
            name = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            var target = Path.Combine(outputFolder, name + ".png");
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(target); encoder.Save(output);
            count++;
        }
        return count;
    }

    public static byte[] CompileMetadata(string xml, string coreDll)
    {
        EnsureCore(coreDll);
        var document = new System.Xml.XmlDocument();
        document.LoadXml(xml);
        return (byte[]?)core!.GetType("CodeWalker.GameFiles.XmlMeta", true)!.GetMethod("GetRSCData")!.Invoke(null, [document])
            ?? throw new InvalidDataException("Could not compile clothing metadata.");
    }

    private static void EnsureCore(string path)
    {
        path = Path.GetFullPath(path);
        if (core != null && coreDirectory == Path.GetDirectoryName(path)) return;
        coreDirectory = Path.GetDirectoryName(path)!;
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            var dependency = Path.Combine(coreDirectory, name.Name + ".dll");
            return File.Exists(dependency) ? Assembly.LoadFrom(dependency) : null;
        };
        core = Assembly.LoadFrom(path);
    }

    private static object? Member(object value, string name)
    {
        var type = value.GetType();
        return type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(value)
            ?? type.GetField(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(value);
    }

    private static BitmapSource? LoadTexture(string? path, string? textureName = null, string? modelPath = null)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        var ytdType = core!.GetType("CodeWalker.GameFiles.YtdFile", true)!;
        var ytd = Activator.CreateInstance(ytdType)!;
        ytdType.GetMethod("Load", [typeof(byte[])])!.Invoke(ytd, [File.ReadAllBytes(path)]);
        var dictionary = Member(ytd, "TextureDict");
        var textures = dictionary == null ? null : Member(dictionary, "Textures");
        var entries = textures == null ? null : (Array?)Member(textures, "data_items");
        if (entries is not { Length: > 0 }) return null;
        var names = textureName != null ? new List<string> { textureName } : modelPath != null ? DiffuseTextureNames(modelPath) : [];
        var selected = DiffuseBinding.Select(entries.Cast<object>().Select(t => Member(t, "Name")?.ToString() ?? ""), names, modelPath != null ? MaterialTextureNames(modelPath, false) : []);
        var texture = entries.Cast<object>().FirstOrDefault(t => string.Equals(Member(t, "Name")?.ToString(), selected, StringComparison.OrdinalIgnoreCase));
        if (texture == null) return null;
        var width = Convert.ToInt32(Member(texture, "Width"));
        var height = Convert.ToInt32(Member(texture, "Height"));
        var format = Member(texture, "Format")?.ToString();
        var bytes = (byte[]?)Member(Member(texture, "Data")!, "FullData");
        if (bytes == null || width <= 0 || height <= 0 || width > 8192 || height > 8192) return null;
        byte[]? pixels = format switch
        {
            "D3DFMT_DXT1" => DecodeBlockTexture(bytes, width, height, 8, false),
            "D3DFMT_DXT3" => DecodeBlockTexture(bytes, width, height, 16, false, true),
            "D3DFMT_DXT5" => DecodeBlockTexture(bytes, width, height, 16, true),
            "D3DFMT_A8R8G8B8" => bytes.Take(width * height * 4).ToArray(),
            _ => null
        };
        if (pixels == null) return null;
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static byte[] DecodeBlockTexture(byte[] input, int width, int height, int blockSize, bool dxt5, bool dxt3 = false)
    {
        var output = new byte[width * height * 4];
        var blocksX = (width + 3) / 4;
        var blocksY = (height + 3) / 4;
        if (input.Length < blocksX * blocksY * blockSize) throw new InvalidDataException("Texture data is truncated.");
        for (var by = 0; by < blocksY; by++)
        for (var bx = 0; bx < blocksX; bx++)
        {
            var block = (by * blocksX + bx) * blockSize;
            var color = block + blockSize - 8;
            var c0 = BitConverter.ToUInt16(input, color);
            var c1 = BitConverter.ToUInt16(input, color + 2);
            var colors = new byte[4, 4];
            Set565(colors, 0, c0); Set565(colors, 1, c1);
            for (var channel = 0; channel < 3; channel++)
            {
                if (c0 > c1 || blockSize == 16)
                {
                    colors[2, channel] = (byte)((2 * colors[0, channel] + colors[1, channel]) / 3);
                    colors[3, channel] = (byte)((colors[0, channel] + 2 * colors[1, channel]) / 3);
                }
                else { colors[2, channel] = (byte)((colors[0, channel] + colors[1, channel]) / 2); colors[3, channel] = 0; }
            }
            colors[0, 3] = 255; colors[1, 3] = 255; colors[2, 3] = 255; colors[3, 3] = c0 > c1 || blockSize == 16 ? (byte)255 : (byte)0;
            var colorBits = BitConverter.ToUInt32(input, color + 4);
            var alphaTable = new byte[8];
            ulong alphaBits = 0;
            if (dxt5)
            {
                alphaTable[0] = input[block]; alphaTable[1] = input[block + 1];
                for (var i = 2; i < 8; i++) alphaTable[i] = alphaTable[0] > alphaTable[1]
                    ? (byte)(((8 - i) * alphaTable[0] + (i - 1) * alphaTable[1]) / 7)
                    : i < 6 ? (byte)(((6 - i) * alphaTable[0] + (i - 1) * alphaTable[1]) / 5) : (byte)(i == 6 ? 0 : 255);
                for (var i = 0; i < 6; i++) alphaBits |= (ulong)input[block + 2 + i] << (8 * i);
            }
            for (var py = 0; py < 4; py++)
            for (var px = 0; px < 4; px++)
            {
                var x = bx * 4 + px; var y = by * 4 + py;
                if (x >= width || y >= height) continue;
                var pixelIndex = py * 4 + px;
                var selected = (int)((colorBits >> (2 * pixelIndex)) & 3);
                var outputIndex = (y * width + x) * 4;
                for (var c = 0; c < 3; c++) output[outputIndex + c] = colors[selected, c];
                output[outputIndex + 3] = dxt5 ? alphaTable[(alphaBits >> (3 * pixelIndex)) & 7] : dxt3
                    ? (byte)((((ulong)BitConverter.ToUInt64(input, block) >> (4 * pixelIndex)) & 15) * 17)
                    : colors[selected, 3];
            }
        }
        return output;
    }

    private static void Set565(byte[,] colors, int row, ushort encoded)
    {
        colors[row, 0] = (byte)((encoded & 31) * 255 / 31);
        colors[row, 1] = (byte)(((encoded >> 5) & 63) * 255 / 63);
        colors[row, 2] = (byte)(((encoded >> 11) & 31) * 255 / 31);
    }
}
