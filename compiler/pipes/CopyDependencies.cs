namespace vein.pipes;

using System.IO;
using System.Linq;
using System.Text;
using cmd;
using fs;

[ExcludeFromCodeCoverage]
public class CopyDependencies : CompilerPipeline
{
    public override void Action()
    {
        if (!Target.HasChanged)
            return;

        foreach (var dependency in Target.Dependencies.SelectMany(x => x.Artifacts))
        {
            if (dependency.Kind is ArtifactKind.BINARY)
                File.Copy(dependency.Path.FullName,
                    Path.Combine(OutputDirectory.FullName, Path.GetFileName(dependency.Path.FullName)));
        }
    }
    public override bool CanApply(CompileSettings flags) => true;
    public override int Order => 0;
}


[ExcludeFromCodeCoverage]
public class GenerateMetalinks : CompilerPipeline
{
    public override void Action()
    {
        if (!Target.HasChanged)
            return;

        var storage = new ShardStorage();
        var metalinks = new StringBuilder();

        foreach (var package in Target.Project.Dependencies.Packages)
            metalinks.AppendLine($"{storage.GetPackageSpace(package.Name, package.Version).SubDirectory("lib").FullName}");

        OutputDirectory.File("metalinks.txt").WriteAllText(metalinks.ToString());
    }
    public override bool CanApply(CompileSettings flags) => true;
    public override int Order => 15;
}
