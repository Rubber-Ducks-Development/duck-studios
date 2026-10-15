using System.IO;
using System.Text.Json;
using DuckStudio;
using DuckStudio.Setup;
using Microsoft.Win32;

static class ProjectFileChecks
{
    public static void Run(string root, Action<bool, string> check)
    {
        var folder = Directory.CreateDirectory(Path.Combine(root, "ProjectFiles")).FullName;
        var asset = Path.Combine(folder, "source.ydd");
        File.WriteAllBytes(asset, [11, 22, 33]);
        var original = new ClothingProject { Name = "Migration", Collection = "migration", HistoryId = "stable-history", Items = [new() { ModelPath = asset }] };
        foreach (var extension in new[] { ".duckproj", ".DUCKPROJ", ".weftproj", ".duck" })
        {
            var path = Path.Combine(folder, "legacy" + extension);
            File.WriteAllText(path, JsonSerializer.Serialize(original, ProjectIO.Json));
            var before = File.ReadAllBytes(path);
            var project = ProjectIO.Load(path);
            check(project.Items.Single().ModelPath == asset && project.HistoryId == "stable-history", extension + " opens with asset links and history intact");
            var migrated = ProjectFiles.SuggestedSavePath(path);
            project.Name = "Saved copy";
            ProjectIO.Save(project, migrated);
            check(ProjectIO.Load(migrated).Name == "Saved copy", extension + " saves as a readable .duckproj");
            if (!ProjectFiles.IsCurrentProject(path)) check(File.ReadAllBytes(path).SequenceEqual(before), "Migrating " + extension + " preserves the original file");
        }
        MixedBackups(folder, check);
        DurtyImport(folder, check);
        PortableExport(folder, check);
        Associations(check);
    }

    static void MixedBackups(string root, Action<bool, string> check)
    {
        var backups = new ProjectBackups(Path.Combine(root, "Backups"), 3);
        var firstSource = Path.Combine(root, "first.ytd"); File.WriteAllBytes(firstSource, [101]);
        var secondSource = Path.Combine(root, "second.ytd"); File.WriteAllBytes(secondSource, [202]);
        ClothingProject Snapshot(string source) => new() { Name = "Mixed", Collection = "mixed", Items = [new() { ModelPath = source }] };
        var first = backups.Capture(Snapshot(firstSource), "same-project")!;
        var firstLegacy = Path.ChangeExtension(first, ".weftproj"); File.Move(first, firstLegacy);
        File.SetLastWriteTimeUtc(firstLegacy, DateTime.UtcNow.AddMinutes(-3));
        var firstAsset = ProjectIO.Load(firstLegacy).Items.Single().ModelPath;
        var second = backups.Capture(Snapshot(secondSource), "same-project")!;
        var secondLegacy = Path.ChangeExtension(second, ".duck"); File.Move(second, secondLegacy);
        File.SetLastWriteTimeUtc(secondLegacy, DateTime.UtcNow.AddMinutes(-2));
        var secondAsset = ProjectIO.Load(secondLegacy).Items.Single().ModelPath;
        var current = backups.Capture(new() { Name = "No assets", Collection = "mixed" }, "same-project")!;
        File.SetLastWriteTimeUtc(current, DateTime.UtcNow.AddMinutes(-1));
        check(Path.GetExtension(current) == ".duckproj", "New recovery snapshots use .duckproj");
        check(File.Exists(firstLegacy) && File.Exists(secondLegacy) && File.Exists(firstAsset) && File.Exists(secondAsset), "Legacy recovery snapshots retain their shared asset files after upgrade");
        check(File.ReadAllBytes(firstAsset).SequenceEqual(new byte[] { 101 }) && File.ReadAllBytes(secondAsset).SequenceEqual(new byte[] { 202 }), "Legacy recovery assets restore with their original bytes");
        var newest = new ProjectBackups(backups.Root, 2).Capture(new() { Name = "Newest", Collection = "mixed" }, "same-project")!;
        check(ProjectFiles.EnumerateNativeProjects(Path.GetDirectoryName(newest)!).Count() == 2 && File.Exists(current) && !File.Exists(firstLegacy) && !File.Exists(secondLegacy), "Retention counts current and legacy recovery formats together");
        check(!File.Exists(firstAsset) && !File.Exists(secondAsset), "Backup assets are pruned only after their last retained snapshot expires");
    }

