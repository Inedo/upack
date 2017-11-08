package main

import (
	"archive/zip"
	"crypto/sha256"
	"fmt"
	"io"
	"io/ioutil"
	"os"
	"os/user"
	"path/filepath"
	"strings"

	"github.com/pkg/errors"
)

type BuildDependencyTree struct {
	PackageName        string
	Version            string
	SourceURL          string
	TargetDirectory    *string
	Authentication     *[2]string
	Overwrite          bool
	Prerelease         bool
	Comment            *string
	UserRegistry       bool
	Unregistered       bool
	CachePackages      bool
	PerserveTimestamps bool

	registry Registry
	cache    Registry
}

type ResolvedDependencyInfo struct {
	Packages []*ResolvedDependencyPackage
	Dirs     map[string]struct{}
	Files    map[string][sha256.Size]byte
}

type ResolvedDependencyPackage struct {
	PackageName string
	Version     *UniversalPackageVersion
}

type DependencyTree struct {
	PackageName  string
	Version      *UniversalPackageVersion
	Dependencies []*DependencyTree
	Dirs         map[string]struct{}
	Files        map[string][sha256.Size]byte
}

func (*BuildDependencyTree) Name() string { return "build-dependency-tree" }
func (*BuildDependencyTree) Description() string {
	return "[Experimental] Download a package with all of its dependencies, recursively. Behavior subject to change."
}

func (b *BuildDependencyTree) Help() string  { return defaultCommandHelp(b) }
func (b *BuildDependencyTree) Usage() string { return defaultCommandUsage(b) }

func (*BuildDependencyTree) PositionalArguments() []PositionalArgument {
	return []PositionalArgument{
		{
			Name:        "package",
			Description: "Package name and group, such as group/name.",
			Index:       0,
			TrySetValue: trySetStringValue("package", func(cmd Command) *string {
				return &cmd.(*BuildDependencyTree).PackageName
			}),
		},
		{
			Name:        "version",
			Description: "Package version. If not specified, the latest version is retrieved.",
			Index:       1,
			Optional:    true,
			TrySetValue: trySetStringValue("version", func(cmd Command) *string {
				return &cmd.(*BuildDependencyTree).Version
			}),
		},
	}
}

func (*BuildDependencyTree) ExtraArguments() []ExtraArgument {
	return []ExtraArgument{
		{
			Name:        "source",
			Description: "URL of a upack API endpoint.",
			Required:    true,
			TrySetValue: trySetStringValue("source", func(cmd Command) *string {
				return &cmd.(*BuildDependencyTree).SourceURL
			}),
		},
		{
			Name:        "target",
			Description: "Directory where the contents of the package will be extracted. If not specified, the packages will be listed but not installed.",
			TrySetValue: trySetStringValue("target", func(cmd Command) *string {
				target := new(string)
				cmd.(*BuildDependencyTree).TargetDirectory = target
				return target
			}),
		},
		{
			Name:        "user",
			Description: "User name and password to use for servers that require authentication. Example: username:password",
			TrySetValue: trySetBasicAuthValue("user", func(cmd Command) **[2]string {
				return &cmd.(*BuildDependencyTree).Authentication
			}),
		},
		{
			Name:        "overwrite",
			Description: "When specified, Overwrite files in the target directory.",
			Flag:        true,
			TrySetValue: trySetBoolValue("overwrite", func(cmd Command) *bool {
				return &cmd.(*BuildDependencyTree).Overwrite
			}),
		},
		{
			Name:        "prerelease",
			Description: "When version is not specified, will install the latest prerelase version instead of the latest stable version.",
			Flag:        true,
			TrySetValue: trySetBoolValue("prerelease", func(cmd Command) *bool {
				return &cmd.(*BuildDependencyTree).Prerelease
			}),
		},
		{
			Name:        "comment",
			Description: "The reason for installing the package, for the local registry.",
			TrySetValue: trySetStringValue("comment", func(cmd Command) *string {
				s := new(string)
				cmd.(*BuildDependencyTree).Comment = s
				return s
			}),
		},
		{
			Name:        "userregistry",
			Description: "Register the package in the user registry instead of the machine registry.",
			Flag:        true,
			TrySetValue: trySetBoolValue("userregistry", func(cmd Command) *bool {
				return &cmd.(*BuildDependencyTree).UserRegistry
			}),
		},
		{
			Name:        "unregistered",
			Description: "Do not register the package in a local registry.",
			Flag:        true,
			TrySetValue: trySetBoolValue("unregistered", func(cmd Command) *bool {
				return &cmd.(*BuildDependencyTree).Unregistered
			}),
		},
		{
			Name:        "cache",
			Description: "Cache the contents of the package in the local registry.",
			Flag:        true,
			TrySetValue: trySetBoolValue("cache", func(cmd Command) *bool {
				return &cmd.(*BuildDependencyTree).CachePackages
			}),
		},
		{
			Name:        "perserve-timestamps",
			Description: "Set extracted file timestamps to the timestamp of the file in the archive instead of the current time.",
			Flag:        true,
			TrySetValue: trySetBoolValue("perserve-timestamps", func(cmd Command) *bool {
				return &cmd.(*Install).PerserveTimestamps
			}),
		},
	}
}

