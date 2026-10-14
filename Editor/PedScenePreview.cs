using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace DuckStudio;

public static class PedScenePreview
{
    public static readonly DependencyProperty SourceSlotProperty=DependencyProperty.RegisterAttached("SourceSlot",typeof(string),typeof(PedScenePreview),new PropertyMetadata(""));
    public static NativePreviewResult Load(string folder)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "scene.json")));
        var group = new Model3DGroup();
        var brushes = new Dictionary<string, Brush>(StringComparer.OrdinalIgnoreCase);
        var min = new Point3D(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity);
        var max = new Point3D(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity);
        foreach (var part in document.RootElement.GetProperty("meshes").EnumerateArray())
        {
            var positions = part.GetProperty("positions").EnumerateArray().Select(x => x.GetDouble()).ToArray();
            var normals = part.GetProperty("normals").EnumerateArray().Select(x => x.GetDouble()).ToArray();
            var uvs = part.GetProperty("uvs").EnumerateArray().Select(x => x.GetDouble()).ToArray();
            var indices = part.GetProperty("indices").EnumerateArray().Select(x => x.GetInt32()).ToArray();
            if (positions.Length % 3 != 0 || normals.Length != positions.Length || uvs.Length != positions.Length / 3 * 2) continue;
            var mesh = new MeshGeometry3D();
            for (var i = 0; i < positions.Length / 3; i++)
            {
                var point = new Point3D(positions[i * 3], positions[i * 3 + 2], -positions[i * 3 + 1]);
                mesh.Positions.Add(point);
                mesh.Normals.Add(new Vector3D(normals[i * 3], normals[i * 3 + 2], -normals[i * 3 + 1]));
                mesh.TextureCoordinates.Add(new Point(uvs[i * 2], uvs[i * 2 + 1]));
                min = new Point3D(Math.Min(min.X, point.X), Math.Min(min.Y, point.Y), Math.Min(min.Z, point.Z));
                max = new Point3D(Math.Max(max.X, point.X), Math.Max(max.Y, point.Y), Math.Max(max.Z, point.Z));
            }
            foreach (var index in indices) if (index >= 0 && index < mesh.Positions.Count) mesh.TriangleIndices.Add(index);
            if (mesh.TriangleIndices.Count < 3) continue;
            var texture = part.GetProperty("texture");
            Brush brush = new SolidColorBrush(Color.FromRgb(151, 174, 169));
            if (texture.ValueKind == JsonValueKind.String)
            {
                var name = texture.GetString()!;
                var slot = part.TryGetProperty("slot", out var slotValue) ? slotValue.GetString() : null;
                var mode = part.TryGetProperty("shader", out var shader)
                    ? PreviewTexture.ModeForShader(shader.GetUInt32(), part.TryGetProperty("render_bucket", out var bucket) ? bucket.GetInt32() : 0)
                    : slot is "decl" ? PreviewTexture.AlphaMode.Blend : slot is "hair" or "berd" ? PreviewTexture.AlphaMode.Cutout : PreviewTexture.AlphaMode.Opaque;
                var key = name + "|" + mode;
                if (!brushes.TryGetValue(key, out brush!))
                {
                    var path = Path.GetFullPath(Path.Combine(folder, name));
                    if (!path.StartsWith(Path.GetFullPath(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Scene texture path escapes its folder.");
                    using var stream = new MemoryStream(File.ReadAllBytes(path));
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
                    // GTA UVs address the whole atlas, not each submesh's UV bounds.
                    // RelativeToBoundingBox stretches the entire atlas into that
                    // submesh's range and corrupts details on attached equipment.
                    brush = new ImageBrush(PreviewTexture.ForMaterial(bitmap, mode))
                    {
                        Stretch = Stretch.Fill,
                        ViewportUnits = BrushMappingMode.Absolute,
                        Viewport = new Rect(0, 0, 1, 1),
                        TileMode = TileMode.Tile
                    };
                    brush.Freeze();
                    brushes[key] = brush;
                }
            }
            var material = new DiffuseMaterial(brush);
            var geometry=new GeometryModel3D(mesh, material) { BackMaterial = material };
            if(part.TryGetProperty("weights",out var weights) && part.TryGetProperty("blend_indices",out var blend))
            {
                var bones=part.TryGetProperty("bones",out var own) && own.ValueKind==JsonValueKind.Array && own.GetArrayLength()>0 ? own : document.RootElement.TryGetProperty("bones",out var shared)?shared:default;
                geometry.SetValue(PreviewPoses.SkinProperty,new SkinMesh(mesh,SkinMesh.ReadBones(bones),part.TryGetProperty("bone_ids",out var palette)&&palette.ValueKind==JsonValueKind.Array?palette.EnumerateArray().Select(x=>x.GetInt32()).ToArray():[],blend.EnumerateArray().Select(x=>x.GetInt32()).ToArray(),weights.EnumerateArray().Select(x=>x.GetSingle()).ToArray()));
            }
            geometry.SetValue(SourceSlotProperty,part.TryGetProperty("slot",out var sourceSlot)?sourceSlot.GetString()??"":"");
            group.Children.Add(geometry);
        }
        if (group.Children.Count == 0) throw new InvalidDataException("PED scene contained no renderable geometry.");
        var center = new Point3D((min.X + max.X) / 2, (min.Y + max.Y) / 2, (min.Z + max.Z) / 2);
        var extent = Math.Max(max.X - min.X, Math.Max(max.Y - min.Y, max.Z - min.Z));
        return new NativePreviewResult(group, center, Math.Max(extent, .2));
    }
}
