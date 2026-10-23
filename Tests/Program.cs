using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using DuckStudio;

static class Program
{
    static int checks;
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
    [STAThread]static void Main(string[] args)
    {
        if (PerformanceChecks.RunReaderFixture(args)) return;
        var root=Path.Combine(Environment.GetEnvironmentVariable("DUCK_STUDIO_TEST_ROOT")??Path.Combine(Environment.CurrentDirectory,"work","DuckStudioTests"),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var model=Path.Combine(root,"model.ydd");var texture=Path.Combine(root,"texture.ytd");var image=Path.Combine(root,"art.png");File.WriteAllBytes(model,[1,2,3]);File.WriteAllBytes(texture,[4,5,6]);File.WriteAllBytes(image,[7,8,9]);
        var asset=new ClothingAsset{Name="Vest",Slot="task",Drawable=12,ModelPath=model,TexturePaths=[texture],TextureSources=new(){[texture]=new(){SourcePath=image,TextureName="vest"}}};
        var project=new ClothingProject{Name="Tests",Collection="duck_tests",OutputFolder=root,Items=[asset]};
        var journal=new EditJournal(Path.Combine(root,"Undo"));journal.Reset(project);asset.Name="New name";journal.Observe(project);project.Items.Clear();journal.Observe(project);
        var restored=journal.Undo();Check(restored.Items.Count==1&&restored.Items[0].Name=="New name","Undo restores deleted drawable");
        restored=journal.Undo();Check(restored.Items[0].Name=="Vest","Undo restores metadata");Check(restored.Items[0].TextureSources.Values.Single().SourcePath==image,"Unchanged original artwork remains linked");Check(File.ReadAllBytes(restored.Items[0].ModelPath).SequenceEqual(new byte[]{1,2,3}),"Restored model retains its bytes");
        Check(!journal.Observe(restored)&&journal.CanRedo,"Idle observation preserves redo");restored=journal.Redo();Check(restored.Items[0].Name=="New name","Redo restores metadata after path remapping");restored.Name="Branch";journal.Observe(restored);Check(!journal.CanRedo,"New edit clears redo");
        project=restored;journal.Reset(project);var before=File.ReadAllBytes(project.Items[0].TexturePaths[0]);File.WriteAllBytes(project.Items[0].TexturePaths[0],[99]);File.WriteAllBytes(image,[88]);journal.Observe(project);restored=journal.Undo();Check(File.ReadAllBytes(restored.Items[0].TexturePaths[0]).SequenceEqual(before),"Undo restores overwritten texture bytes");Check(restored.Items[0].TextureSources.Values.Single().SourcePath!=image&&File.ReadAllBytes(restored.Items[0].TextureSources.Values.Single().SourcePath).SequenceEqual(new byte[]{7,8,9}),"Changed artwork restores independently");
        var male=restored.Items[0];var first=ProjectAssets.Clone(restored).Items[0];first.Id=Guid.NewGuid().ToString("N");first.Name="Earlier vest";first.Drawable=1;restored.Items.Add(first);
        var outfit=new OutfitPreset{Name="Uniform",Entries=[new(){Slot="task",AssetId=male.Id,Texture=0},new(){Slot="accs",Hidden=true}]};restored.Outfits.Add(outfit);
        var parts=FiveMCompanion.Outfits(restored).Single().parts;Check(parts[0].drawable==1&&parts[0].collection=="mp_m_duck_tests","Outfit mapping matches sorted local build index");Check(parts[1].hidden&&parts[1].collection=="","Outfit preserves hidden base component");
        var saved=Path.Combine(root,"test.duckproj");ProjectIO.Save(restored,saved);var reopened=ProjectIO.Load(saved);Check(reopened.Outfits[0].Entries[0].AssetId==male.Id,"Outfits survive save/reopen");
        Check(BatchEditing.Name("{gender}_{slot}_{n:000}_{name}",male,7)=="M_task_007_New name","Batch pattern expands all placeholders");Check(BatchEditing.SortFiles(["v10.png","v2.png","v1.png"]).SequenceEqual(new[]{"v1.png","v2.png","v10.png"}),"Batch import uses natural ordering");
        var changed=ProjectAssets.Clone(reopened);changed.Items[0].Name="Changed vest";var differences=ProjectComparison.Compare(reopened,changed);Check(differences.Any(x=>x.Change=="Name"),"Comparison finds metadata change");var replacement=Path.Combine(root,"replacement.ytd");File.WriteAllBytes(replacement,[77,88]);changed.Items[0].TexturePaths[0]=replacement;Check(ProjectComparison.Compare(reopened,changed).Any(x=>x.Change=="Texture a"&&x.After.Contains("2 bytes")),"Comparison finds content and size change");
        var mesh=GridMesh(25);var triangles=ModelTools.Indices(mesh).Length/3;var removed=ModelTools.Simplify(mesh,.5);var rows=ModelTools.Rows(mesh);var indices=ModelTools.Indices(mesh);Check(removed>0&&indices.Length/3<triangles,"LOD clustering reduces a dense grid");Check(indices.All(x=>x>=0&&x<rows.Length),"LOD compaction produces valid indices");Check(Enumerable.Range(0,indices.Length/3).All(i=>indices[i*3]!=indices[i*3+1]&&indices[i*3]!=indices[i*3+2]&&indices[i*3+1]!=indices[i*3+2]),"LOD output contains no collapsed triangles");
        using(var rsa=RSA.Create())
        {
            var release=new UpdateRelease{Version="9.0.0",InstallerUrl="https://example.invalid/setup.exe",Size=1,Sha256=new string('A',64),PublishedUtc=DateTime.UtcNow};var bytes=JsonSerializer.SerializeToUtf8Bytes(release,ProjectIO.Json);var signed=new SignedUpdate{Payload=Convert.ToBase64String(bytes),Signature=Convert.ToBase64String(rsa.SignData(bytes,HashAlgorithmName.SHA256,RSASignaturePadding.Pss))};
            Check(ApplicationUpdates.Verify(JsonSerializer.Serialize(signed),rsa.ExportSubjectPublicKeyInfoPem()).Version=="9.0.0","Release signature verifies");signed.Payload=Convert.ToBase64String(new byte[]{1,2,3});var rejected=false;try{ApplicationUpdates.Verify(JsonSerializer.Serialize(signed),rsa.ExportSubjectPublicKeyInfoPem());}catch(InvalidDataException){rejected=true;}Check(rejected,"Modified release payload is rejected");
        }
        foreach(var name in new[]{"FF1","CMD","1","Hat2_Copy1","test","Short Sleeve PSD_Copy1"})
            Check(DiffuseBinding.Select(["GravityFlow#0065_map","GravityFlow#0065_Spec",name],["GravityFlow#0065"],["GravityFlow#0065_map"])==name,"Renamed diffuse resolves: "+name);
        Check(DiffuseBinding.Select(["cloth_normal","cloth_spec"],["cloth"],[])==null,"Normal/specular dictionaries cannot masquerade as diffuse overrides");
        Check(DiffuseBinding.Select(["FF1","FF2"],["cloth"],[])==null,"Ambiguous color maps require an exact binding");
        Check(DiffuseBinding.Select(["FF1","cloth"],["CLOTH"],[])=="cloth","Exact material binding takes precedence over other color maps");
        ProjectFileChecks.Run(root,Check);
        PackReplacementChecks.Run(root,Check);
        PerformanceChecks.Run(root,Check);
        SecurityChecks.Run(root,Check);
        InstallerUpdateChecks.Run(root,Check);
        Environment.SetEnvironmentVariable("WEFT_STUDIO_DATA_HOME", Path.Combine(root, "EditorSettings"));
        EditorInteractionChecks.Run(root,Check);
        RealPreviewChecks.Run(root,Check);
        Console.WriteLine($"All {checks} source tests passed. Fixtures: {root}");
    }
    static XElement GridMesh(int count)
    {
        var vertices=new List<string>();var indices=new List<int>();
        for(var y=0;y<count;y++)for(var x=0;x<count;x++){var u=(x/(double)(count-1)).ToString("R",CultureInfo.InvariantCulture);var v=(y/(double)(count-1)).ToString("R",CultureInfo.InvariantCulture);vertices.Add($"{u} {v} 0   0 0 1   {u} {v}");}
        for(var y=0;y<count-1;y++)for(var x=0;x<count-1;x++){var i=y*count+x;indices.AddRange([i,i+1,i+count,i+1,i+count+1,i+count]);}
        return new XElement("Item",new XElement("VertexBuffer",new XElement("Layout",new XElement("Position"),new XElement("Normal"),new XElement("TexCoord0")),new XElement("Data",string.Join('\n',vertices))),new XElement("IndexBuffer",new XElement("Data",string.Join(' ',indices))));
    }
}