func (b *BuildDependencyTree) Run() int {
	if b.Unregistered {
		b.registry = Unregistered
	} else if b.UserRegistry {
		b.registry = User
	} else {
		b.registry = Machine
	}

	if b.CachePackages {
		b.cache = b.registry
	} else {
		dir, err := ioutil.TempDir("", "upack")
		if err != nil {
			fmt.Fprintln(os.Stderr, err)
			return 1
		}
		defer os.RemoveAll(dir)
		b.cache = Registry(dir)
	}

	tree, err := b.BuildTree(b.PackageName, b.Version, b.Prerelease)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
	}

	resolved, err := b.Resolve(tree)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
	}

	if b.TargetDirectory != nil {
		if !b.Overwrite && b.CheckOverwrite(resolved) {
			return 1
		}

		for _, pkg := range resolved.Packages {
			fmt.Printf("Extracting %s:%v...\n", pkg.PackageName, pkg.Version)

			err = b.ExtractPackage(pkg)
			if err != nil {
				fmt.Fprintln(os.Stderr, err)
				return 1
			}
		}
	} else {
		for _, pkg := range resolved.Packages {
			fmt.Printf("%s:%v\n", pkg.PackageName, pkg.Version)
		}
	}

	return 0
}

func (b *BuildDependencyTree) CheckOverwrite(resolved *ResolvedDependencyInfo) bool {
	found := false

	for name := range resolved.Dirs {
		fi, err := os.Stat(filepath.Join(*b.TargetDirectory, name))
		if os.IsNotExist(err) {
			continue
		}
		if err != nil {
			fmt.Fprintf(os.Stderr, "when checking directory %q: %v\n", name, err)
			found = true
		} else if !fi.IsDir() {
			fmt.Fprintf(os.Stderr, "refusing to overwrite file with directory: %q\n", name)
			found = true
		}
	}

	for name := range resolved.Files {
		fi, err := os.Stat(filepath.Join(*b.TargetDirectory, name))
		if os.IsNotExist(err) {
			continue
		}
		if err != nil {
			fmt.Fprintf(os.Stderr, "when checking file %q: %v\n", name, err)
			found = true
		} else if fi.IsDir() {
			fmt.Fprintf(os.Stderr, "refusing to overwrite directory with file: %q\n", name)
			found = true
		} else {
			fmt.Fprintf(os.Stderr, "refusing to overwrite file: %q\n", name)
			found = true
		}
	}

	return found
}

func (b *BuildDependencyTree) ExtractPackage(pkg *ResolvedDependencyPackage) (err error) {
	r, size, done, err := b.OpenPackage(pkg.PackageName, pkg.Version.String(), false)
	if err != nil {
		return err
	}
	defer func() {
		if e := done(); err == nil {
			err = e
		}
	}()

	zipFile, err := zip.NewReader(r, size)
	if err != nil {
		return err
	}

	return UnpackZip(*b.TargetDirectory, true, zipFile, b.PerserveTimestamps)
}

func (b *BuildDependencyTree) Resolve(tree *DependencyTree) (*ResolvedDependencyInfo, error) {
	maxDepth := b.ComputeMaxDepth(tree)

	info := &ResolvedDependencyInfo{}
	for depth := maxDepth; depth >= 0; depth-- {
		info.Packages = b.GatherPackages(tree, depth, info.Packages)
	}

	var err error
	info.Dirs, info.Files, err = b.MergeContents(tree)
	return info, err
}

