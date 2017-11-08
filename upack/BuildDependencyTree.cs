using Golang.Archive.Zip;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Inedo.ProGet.UPack
{
    [DisplayName("build-dependency-tree")]
    [Description("[Experimental] Download a package with all of its dependencies, recursively. Behavior subject to change.")]
    public sealed class BuildDependencyTree : Command
    {
        [DisplayName("package")]
        [Description("Package name and group, such as group/name.")]
        [PositionalArgument(0)]
        public string PackageName { get; set; }

        [DisplayName("version")]
        [Description("Package version. If not specified, the latest version is retrieved.")]
        [PositionalArgument(1, Optional = true)]
        public string Version { get; set; }

        [DisplayName("source")]
        [Description("URL of a upack API endpoint.")]
        [ExtraArgument(Optional = false)]
        public string SourceUrl { get; set; }

        [DisplayName("target")]
        [Description("Directory where the contents of the package will be extracted. If not specified, the packages will be listed but not installed.")]
        [ExtraArgument]
        public string TargetDirectory { get; set; }

        [DisplayName("user")]
        [Description("User name and password to use for servers that require authentication. Example: username:password")]
        [ExtraArgument]
        public NetworkCredential Authentication { get; set; }

        [DisplayName("overwrite")]
        [Description("When specified, Overwrite files in the target directory.")]
        [ExtraArgument]
        [DefaultValue(false)]
        public bool Overwrite { get; set; } = false;

        [DisplayName("prerelease")]
        [Description("When version is not specified, will install the latest prerelase version instead of the latest stable version.")]
        [ExtraArgument]
        [DefaultValue(false)]
        public bool Prerelease { get; set; } = false;

        [DisplayName("comment")]
        [Description("The reason for installing the package, for the local registry.")]
        [ExtraArgument]
        public string Comment { get; set; }

        [DisplayName("userregistry")]
        [Description("Register the package in the user registry instead of the machine registry.")]
        [ExtraArgument]
        [DefaultValue(false)]
        public bool UserRegistry { get; set; } = false;

        [DisplayName("unregistered")]
        [Description("Do not register the package in a local registry.")]
        [ExtraArgument]
        [DefaultValue(false)]
        public bool Unregistered { get; set; } = false;

        [DisplayName("cache")]
        [Description("Cache the contents of the package in the local registry.")]
        [ExtraArgument]
        [DefaultValue(false)]
        public bool CachePackages { get; set; } = false;

        [DisplayName("perserve-timestamps")]
        [Description("Set extracted file timestamps to the timestamp of the file in the archive instead of the current time.")]
        [ExtraArgument]
        [DefaultValue(false)]
        public bool PerserveTimestamps { get; set; } = false;

        private Registry registry, cache;

        public override async Task<int> RunAsync()
        {
            if (this.Unregistered)
            {
                this.registry = Registry.Unregistered;
            }
            else if (this.UserRegistry)
            {
                this.registry = Registry.User;
            }
            else
            {
                this.registry = Registry.Machine;
            }

            string tempCacheRegistry = null;
            if (this.CachePackages)
            {
                this.cache = this.registry;
            }
            else
            {
                tempCacheRegistry = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
                this.cache = new Registry(tempCacheRegistry);
            }

            try
            {
                var tree = await this.BuildTreeAsync(this.PackageName, this.Version, this.Prerelease);
                var resolved = this.Resolve(tree);
                if (this.TargetDirectory != null)
                {
                    if (!this.Overwrite && this.CheckOverwrite(resolved))
                    {
                        return 1;
                    }

                    foreach (var pkg in resolved.Packages)
                    {
                        Console.WriteLine($"Extracting {pkg.PackageName}:{pkg.Version}...");

                        using (var stream = await this.OpenPackageAsync(pkg.PackageName, pkg.Version.ToString(), false))
                        using (var zip = new ZipReader(stream, true))
                        {
                            await UnpackZipAsync(this.TargetDirectory, true, zip, this.PerserveTimestamps);
                        }
                    }
                }
                else
                {
                    foreach (var pkg in resolved.Packages)
                    {
                        Console.WriteLine($"{pkg.PackageName}:{pkg.Version}");
                    }
                }

                return 0;
            }
            finally
            {
                if (tempCacheRegistry != null)
                {
                    try
                    {
                        Directory.Delete(tempCacheRegistry, true);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private sealed class ResolvedDependencyInfo
        {
            public List<ResolvedDependencyPackage> Packages { get; } = new List<ResolvedDependencyPackage>();
            public HashSet<string> Dirs { get; } = new HashSet<string>();
            public Dictionary<string, byte[]> Files { get; } = new Dictionary<string, byte[]>();
        }

        private struct ResolvedDependencyPackage
        {
            public string PackageName { get; }
            public UniversalPackageVersion Version { get; }

            public ResolvedDependencyPackage(string name, UniversalPackageVersion version)
            {
                this.PackageName = name;
                this.Version = version;
            }
        }

        private sealed class DependencyTree
        {
            public string PackageName { get; }
            public UniversalPackageVersion Version { get; }
            public List<DependencyTree> Dependencies { get; } = new List<DependencyTree>();
            public HashSet<string> Dirs { get; } = new HashSet<string>();
            public Dictionary<string, byte[]> Files { get; } = new Dictionary<string, byte[]>();

            public DependencyTree(PackageMetadata metadata)
            {
                this.PackageName = metadata.GroupAndName;
                this.Version = UniversalPackageVersion.Parse(metadata.Version);
            }
        }

        private bool CheckOverwrite(ResolvedDependencyInfo resolved)
        {
            bool found = false;

            foreach (var name in resolved.Dirs)
            {
                if (File.Exists(Path.Combine(this.TargetDirectory, name)))
                {
                    Console.Error.WriteLine($"refusing to overwrite file with directory: {name}");
                    found = true;
                }
            }

            foreach (var file in resolved.Files)
            {
                if (Directory.Exists(Path.Combine(this.TargetDirectory, file.Key)))
                {
                    Console.Error.WriteLine($"refusing to overwrite directory with file: {file.Key}");
                    found = true;
                }
                else if (File.Exists(Path.Combine(this.TargetDirectory, file.Key)))
                {
                    Console.Error.WriteLine($"refusing to overwrite file: {file.Key}");
                    found = true;
                }
            }

            return found;
        }

        private ResolvedDependencyInfo Resolve(DependencyTree tree)
        {
            var maxDepth = this.ComputeMaxDepth(tree);

            var info = new ResolvedDependencyInfo();
            for (var depth = maxDepth; depth >= 0; depth--)
            {
                this.GatherPackages(tree, depth, info.Packages);
            }

            this.MergeContents(info.Dirs, info.Files, tree);
            return info;
        }

        private int ComputeMaxDepth(DependencyTree tree)
        {
            int depth = 0;

            foreach (var d in tree.Dependencies)
            {
                int childDepth = this.ComputeMaxDepth(d);
                if (childDepth >= depth)
                {
                    depth = childDepth + 1;
                }
            }

            return depth;
        }

        private void GatherPackages(DependencyTree tree, int depth, List<ResolvedDependencyPackage> packages)
        {
            if (depth > 0)
            {
                foreach (var d in tree.Dependencies)
                {
                    this.GatherPackages(d, depth - 1, packages);
                }
                return;
            }

            packages.RemoveAll(pkg => string.Equals(pkg.PackageName, tree.PackageName) && pkg.Version == tree.Version);
            packages.Add(new ResolvedDependencyPackage(tree.PackageName, tree.Version));
        }

        private void MergeContents(HashSet<string> mergedDirs, Dictionary<string, byte[]> mergedFiles, DependencyTree tree)
        {
            var depDirs = new List<HashSet<string>>();
            var depFiles = new List<Dictionary<string, byte[]>>();

            foreach (var d in tree.Dependencies)
            {
                var dirs = new HashSet<string>();
                var files = new Dictionary<string, byte[]>();
                try
                {
                    this.MergeContents(dirs, files, d);
                }
                catch (Exception ex)
                {
                    throw new AggregateException($"in dependency of {tree.PackageName}:{tree.Version}: {ex.Message}", ex);
                }
                depDirs.Add(dirs);
                depFiles.Add(files);
            }

            var fileFrom = new Dictionary<string, int>();

            for (int i = 0; i < depFiles.Count; i++)
            {
                var df = depFiles[i];
                var d = tree.Dependencies[i];

                foreach (var file in df)
                {
                    if (tree.Files.ContainsKey(file.Key))
                    {
                        continue;
                    }

                    if (mergedFiles.ContainsKey(file.Key))
                    {
                        var existingHash = mergedFiles[file.Key];
                        if (existingHash.SequenceEqual(file.Value))
                        {
                            continue;
                        }

                        var existing = tree.Dependencies[fileFrom[file.Key]];
                        throw new InvalidOperationException($"Cannot have both {existing.PackageName}:{existing.Version} and {d.PackageName}:{d.Version} as dependencies of {tree.PackageName}:{tree.Version} as both contain the file {file.Key} with different hashes ({BitConverter.ToString(existingHash)} vs {BitConverter.ToString(file.Value)})");
                    }

                    mergedFiles[file.Key] = file.Value;
                    fileFrom[file.Key] = i;
                }
            }

            foreach (var file in tree.Files)
            {
                mergedFiles[file.Key] = file.Value;
            }

            var dirFrom = new Dictionary<string, int>();

            foreach (var name in tree.Dirs)
            {
                mergedDirs.Add(name);
            }

            for (int i = 0; i < depDirs.Count; i++)
            {
                foreach (var name in depDirs[i])
                {
                    if (mergedDirs.Add(name))
                    {
                        dirFrom[name] = i;
                    }
                }
            }

            foreach (var name in mergedDirs)
            {
                if (mergedFiles.ContainsKey(name))
                {
                    var dirTree = dirFrom.ContainsKey(name) ? tree.Dependencies[dirFrom[name]] : tree;
                    var fileTree = fileFrom.ContainsKey(name) ? tree.Dependencies[fileFrom[name]] : tree;
                    throw new InvalidOperationException($"Cannot have both a directory from {dirTree.PackageName}:{dirTree.Version} and a file from {fileTree.PackageName}:{fileTree.Version} named {name} in {tree.PackageName}:{tree.Version}");
                }
            }
        }

        private async Task<DependencyTree> BuildTreeAsync(string name, string version, bool prerelease)
        {
            PackageMetadata manifest;
            DependencyTree tree;

            using (var stream = await this.OpenPackageAsync(name, version, prerelease))
            using (var zip = new ZipReader(stream, true))
            {
                var metadataEntry = zip.File.Where(f => string.Equals(f.Header.Name, "upack.json")).First();
                using (var metadataStream = metadataEntry.Open())
                {
                    manifest = await ReadManifestAsync(metadataStream);
                }

                tree = new DependencyTree(manifest);

                var entries = zip.File.Where(e => e.Header.Name.StartsWith("package/", StringComparison.OrdinalIgnoreCase));

                foreach (var entry in entries)
                {
                    var entryName = entry.Header.Name.Substring("package/".Length);
                    if (entryName == "")
                    {
                        continue;
                    }

                    if (entry.Header.Mode.HasFlag(FileAttributes.Directory))
                    {
                        entryName = entryName.TrimEnd('/');
                        tree.Dirs.Add(entryName);
                    }
                    else
                    {
                        using (var hash = SHA256.Create())
                        using (var entryStream = entry.Open())
                        {
                            tree.Files[entryName] = hash.ComputeHash(entryStream);
                        }
                    }

                    while (true)
                    {
                        var i = entryName.LastIndexOf('/');
                        if (i == -1)
                        {
                            break;
                        }
                        entryName = entryName.Substring(0, i);
                        tree.Dirs.Add(entryName);
                    }
                }
            }

            prerelease = !string.IsNullOrEmpty(tree.Version.Prerelease);

            foreach (var d in manifest.Dependencies)
            {
                var parts = d.Split(new[]{ ':' }, 3);
                if (parts.Length == 1)
                {
                    name = parts[0];
                    version = null;
                }
                else if (parts.Length == 2)
                {
                    if (parts[1] == "*")
                    {
                        name = parts[0];
                        version = null;
                    }
                    else
                    {
                        var parsedVersion = UniversalPackageVersion.TryParse(parts[1]);
                        if (parsedVersion != null)
                        {
                            name = parts[0];
                            version = parsedVersion.ToString();
                        }
                        else
                        {
                            name = parts[0] + "/" + parts[1];
                            version = null;
                        }
                    }
                }
                else if (parts.Length == 3)
                {
                    name = parts[0] + "/" + parts[1];
                    if (parts[2] == "*")
                    {
                        version = null;
                    }
                    else
                    {
                        version = parts[2];
                    }
                }

                tree.Dependencies.Add(await this.BuildTreeAsync(name, version, prerelease));
            }

            return tree;
        }

        private async Task<Stream> OpenPackageAsync(string packageName, string packageVersion, bool prerelease)
        {
            string group = null, name = null, version = null;

            var parts = this.PackageName.Split(new[] { ':', '/' });
            group = parts.Length > 1 ? string.Join("/", new ArraySegment<string>(parts, 0, parts.Length - 1)) : null;
            name = parts[parts.Length - 1];

            version = await GetVersionAsync(this.SourceUrl, group, name, version, this.Authentication, prerelease);

            if (this.TargetDirectory != null)
            {
                await this.registry.RegisterPackageAsync(group, name, UniversalPackageVersion.Parse(version),
                    this.TargetDirectory, this.SourceUrl, this.Authentication,
                    this.Comment, null, Environment.UserName);
            }

            return await this.cache.GetOrDownloadAsync(group, name, UniversalPackageVersion.Parse(version), this.SourceUrl, this.Authentication, true);
        }
    }
}