    static void DurtyImport(string root, Action<bool, string> check)
    {
        var path = Path.Combine(root, "uniform.DCTPROJ");
        File.WriteAllText(path, "Durty Cloth fixture; the adjacent build supplies the assets.");
        var built = Directory.CreateDirectory(Path.Combine(root, "clothproject_uniform", "uniform_build")).FullName;
        File.WriteAllText(Path.Combine(built, "fxmanifest.lua"), "fx_version 'cerulean'\ngame 'gta5'\n");
        var stream = Directory.CreateDirectory(Path.Combine(built, "stream")).FullName;
        var model = Path.Combine(stream, "mp_m_freemode_01_mp_m_uniform^jbib_000_u.ydd");
        var texture = Path.Combine(stream, "mp_m_freemode_01_mp_m_uniform^jbib_diff_000_a_uni.ytd");
        File.WriteAllBytes(model, [1, 2]); File.WriteAllBytes(texture, [3, 4]);
        var before = File.ReadAllBytes(path);
        var imported = ResourceImporter.ImportDurtyProject(path);
        check(imported.Items.Count == 1 && imported.Items[0].ModelPath == model && imported.Items[0].TexturePaths.Single() == texture, "Durty Cloth .dctproj still imports its adjacent built FiveM clothing resource");
        ProjectIO.Save(imported, ProjectFiles.SuggestedSavePath(path));
        check(File.ReadAllBytes(path).SequenceEqual(before) && ProjectIO.Load(Path.ChangeExtension(path, ".duckproj")).Items.Count == 1, "Imported Durty Cloth project saves independently as .duckproj without changing .dctproj");
        var missing = Path.Combine(root, "unbuilt.dctproj"); File.WriteAllText(missing, "fixture");
        var rejected = false;
        try { ResourceImporter.ImportDurtyProject(missing); }
        catch (FileNotFoundException ex) { rejected = ex.Message.Contains("Build it in Durty Cloth Tool"); }
        check(rejected, "Unbuilt Durty Cloth projects explain the required adjacent build");
    }

    static void PortableExport(string root, Action<bool, string> check)
    {
        // Prebuilt byte fixtures exercise packaging and paths, not GTA rendering.
        var texture = Path.Combine(root, "tattoo.ytd"); File.WriteAllBytes(texture, [41, 42]);
        var metadata = Path.Combine(root, "mp_creaturemetadata_portable.ymt"); File.WriteAllBytes(metadata, [43, 44]);
        var project = new ClothingProject { Name = "Portable", Collection = "portable", OutputFolder = root,
            SupplementalYmtPaths = [metadata], Tattoos = [new() { TextureName = "test_tattoo", TexturePath = texture }] };
        var target = PackBuilder.Build(project, Path.Combine(root, "Exports"));
        var path = Path.Combine(target, "Project", "portable.duckproj");
        check(File.Exists(path) && !Directory.GetFiles(Path.GetDirectoryName(path)!, "*.weftproj").Any(), "Pack builds export a .duckproj workspace");
        var reopened = ProjectIO.Load(path);
        check(File.ReadAllBytes(reopened.Tattoos.Single().TexturePath).SequenceEqual(new byte[] { 41, 42 }), "Exported .duckproj reopens its bundled relative assets");
        var relocated = target + "-moved"; Directory.Move(target, relocated);
        reopened = ProjectIO.Load(Path.Combine(relocated, "Project", "portable.duckproj"));
        check(File.Exists(reopened.Tattoos.Single().TexturePath) && File.ReadAllText(Path.Combine(relocated, "Project", "README.txt")).Contains("portable.duckproj"), "Relocated exports retain usable links and updated restore instructions");
    }

    static void Associations(Action<bool, string> check)
    {
        // Use a disposable registry subtree, never the user's actual associations.
        var keyPath = @"Software\DuckStudio.Tests\" + Guid.NewGuid().ToString("N");
        try
        {
            using var classes = Registry.CurrentUser.CreateSubKey(keyPath);
            using (var durty = classes.CreateSubKey(".dctproj")) durty.SetValue("", "DurtyCloth.Project");
            using (var other = classes.CreateSubKey(@".dctproj\OpenWithProgids")) other.SetValue("DurtyCloth.Project", Array.Empty<byte>(), RegistryValueKind.None);
            var target = @"C:\Duck Studio Test";
            InstallEngine.RegisterProjectFiles(classes, target);
            foreach (var extension in new[] { ".duckproj", ".duck", ".weftproj", ".dctproj" })
            {
                using var registered = classes.OpenSubKey(extension + @"\OpenWithProgids");
                check(registered?.GetValueNames().Contains("DuckStudio.Project") == true, "Installer offers Open With for " + extension);
            }
            using (var command = classes.OpenSubKey(@"DuckStudio.Project\shell\open\command"))
                check((string?)command?.GetValue("") == "\"C:\\Duck Studio Test\\DuckStudio.exe\" \"%1\"", "Open With quotes both the executable and project path");
            InstallEngine.UnregisterProjectFiles(classes);
            foreach (var extension in new[] { ".duckproj", ".duck", ".weftproj", ".dctproj" })
            {
                using var registered = classes.OpenSubKey(extension + @"\OpenWithProgids");
                check(registered?.GetValue("DuckStudio.Project") == null, "Uninstall removes Duck Studio's Open With entry for " + extension);
            }
            using var durtyDefault = classes.OpenSubKey(".dctproj");
            using var durtyOther = classes.OpenSubKey(@".dctproj\OpenWithProgids");
            check((string?)durtyDefault?.GetValue("") == "DurtyCloth.Project" && durtyOther?.GetValueNames().Contains("DurtyCloth.Project") == true, "Install and uninstall preserve Durty Cloth's default app and Open With entry");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(keyPath, false); }
    }
}