func (b *BuildDependencyTree) ComputeMaxDepth(tree *DependencyTree) int {
	depth := 0

	for _, d := range tree.Dependencies {
		if childDepth := b.ComputeMaxDepth(d); childDepth >= depth {
			depth = childDepth + 1
		}
	}

	return depth
}

func (b *BuildDependencyTree) GatherPackages(tree *DependencyTree, depth int, packages []*ResolvedDependencyPackage) []*ResolvedDependencyPackage {
	if depth > 0 {
		for _, d := range tree.Dependencies {
			packages = b.GatherPackages(d, depth-1, packages)
		}
		return packages
	}

	for i, p := range packages {
		if p.PackageName == tree.PackageName && p.Version.Equals(tree.Version) {
			packages = append(packages[:i], packages[i+1:]...)
			break
		}
	}

	return append(packages, &ResolvedDependencyPackage{
		PackageName: tree.PackageName,
		Version:     tree.Version,
	})
}

func (b *BuildDependencyTree) MergeContents(tree *DependencyTree) (map[string]struct{}, map[string][sha256.Size]byte, error) {
	depDirs := make([]map[string]struct{}, len(tree.Dependencies))
	depFiles := make([]map[string][sha256.Size]byte, len(tree.Dependencies))

	for i, d := range tree.Dependencies {
		var err error
		depDirs[i], depFiles[i], err = b.MergeContents(d)
		if err != nil {
			return nil, nil, errors.Wrapf(err, "in dependency of %s:%v", tree.PackageName, tree.Version)
		}
	}

	fileFrom := make(map[string]int)
	mergedFiles := make(map[string][sha256.Size]byte)

	for i, df := range depFiles {
		d := tree.Dependencies[i]
		for name, hash := range df {
			if _, ok := tree.Files[name]; ok {
				continue
			}

			if existingHash, ok := mergedFiles[name]; ok {
				if existingHash == hash {
					continue
				}

				existing := tree.Dependencies[fileFrom[name]]
				return nil, nil, errors.Errorf("Cannot have both %s:%v and %s:%v as dependencies of %s:%v as both contain the file %q with different hashes (%x vs %x)", existing.PackageName, existing.Version, d.PackageName, d.Version, tree.PackageName, tree.Version, name, existingHash, hash)
			}

			mergedFiles[name] = hash
			fileFrom[name] = i
		}
	}

	for name, hash := range tree.Files {
		mergedFiles[name] = hash
	}

	mergedDirs := make(map[string]struct{})
	dirFrom := make(map[string]int)

	for name := range tree.Dirs {
		mergedDirs[name] = struct{}{}
	}

	for i, dd := range depDirs {
		for name := range dd {
			if _, ok := mergedDirs[name]; !ok {
				mergedDirs[name] = struct{}{}
				dirFrom[name] = i
			}
		}
	}

	for name := range mergedDirs {
		if _, ok := mergedFiles[name]; ok {
			dirTree := tree
			if i, ok := dirFrom[name]; ok {
				dirTree = tree.Dependencies[i]
			}
			fileTree := tree
			if i, ok := fileFrom[name]; ok {
				fileTree = tree.Dependencies[i]
			}
			return nil, nil, errors.Errorf("Cannot have both a directory from %s:%v and a file from %s:%v named %q in %s:%v", dirTree.PackageName, dirTree.Version, fileTree.PackageName, fileTree.Version, name, tree.PackageName, tree.Version)
		}
	}

	return mergedDirs, mergedFiles, nil
}

func (b *BuildDependencyTree) BuildTree(name, version string, prerelease bool) (*DependencyTree, error) {
	manifest, dirs, files, err := b.ReadUpack(name, version, prerelease)
	if err != nil {
		return nil, err
	}

	resolvedName := manifest.groupAndName()
	resolvedVersion, err := ParseUniversalPackageVersion(manifest.Version)
	if err != nil {
		return nil, err
	}

	tree := &DependencyTree{
		PackageName:  resolvedName,
		Version:      resolvedVersion,
		Dependencies: make([]*DependencyTree, len(manifest.Dependencies)),
		Files:        files,
		Dirs:         dirs,
	}

	prerelease = resolvedVersion.Prerelease != ""

	for i, d := range manifest.Dependencies {
		parts := strings.SplitN(d, ":", 3)
		switch len(parts) {
		case 1:
			name = parts[0]
			version = ""
		case 2:
			if parts[1] == "*" {
				name = parts[0]
				version = ""
			} else if parsedVersion, err := ParseUniversalPackageVersion(parts[1]); err == nil {
				name = parts[0]
				version = parsedVersion.String()
			} else {
				name = parts[0] + "/" + parts[1]
				version = ""
			}
		case 3:
			name = parts[0] + "/" + parts[1]
			if parts[2] == "*" {
				version = ""
			} else {
				version = parts[2]
			}
		}
		tree.Dependencies[i], err = b.BuildTree(name, version, prerelease)
		if err != nil {
			return nil, err
		}
	}

	return tree, nil
}

