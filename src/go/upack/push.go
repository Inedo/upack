package upack

import (
	"archive/zip"
	"fmt"
	"net/http"
	"os"
)

type Push struct {
	Package        string
	Target         string
	Authentication *[2]string
}

func (*Push) Name() string { return "push" }
func (*Push) Description() string {
	return "Pushes a ProGet universal package to the specified ProGet feed."
}

func (p *Push) Help() string  { return defaultCommandHelp(p) }
func (p *Push) Usage() string { return defaultCommandUsage(p) }

func (*Push) PositionalArguments() []PositionalArgument {
	return []PositionalArgument{
		{
			Name:        "package",
			Description: "Path of a valid .upack file.",
			Index:       0,
			TrySetValue: trySetStringValue("package", func(cmd Command) *string {
				return &cmd.(*Push).Package
			}),
		},
		{
			Name:        "target",
			Description: "URL of a upack API endpoint.",
			Index:       1,
			TrySetValue: trySetStringValue("target", func(cmd Command) *string {
				return &cmd.(*Push).Target
			}),
		},
	}
}
func (*Push) ExtraArguments() []ExtraArgument {
	return []ExtraArgument{
		{
			Name:        "user",
			Description: "User name and password to use for servers that require authentication. Example: username:password",
			TrySetValue: trySetBasicAuthValue("user", func(cmd Command) **[2]string {
				return &cmd.(*Push).Authentication
			}),
		},
	}
}

func (p *Push) Run() int {
	packageStream, err := os.Open(p.Package)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
	}
	defer packageStream.Close()

	var info *UniversalPackageMetadata

	fi, err := packageStream.Stat()
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
	}

	zipFile, err := zip.NewReader(packageStream, fi.Size())
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
	}

	for _, entry := range zipFile.File {
		if entry.Name == "upack.json" {
			r, err := entry.Open()
			if err != nil {
				fmt.Fprintln(os.Stderr, err)
				return 1
			}

			info, err = ReadManifest(r)
			if err != nil {
				fmt.Fprintln(os.Stderr, err)
				return 1
			}
			break
		}
	}

	if info == nil {
		fmt.Fprintln(os.Stderr, "upack.json missing from upack file!")
		return 1
	}

	err = ValidateManifest(info)
	if err != nil {
		fmt.Fprintln(os.Stderr, "Invalid upack.json:", err)
		return 2
	}

	PrintManifest(info)

	req, err := http.NewRequest("PUT", p.Target, packageStream)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
	}

	req.Header.Set("Content-Length", fmt.Sprintf("%d", fi.Size()))
	req.Header.Set("Content-Type", "application/octet-stream")

	if p.Authentication != nil {
		req.SetBasicAuth(p.Authentication[0], p.Authentication[1])
	}

	resp, err := http.DefaultClient.Do(req)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusCreated {
		fmt.Fprintln(os.Stderr, resp.Status)
		return 1
	}

	fmt.Println(info.groupAndName(), info.Version(), "published!")

	return 0
}
