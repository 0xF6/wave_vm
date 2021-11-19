namespace vein.project
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using Newtonsoft.Json;

    public class VeinSDK
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("packs")]
        [JsonConverter(typeof(PacksConverter))]
        public SDKPack[] Packs { get; set; }

        internal static DirectoryInfo SDKRoot =>
            new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".vein"));

        public DirectoryInfo GetFullPath(SDKPack sdkPack) =>
            SDKRoot.SubDirectory("sdk")
                .SubDirectory($"{Name}-v{Version}")
                .ThrowIfNotExist($"'{Name}-v{Version}' is not installed.")
                .SubDirectory(sdkPack.Name);

        public DirectoryInfo GetFullPath() =>
            SDKRoot.SubDirectory("sdk")
                .SubDirectory($"{Name}-v{Version}")
                .ThrowIfNotExist($"'{Name}-v{Version}' is not installed.")
                .SubDirectory(GetDefaultPack().Name);

        public SDKPack GetDefaultPack()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return GetPackByAlias("win10-x64");
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return GetPackByAlias("osx-x64");
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return GetPackByAlias("linux-x64");
            throw new NotSupportedException("OS is not support");
        }

        public SDKPack GetPackByAlias(string alias)
        {
            var pack =
                Packs.FirstOrDefault(x => x.Alias.Equals(alias)) ??
                Packs.FirstOrDefault(x => x.Alias.Equals("*"));

            if (pack is not null)
                return pack;
            throw new DirectoryNotFoundException($"Pack '{alias}' not installed in '{Name}' sdk.");
        }

        public static VeinSDK Resolve(string name)
        {
            if (name.Equals("no-runtime"))
                return new VeinSDK
                {
                    Name = name,
                    Version = "1.0.0.0",
                    Packs = new[]
                    {
                        new SDKPack() { Alias = "*", Kind = PackKind.Sdk, Name = "All", Version = "1.0.0.0" }
                    }
                };

            if (!SDKRoot.Exists)
                throw new SDKNotInstalled($"Sdk is not installed.");

            return SDKRoot
                .SubDirectory("manifest")
                .EnumerateFiles("*.manifest.json")
                .Select(json => json.FullName)
                .Select(File.ReadAllText)
                .Select(JsonConvert.DeserializeObject<VeinSDK>)
                .FirstOrDefault(x => x.Name.Equals(name));
        }

        public FileInfo GetHostApplicationFile(SDKPack sdkPack) =>
            SDKRoot.SubDirectory("sdk")
                .SubDirectory($"{Name}-v{Version}")
                .SubDirectory(sdkPack.Name)
                .SubDirectory("host")
                .SingleFileByPattern("host.*");
    }
    public enum PackKind
    {
        Sdk,
        Template,
        Tools,
        Resources
    }

    public class SDKNotInstalled : Exception
    {
        public SDKNotInstalled(string msg) : base(msg)
        {

        }
    }
    public class SDKPack
    {
        public string Name { get; set; }
        [JsonProperty("kind")]
        public PackKind Kind { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("alias")]
        public string Alias { get; set; }
    }
}
