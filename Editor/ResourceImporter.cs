using System.IO;
using System.Xml.Linq;
using System.Text.RegularExpressions;

namespace DuckStudio;

public static class ResourceImporter
{
    public static string? FindAdjacentBuild(string dctProjectPath)
    {
        var parent = Path.GetDirectoryName(dctProjectPath);
        if (parent == null) return null;
        var stem = Path.GetFileNameWithoutExtension(dctProjectPath);
        var dataFolder = Directory.GetDirectories(parent, "clothproject_*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(x => Path.GetFileName(x).Equals("clothproject_" + stem, StringComparison.OrdinalIgnoreCase));
        if (dataFolder == null) return null;
        return Directory.GetDirectories(dataFolder).FirstOrDefault(x => File.Exists(Path.Combine(x, "fxmanifest.lua")));
    }

    public static ClothingProject Import(string resourcePath)
    {
        if (!File.Exists(Path.Combine(resourcePath, "fxmanifest.lua")))
            throw new InvalidDataException("The selected folder has no fxmanifest.lua.");

        var project = new ClothingProject { Name = Path.GetFileName(resourcePath), SourceResourcePath = resourcePath };
        var metas = Directory.GetFiles(resourcePath, "*.meta", SearchOption.TopDirectoryOnly);
        foreach (var meta in metas)
        {
            XDocument xml;
            try { xml = XDocument.Load(meta); }
            catch { continue; }
            if (xml.Root?.Name.LocalName != "ShopPedApparel") continue;
            var ped = xml.Root.Element("pedName")?.Value;
            var full = xml.Root.Element("fullDlcName")?.Value;
            if (ped is not ("mp_m_freemode_01" or "mp_f_freemode_01") || string.IsNullOrWhiteSpace(full)) continue;
            if (full.StartsWith(ped + "_", StringComparison.OrdinalIgnoreCase))
                project.Collection = full[(ped.Length + 1)..];
            var ymt = Directory.GetFiles(Path.Combine(resourcePath, "stream"), full + ".ymt", SearchOption.AllDirectories).FirstOrDefault();
            if (ped == "mp_m_freemode_01") { project.MaleMetaPath = meta; project.MaleYmtPath = ymt ?? ""; }
            else { project.FemaleMetaPath = meta; project.FemaleYmtPath = ymt ?? ""; }
        }

        var stream = Path.Combine(resourcePath, "stream");
        if (!Directory.Exists(stream)) throw new InvalidDataException("The resource has no stream folder.");
        var drawables = Directory.GetFiles(stream, "*.ydd", SearchOption.AllDirectories).OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var path in drawables)
        {
            if (!ProjectIO.TryParseDrawable(path, out var slot, out var number)) continue;
            if(Regex.IsMatch(path,@"_[urm]_\d+\.ydd$",RegexOptions.IgnoreCase)) continue;
            var ped = ProjectIO.InferPed(path);
            project.Items.Add(new ClothingAsset { Name = ProjectIO.SlotLabel(slot).Split('[','(')[0].Trim(), Ped = ped, Slot = slot, Drawable = number, ModelPath = path });
        }
        foreach(var item in project.Items)
        {
            var basePath=Path.Combine(Path.GetDirectoryName(item.ModelPath)!,Path.GetFileNameWithoutExtension(item.ModelPath));
            if(File.Exists(basePath+"_1.ydd"))item.FirstPersonPath=basePath+"_1.ydd";
            if(File.Exists(basePath+".yld"))item.PhysicsPath=basePath+".yld";
        }
        foreach (var path in Directory.GetFiles(stream, "*.ytd", SearchOption.AllDirectories).OrderBy(x=>x,StringComparer.OrdinalIgnoreCase))
        {
            if (!ProjectIO.TryParseTexture(path, out var slot, out var number)) continue;
            var ped = ProjectIO.InferPed(path);
            var item = project.Items.FirstOrDefault(x => x.Ped == ped && x.Slot == slot && x.Drawable == number && ProjectIO.SourceCollection(x.ModelPath)==ProjectIO.SourceCollection(path));
            item?.TexturePaths.Add(path);
        }
        if (project.Items.Count == 0) throw new InvalidDataException("No recognized clothing drawables were found in stream.");
        return project;
    }
}
