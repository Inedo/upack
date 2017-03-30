using Newtonsoft.Json;

namespace Inedo.ProGet.UPack
{
    internal sealed class RemotePackageMetadata
    {
        [JsonProperty(Required = Required.DisallowNull, PropertyName = "group")]
        public string Group { get; set; }
        [JsonProperty(Required = Required.Always, PropertyName = "name")]
        public string Name { get; set; }
        [JsonProperty(Required = Required.DisallowNull, PropertyName = "latestVersion")]
        public string LatestVersion { get; set; }
        [JsonProperty(Required = Required.Always, PropertyName = "versions")]
        public string[] Versions { get; set; }
    }
}
