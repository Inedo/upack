using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;

namespace Inedo.ProGet.UPack
{
    public abstract class Command
    {
        [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
        public sealed class PositionalArgumentAttribute : Attribute
        {
            public int Index { get; }
            public bool Optional { get; set; } = false;

            public PositionalArgumentAttribute(int index)
            {
                this.Index = index;
            }
        }

        [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
        public sealed class ExtraArgumentAttribute : Attribute
        {
            public bool Optional { get; set; } = true;
        }

        public abstract class Argument
        {
            protected readonly PropertyInfo p;

            internal Argument(PropertyInfo p)
            {
                this.p = p;
            }

            public abstract bool Optional { get; }
            public string DisplayName => p.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName ?? p.Name;
            public string Description => p.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty;
            public object DefaultValue => p.GetCustomAttribute<DefaultValueAttribute>()?.Value;

            public abstract string GetUsage();

            public virtual string GetHelp()
            {
                return $"{this.DisplayName} - {this.Description}";
            }

            public bool TrySetValue(Command cmd, string value)
            {
                if (p.PropertyType == typeof(bool))
                {
                    if (string.IsNullOrEmpty(value))
                    {
                        p.SetValue(cmd, true);
                        return true;
                    }
                    bool result;
                    if (bool.TryParse(value, out result))
                    {
                        p.SetValue(cmd, result);
                        return true;
                    }
                    Console.WriteLine($@"--{this.DisplayName} must be ""true"" or ""false"".");
                    return false;
                }

                if (p.PropertyType == typeof(string))
                {
                    p.SetValue(cmd, value);
                    return true;
                }

                if (p.PropertyType == typeof(NetworkCredential))
                {
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        p.SetValue(cmd, null);
                        return true;
                    }

                    var parts = value.Split(new[] { ':' }, 2);
                    if (parts.Length != 2)
                    {
                        Console.WriteLine($@"--{this.DisplayName} must be in the format ""username:password"".");
                        return false;
                    }

                    p.SetValue(cmd, new NetworkCredential(parts[0], parts[1]));
                    return true;
                }

                throw new ArgumentException(p.PropertyType.FullName);
            }
        }

        public sealed class PositionalArgument : Argument
        {
            internal PositionalArgument(PropertyInfo p) : base(p)
            {
            }

            public int Index => p.GetCustomAttribute<PositionalArgumentAttribute>().Index;
            public override bool Optional => p.GetCustomAttribute<PositionalArgumentAttribute>().Optional;

            public override string GetUsage()
            {
                var s = $"«{this.DisplayName}»";

                if (this.Optional)
                {
                    s = $"[{s}]";
                }

                return s;
            }
        }

        public sealed class ExtraArgument : Argument
        {
            internal ExtraArgument(PropertyInfo p) : base(p)
            {
            }

            public override bool Optional => p.GetCustomAttribute<ExtraArgumentAttribute>().Optional;

            public override string GetUsage()
            {
                var s = $"--{this.DisplayName}=«{this.DisplayName}»";

                if (this.Optional)
                {
                    s = $"[{s}]";
                }

                if (p.PropertyType == typeof(bool) && this.DefaultValue == (object)false && this.Optional)
                {
                    s = $"[--{this.DisplayName}]";
                }

                return s;
            }
        }

        public string DisplayName => this.GetType().GetCustomAttribute<DisplayNameAttribute>()?.DisplayName ?? this.GetType().Name;
        public string Description => this.GetType().GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty;
        public IEnumerable<PositionalArgument> PositionalArguments => this.GetType().GetRuntimeProperties()
            .Where(p => p.GetCustomAttribute<PositionalArgumentAttribute>() != null)
            .Select(p => new PositionalArgument(p))
            .OrderBy(a => a.Index);

        public abstract Task<int> RunAsync();

        public IEnumerable<ExtraArgument> ExtraArguments => this.GetType().GetRuntimeProperties()
            .Where(p => p.GetCustomAttribute<ExtraArgumentAttribute>() != null)
            .Select(p => new ExtraArgument(p));

        public string GetUsage()
        {
            var s = new StringBuilder("upack ");

            s.Append(this.DisplayName);

            foreach (var arg in this.PositionalArguments)
            {
                s.Append(' ').Append(arg.GetUsage());
            }

            foreach (var arg in this.ExtraArguments)
            {
                s.Append(' ').Append(arg.GetUsage());
            }

            return s.ToString();
        }

        public string GetHelp()
        {
            var s = new StringBuilder("Usage: ");

            s.AppendLine(this.GetUsage()).AppendLine().AppendLine(this.Description);

            foreach (var arg in this.PositionalArguments)
            {
                s.AppendLine().Append(arg.GetHelp());
            }

            foreach (var arg in this.ExtraArguments)
            {
                s.AppendLine().Append(arg.GetHelp());
            }

            return s.ToString();
        }

        internal static async Task<PackageMetadata> ReadManifestAsync(Stream metadataStream)
        {
            var serializer = new DataContractJsonSerializer(typeof(PackageMetadata));
            return await Task.Run(() => (PackageMetadata)serializer.ReadObject(metadataStream));
        }

        internal static void PrintManifest(PackageMetadata info)
        {
            Console.WriteLine($"Package: {info.GroupAndName}");
            Console.WriteLine($"Version: {info.Version}");
        }

        internal static async Task UnpackZipAsync(string targetDirectory, bool overwrite, ZipArchive zipFile, bool perserveTimestamps)
        {
            Directory.CreateDirectory(targetDirectory);

            var entries = zipFile.Entries.Where(e => e.FullName.StartsWith("package/", StringComparison.OrdinalIgnoreCase));

            int files = 0;
            int directories = 0;

            foreach (var entry in entries)
            {
                var targetPath = Path.Combine(targetDirectory, entry.FullName.Substring("package/".Length).Replace('/', Path.DirectorySeparatorChar));

                if (entry.FullName.EndsWith("/"))
                {
                    Directory.CreateDirectory(targetPath);
                    directories++;
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
                    using (var entryStream = entry.Open())
                    using (var targetStream = new FileStream(targetPath, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        await entryStream.CopyToAsync(targetStream);
                    }

                    // Assume files with timestamps set to 0 (DOS time) or close to 0 are not timestamped.
                    if (perserveTimestamps && entry.LastWriteTime.Year > 1980)
                    {
                        File.SetLastWriteTimeUtc(targetPath, entry.LastWriteTime.DateTime);
                    }

                    files++;
                }
            }

            Console.WriteLine($"Extracted {files} files and {directories} directories.");
        }

        internal static async Task CreateEntryFromFileAsync(ZipArchive zipFile, string fileName, string entryPath)
        {
            var entry = zipFile.CreateEntry(entryPath);

            using (var input = new FileStream(fileName, FileMode.Open, FileAccess.Read))
            using (var output = entry.Open())
            {
                await input.CopyToAsync(output);
            }
        }

        internal static async Task CreateEntryFromStreamAsync(ZipArchive zipFile, Stream file, string entryPath)
        {
            var entry = zipFile.CreateEntry(entryPath);
            
            using (var output = entry.Open())
            {
                file.Position = 0;
                await file.CopyToAsync(output);
            }
        }

        internal static async Task AddDirectoryAsync(ZipArchive zipFile, string sourceDirectory, string entryRootPath)
        {
            bool hasContent = false;

            foreach (var fileName in Directory.EnumerateFiles(sourceDirectory))
            {
                await CreateEntryFromFileAsync(zipFile, fileName, entryRootPath + Path.GetFileName(fileName));
                hasContent = true;
            }

            foreach (var directoryName in Directory.EnumerateDirectories(sourceDirectory))
            {
                hasContent = true;
                await AddDirectoryAsync(zipFile, directoryName, entryRootPath + Path.GetFileName(directoryName) + "/");
            }

            if (!hasContent)
                zipFile.CreateEntry(entryRootPath);
        }

        internal static async Task<string> GetVersionAsync(string source, string group, string name, string version, NetworkCredential credentials, bool prerelease)
        {
            if (!string.IsNullOrEmpty(version) && !string.Equals(version, "latest", StringComparison.OrdinalIgnoreCase) && !prerelease)
            {
                return version;
            }

            using (var client = CreateClient(credentials))
            using (var response = await client.GetAsync($"{source.TrimEnd('/')}/packages?group={Uri.EscapeDataString(group)}&name={Uri.EscapeDataString(name)}"))
            {
                response.EnsureSuccessStatusCode();

                var serializer = new DataContractJsonSerializer(typeof(RemotePackageMetadata));
                var metadata = (RemotePackageMetadata)serializer.ReadObject(await response.Content.ReadAsStreamAsync());
                var versions = metadata.Versions.Select(UniversalPackageVersion.Parse);

                if (!prerelease)
                {
                    versions = versions.Where(v => string.IsNullOrEmpty(v.Prerelease));
                }

                return versions.Max().ToString();
            }
        }

        internal static HttpClient CreateClient(NetworkCredential credentials)
        {
            return new HttpClient(new HttpClientHandler
            {
                UseDefaultCredentials = credentials == null,
                Credentials = credentials,
                PreAuthenticate = true,
            });
        }
    }
}
