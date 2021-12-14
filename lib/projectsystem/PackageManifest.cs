namespace vein.project;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;
using NuGet.Versioning;

public record PackageManifest
{
    [JsonProperty("name")]
    public string Name { get; set; }
    [JsonProperty("version"), JsonConverter(typeof(NuGetVersionConverter))]
    public NuGetVersion Version { get; set; }
    [JsonProperty("description")]
    public string Description { get; set; }
    [JsonProperty("authors")]
    public List<PackageAuthor> Authors { get; set; } = new();
    [JsonProperty("icon")]
    public string Icon { get; set; }
    [JsonProperty("license")]
    public string License { get; set; }
    [JsonProperty("preview")]
    public bool IsPreview { get; set; }
    [JsonProperty("urls")]
    public PackageUrls Urls { get; set; }
    [JsonProperty("keywords")]
    public List<string> Keywords { get; set; } = new();
    [JsonProperty("categories")]
    public List<string> Categories { get; set; } = new();
    [JsonProperty("dependencies")]
    public List<PackageReference> Dependencies { get; set; } = new();
    [JsonProperty("requireLicenseAcceptance")]
    public bool RequireLicenseAcceptance { get; set; }
    [JsonProperty("hasEmbeddedIcon")]
    public bool HasEmbeddedIcon { get; set; }
    [JsonProperty("hasEmbbededReadme")]
    public bool HasEmbbededReadme { get; set; }
}

public record PackageAuthor(string name, string github)
{
    public PackageAuthor() : this("", "") {}
}

public record PackageUrls
{
    [JsonProperty("bugs")]
    public string BugUrl { get; set; }
    [JsonProperty("homepage")]
    public string HomepageUrl { get; set; }
    [JsonProperty("repository")]
    public string Repository { get; set; }
    [JsonProperty("other"), JsonConverter(typeof(ToStringConverter<Dictionary<string, string>>))]
    public Dictionary<string, string> otherUrls { get; set; }
}

public class ToStringConverter<T> : JsonConverter<T>
{
    public override T ReadJson(JsonReader reader, Type objectType, [AllowNull] T existingValue, bool hasExistingValue, JsonSerializer serializer)
        => JsonConvert.DeserializeObject<T>(reader.ReadAsString());
    public override void WriteJson(JsonWriter writer, [AllowNull] T value, JsonSerializer serializer)
        => writer.WriteValue(JsonConvert.SerializeObject(value));
}

public record PackageMetadata
{
    [JsonProperty("publish_date")]
    public DateTimeOffset PublishDate { get; set; }
    [JsonProperty("id")]
    public string ID { get; set; }
}

public record PackageCertificate([JsonProperty("cert")] string PublicCertificate) { }

