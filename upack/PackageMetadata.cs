using System.Runtime.Serialization;

namespace Inedo.ProGet.UPack
{
    [DataContract]
    internal sealed class PackageMetadata
    {
        [DataMember(IsRequired = false, Name = "group")]
        public string Group { get; set; }
        [DataMember(IsRequired = true, Name = "name")]
        public string Name { get; set; }
        [DataMember(IsRequired = true, Name = "version")]
        public string Version { get; set; }
        [DataMember(IsRequired = false, Name = "title")]
        public string Title { get; set; }
        [DataMember(IsRequired = false, Name = "description")]
        public string Description { get; set; }
        [DataMember(IsRequired = false, Name = "icon")]
        public string IconUrl { get; set; }
        [DataMember(IsRequired = false, Name = "dependencies")]
        public string[] Dependencies { get; set; }

        public string BareVersion
		{
            get
            {
                var packageVersion = UniversalPackageVersion.TryParse(Version);
                return packageVersion != null
                    ? $"{packageVersion.Major}.{packageVersion.Minor}.{packageVersion.Patch}"
                    : Version;
            }
        }
    }
}
