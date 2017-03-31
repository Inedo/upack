using System.Runtime.Serialization;

namespace Inedo.ProGet.UPack
{
    [DataContract]
    internal sealed class RemotePackageMetadata
    {
        [DataMember(IsRequired = false, Name = "group")]
        public string Group { get; set; }
        [DataMember(IsRequired = true, Name = "name")]
        public string Name { get; set; }
        [DataMember(IsRequired = false, Name = "latestVersion")]
        public string LatestVersion { get; set; }
        [DataMember(IsRequired = true, Name = "versions")]
        public string[] Versions { get; set; }
    }
}