func (b *BuildDependencyTree) ReadUpack(name, version string, prerelease bool) (manifest *PackageMetadata, dirs map[string]struct{}, files map[string][sha256.Size]byte, err error) {
	r, size, done, err := b.OpenPackage(name, version, prerelease)
	if err != nil {
		return nil, nil, nil, err
	}
	defer func() {
		if e := done(); err == nil {
			err = e
		}
	}()

	zip, err := zip.NewReader(r, size)
	if err != nil {
		return nil, nil, nil, err
	}

	dirs = make(map[string]struct{})
	files = make(map[string][sha256.Size]byte)

	for _, f := range zip.File {
		if f.Name == "upack.json" {
			manifest, err = b.ReadManifest(f)
			if err != nil {
				return nil, nil, nil, err
			}
		}
		name := strings.Replace(f.Name, "\\", "/", -1)
		if !strings.HasPrefix(name, "package/") {
			continue
		}
		name = strings.TrimRight(name[len("package/"):], "/")
		if name == "" {
			continue
		}
		if f.Mode().IsDir() {
			dirs[name] = struct{}{}
		} else {
			hash, err := b.ComputeHash(f)
			if err != nil {
				return nil, nil, nil, err
			}
			files[name] = hash
		}
		for {
			i := strings.LastIndex(name, "/")
			if i == -1 {
				break
			}
			name = name[:i]
			dirs[name] = struct{}{}
		}
	}

	return manifest, dirs, files, nil
}

func (b *BuildDependencyTree) ReadManifest(f *zip.File) (manifest *PackageMetadata, err error) {
	r, err := f.Open()
	if err != nil {
		return
	}
	defer func() {
		if e := r.Close(); err == nil {
			err = e
		}
	}()

	return ReadManifest(r)
}

func (b *BuildDependencyTree) ComputeHash(f *zip.File) (hash [sha256.Size]byte, err error) {
	r, err := f.Open()
	if err != nil {
		return
	}
	defer func() {
		if e := r.Close(); err == nil {
			err = e
		}
	}()

	h := sha256.New()
	_, err = io.Copy(h, r)
	if err != nil {
		return
	}

	h.Sum(hash[:0])
	return
}

func (b *BuildDependencyTree) OpenPackage(packageName, packageVersion string, prerelease bool) (io.ReaderAt, int64, func() error, error) {
	var group, name string
	var version *UniversalPackageVersion

	parts := strings.Split(strings.Replace(packageName, ":", "/", -1), "/")
	if len(parts) == 1 {
		name = parts[0]
	} else {
		group = strings.Join(parts[:len(parts)-1], "/")
		name = parts[len(parts)-1]
	}

	versionString, err := GetVersion(b.SourceURL, group, name, packageVersion, b.Authentication, prerelease)
	if err != nil {
		return nil, 0, nil, err
	}
	version, err = ParseUniversalPackageVersion(versionString)
	if err != nil {
		return nil, 0, nil, err
	}

	var userName *string
	u, err := user.Current()
	if err == nil {
		userName = &u.Username
	}

	if b.TargetDirectory != nil {
		err = b.registry.RegisterPackage(group, name, version, *b.TargetDirectory, b.SourceURL, b.Authentication, b.Comment, nil, userName)
		if err != nil {
			return nil, 0, nil, err
		}
	}

	f, done, err := b.cache.GetOrDownload(group, name, version, b.SourceURL, b.Authentication, true)
	if err != nil {
		return nil, 0, nil, err
	}

	fi, err := f.Stat()
	if err != nil {
		_ = done()
		return nil, 0, nil, err
	}

	return f, fi.Size(), done, nil
}
