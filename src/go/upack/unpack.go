package upack

import (
	"archive/zip"
	"fmt"
	"os"
)

type Unpack struct {
	Package            string
	Target             string
	Overwrite          bool
	PreserveTimestamps bool
}

func (*Unpack) Name() string { return "unpack" }
func (*Unpack) Description() string {
	return "Extracts the contents of a ProGet universal package to a directory."
}

func (u *Unpack) Help() string  { return defaultCommandHelp(u) }
func (u *Unpack) Usage() string { return defaultCommandUsage(u) }

func (*Unpack) PositionalArguments() []PositionalArgument {
	return []PositionalArgument{
		{
			Name:        "package",
			Description: "Path of a valid .upack file.",
			Index:       0,
			TrySetValue: trySetStringValue("package", func(cmd Command) *string {
				return &cmd.(*Unpack).Package
			}),
		},
		{
			Name:        "target",
			Description: "Directory where the contents of the package will be extracted.",
			Index:       1,
			TrySetValue: trySetPathValue("target", func(cmd Command) *string {
				return &cmd.(*Unpack).Target
			}),
		},
	}
}
func (*Unpack) ExtraArguments() []ExtraArgument {
	return []ExtraArgument{
		{
			Name:        "overwrite",
			Description: "When specified, overwrite files in the target directory.",
			Flag:        true,
			TrySetValue: trySetBoolValue("overwrite", func(cmd Command) *bool {
				return &cmd.(*Unpack).Overwrite
			}),
		},
		{
			Name:        "preserve-timestamps",
			Description: "Set extracted file timestamps to the timestamp of the file in the archive instead of the current time.",
			Flag:        true,
			TrySetValue: trySetBoolValue("preserve-timestamps", func(cmd Command) *bool {
				return &cmd.(*Unpack).PreserveTimestamps
			}),
		},
	}
}

func (u *Unpack) Run() int {
	zipFile, err := zip.OpenReader(u.Package)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
	}
	defer zipFile.Close()

	var found bool
	for _, entry := range zipFile.File {
		if entry.Name == "upack.json" {
			info, err := u.ReadManifest(entry)
			if err != nil {
				fmt.Fprintln(os.Stderr, err)
				return 1
			}
			PrintManifest(info)
			found = true
			break
		}
	}

	if !found {
		fmt.Fprintln(os.Stderr, u.Package, "is not a upack file: missing upack.json.")
		return 1
	}

	err = UnpackZip(u.Target, u.Overwrite, &zipFile.Reader, u.PreserveTimestamps)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
	}

	return 0
}

func (u *Unpack) ReadManifest(entry *zip.File) (*UniversalPackageMetadata, error) {
	r, err := entry.Open()
	if err != nil {
		return nil, err
	}
	defer r.Close()

	return ReadManifest(r)
}
