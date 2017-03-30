using Newtonsoft.Json;

namespace Inedo.ProGet.UPack
{
    internal sealed class PackageMetadata
    {
        [JsonProperty(Required = Required.DisallowNull, PropertyName = "group")]
        public string Group { get; set; }
        [JsonProperty(Required = Required.Always, PropertyName = "name")]
        public string Name { get; set; }
        [JsonProperty(Required = Required.Always, PropertyName = "version")]
        public string Version { get; set; }
        [JsonProperty(Required = Required.DisallowNull, PropertyName = "title")]
        public string Title { get; set; }
        [JsonProperty(Required = Required.DisallowNull, PropertyName = "description")]
        public string Description { get; set; }
        [JsonProperty(Required = Required.DisallowNull, PropertyName = "icon")]
        public string IconUrl { get; set; }
        [JsonProperty(Required = Required.DisallowNull, PropertyName = "dependencies")]
        public string[] Dependencies { get; set; }
    }
}
