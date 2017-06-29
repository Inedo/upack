package main

import (
	"archive/zip"
	"fmt"
	"io"
	"io/ioutil"
	"net/http"
	"os"
	"os/user"
	"strings"
)

type Install struct {
	PackageName     string
	Version         string
	SourceURL       string
	TargetDirectory string
	Authentication  *[2]string
	Overwrite       bool
	Prerelease      bool
	Comment         *string
	UserRegistry    bool
	Unregistered    bool
	CachePackages   bool
}

func (*Install) Name() string { return "install" }
func (*Install) Description() string {
	return "Downloads the specified ProGet universal package and extracts its contents to a directory."
}

func (i *Install) Help() string  { return defaultCommandHelp(i) }
func (i *Install) Usage() string { return defaultCommandUsage(i) }

func (*Install) PositionalArguments() []PositionalArgument {
	return []PositionalArgument{
		{
			Name:        "package",
			Description: "Package name and group, such as group:name.",
			Index:       0,
			TrySetValue: trySetStringValue("package", func(cmd Command) *string {
				return &cmd.(*Install).PackageName
			}),
		},
		{
			Name:        "version",
			Description: "Package version. If not specified, the latest version is retrieved.",
			Index:       1,
			Optional:    true,
			TrySetValue: trySetStringValue("version", func(cmd Command) *string {
				return &cmd.(*Install).Version
			}),
		},
	}
}

func (*Install) ExtraArguments() []ExtraArgument {
	return []ExtraArgument{
		{
			Name:        "source",
			Description: "URL of a upack API endpoint.",
			Required:    true,
			TrySetValue: trySetStringValue("source", func(cmd Command) *string {
				return &cmd.(*Install).SourceURL
			}),
		},
		{
			Name:        "target",
			Description: "Directory where the contents of the package will be extracted.",
			Required:    true,
			TrySetValue: trySetStringValue("target", func(cmd Command) *string {
				return &cmd.(*Install).TargetDirectory
			}),
		},
		{
			Name:        "user",
			Description: "User name and password to use for servers that require authentication. Example: username:password",
			TrySetValue: trySetBasicAuthValue("user", func(cmd Command) **[2]string {
				return &cmd.(*Install).Authentication
			}),
		},
		{
			Name:        "overwrite",
			Description: "When specified, Overwrite files in the target directory.",
			Flag:        true,
			TrySetValue: trySetBoolValue("overwrite", func(cmd Command) *bool {
				return &cmd.(*Install).Overwrite
			}),
		},
		{
			Name:        "prerelease",
			Description: "When version is not specified, will install the latest prerelase version instead of the latest stable version.",
			Flag:        true,
			TrySetValue: trySetBoolValue("prerelease", func(cmd Command) *bool {
				return &cmd.(*Install).Prerelease
			}),
		},
		{
			Name:        "comment",
			Description: "The reason for installing the package, for the local registry.",
			TrySetValue: trySetStringValue("comment", func(cmd Command) *string {
				s := new(string)
				cmd.(*Install).Comment = s
				return s
			}),
		},
		{
			Name:        "userregistry",
			Description: "Register the package in the user registry instead of the machine registry.",
			Flag:        true,
			TrySetValue: trySetBoolValue("userregistry", func(cmd Command) *bool {
				return &cmd.(*Install).UserRegistry
			}),
		},
		{
			Name:        "unregistered",
			Description: "Do not register the package in a local registry.",
			Flag:        true,
			TrySetValue: trySetBoolValue("unregistered", func(cmd Command) *bool {
				return &cmd.(*Install).Unregistered
			}),
		},
		{
			Name:        "cache",
			Description: "Cache the contents of the package in the local registry.",
			Flag:        true,
			TrySetValue: trySetBoolValue("cache", func(cmd Command) *bool {
				return &cmd.(*Install).CachePackages
			}),
		},
	}
}

func (i *Install) Run() int {
	r, size, done, err := i.OpenPackage()
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
	}
	defer done()

	zip, err := zip.NewReader(r, size)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
	}

	err = UnpackZip(i.TargetDirectory, i.Overwrite, zip)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
	}

	return 0
}

func (i *Install) OpenPackage() (io.ReaderAt, int64, func() error, error) {
	var r Registry
	var group, name string
	var version *UniversalPackageVersion

	if !i.Unregistered {
		parts := strings.SplitN(i.PackageName, ":", 2)
		if len(parts) == 1 {
			name = parts[0]
		} else {
			group = parts[0]
			name = parts[1]
		}

		versionString, err := GetVersion(i.SourceURL, group, name, i.Version, i.Authentication, i.Prerelease)
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

		if i.UserRegistry {
			r = User
		} else {
			r = Machine
		}

		err = r.RegisterPackage(group, name, version, i.TargetDirectory, i.SourceURL, i.Authentication, i.Comment, nil, userName)
		if err != nil {
			return nil, 0, nil, err
		}
	}

	if i.Unregistered || !i.CachePackages {
		url, err := FormatDownloadURL(i.SourceURL, i.PackageName, i.Version, i.Authentication, i.Prerelease)
		if err != nil {
			return nil, 0, nil, err
		}

		req, err := http.NewRequest("GET", url, nil)
		if err != nil {
			return nil, 0, nil, err
		}

		if i.Authentication != nil {
			req.SetBasicAuth(i.Authentication[0], i.Authentication[1])
		}

		resp, err := http.DefaultClient.Do(req)
		if err != nil {
			return nil, 0, nil, err
		}
		defer resp.Body.Close()

		if resp.StatusCode >= 400 {
			return nil, 0, nil, fmt.Errorf("GET %q returned %s", url, resp.Status)
		}

		f, err := ioutil.TempFile("", "upack")
		if err != nil {
			return nil, 0, nil, err
		}
		fName := f.Name()

		n, err := io.Copy(f, resp.Body)
		if err != nil {
			_ = f.Close()
			_ = os.Remove(fName)
			return nil, 0, nil, err
		}

		return f, n, func() error {
			err := f.Close()
			if e := os.Remove(fName); err == nil {
				err = e
			}
			return err
		}, nil
	}

	f, err := r.GetOrDownload(group, name, version, i.SourceURL, i.Authentication)
	if err != nil {
		return nil, 0, nil, err
	}

	fi, err := f.Stat()
	if err != nil {
		_ = f.Close()
		return nil, 0, nil, err
	}

	return f, fi.Size(), f.Close, nil
}
