package main

import (
	"archive/zip"
	"bytes"
	"encoding/json"
	"fmt"
	"io/ioutil"
	"os"
	"path/filepath"
	"strings"
)

type Pack struct {
	Manifest        string
	Metadata        UniversalPackageMetadata
	SourceDirectory string
	TargetDirectory string
}

func (*Pack) Name() string { return "pack" }
func (*Pack) Description() string {
	return "Creates a new ProGet universal package using specified metadata and source directory."
}

func (p *Pack) Help() string  { return defaultCommandHelp(p) }
func (p *Pack) Usage() string { return defaultCommandUsage(p) }

func (*Pack) PositionalArguments() []PositionalArgument {
	return []PositionalArgument{
		{
			Name:        "source",
			Description: "Directory containing files to add to the package.",
			Index:       0,
			TrySetValue: trySetPathValue("source", func(cmd Command) *string {
				return &cmd.(*Pack).SourceDirectory
			}),
		},
	}
}
func (*Pack) ExtraArguments() []ExtraArgument {
	return []ExtraArgument{
		{
			Name:        "manifest",
			Alias:       []string{"metadata"},
			Description: "Path of a valid upack.json metadata file.",
			TrySetValue: trySetPathValue("manifest", func(cmd Command) *string {
				return &cmd.(*Pack).Manifest
			}),
		},
		{
			Name:        "targetDirectory",
			Description: "Directory where the .upack file will be created. If not specified, the current working directory is used.",
			TrySetValue: trySetPathValue("targetDirectory", func(cmd Command) *string {
				return &cmd.(*Pack).TargetDirectory
			}),
		},
		{
			Name:        "group",
			Description: "Package group. If metadata file is provided, value will be ignored.",
			TrySetValue: trySetStringValue("group", func(cmd Command) *string {
				return &cmd.(*Pack).Metadata.Group
			}),
		},
		{
			Name:        "name",
			Description: "Package name. If metadata file is provided, value will be ignored.",
			TrySetValue: trySetStringValue("name", func(cmd Command) *string {
				return &cmd.(*Pack).Metadata.Name
			}),
		},
		{
			Name:        "version",
			Description: "Package version. If metadata file is provided, value will be ignored.",
			TrySetValue: trySetStringValue("version", func(cmd Command) *string {
				return &cmd.(*Pack).Metadata.Version
			}),
		},
		{
			Name:        "title",
			Description: "Package title. If metadata file is provided, value will be ignored.",
			TrySetValue: trySetStringValue("title", func(cmd Command) *string {
				return &cmd.(*Pack).Metadata.Title
			}),
		},
		{
			Name:        "description",
			Description: "Package description. If metadata file is provided, value will be ignored.",
			TrySetValue: trySetStringValue("description", func(cmd Command) *string {
				return &cmd.(*Pack).Metadata.Description
			}),
		},
		{
			Name:        "icon",
			Description: "Icon absolute URL. If metadata file is provided, value will be ignored.",
			TrySetValue: trySetStringValue("icon", func(cmd Command) *string {
				return &cmd.(*Pack).Metadata.IconURL
			}),
		},
	}
}

func (p *Pack) Run() int {
	if p.TargetDirectory == "" {
		p.TargetDirectory = "."
	}

	info := &p.Metadata
	if p.Manifest != "" {
		var err error
		info, err = p.ReadManifest()
		if err != nil {
			fmt.Fprintln(os.Stderr, err)
			return 1
		}
	}

	err := ValidateManifest(info)
	if err != nil {
		thing := "upack.json:"
		if strings.TrimSpace(p.Manifest) == "" {
			thing = "parameters:"
		}
		fmt.Fprintln(os.Stderr, "Invalid", thing, err)
		return 2
	}

	PrintManifest(info)

	fi, err := os.Stat(p.SourceDirectory)
	if os.IsNotExist(err) || (err == nil && !fi.IsDir()) {
		fmt.Fprintf(os.Stderr, "The source directory '%s' does not exist.\n", p.SourceDirectory)
		return 2
	} else if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
	}

	_, err = os.Stat(filepath.Join(p.SourceDirectory, info.Name+"-"+info.BareVersion()+".upack"))
	if err == nil {
		fmt.Fprintln(os.Stderr, "Warning: output file already exists in source directory and may be included inadvertently in the package contents.")
	} else if !os.IsNotExist(err) {
		fmt.Fprintln(os.Stderr, err)
		return 1
	}

	targetFileName := filepath.Join(p.TargetDirectory, info.Name+"-"+info.BareVersion()+".upack")
	tmpFile, err := ioutil.TempFile("", "upack")
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
	}
	tmpPath := tmpFile.Name()
	defer func() {
		if tmpFile != nil {
			_ = tmpFile.Close()
			_ = os.Remove(tmpPath)
		}
	}()

	zipFile := zip.NewWriter(tmpFile)

	if p.Manifest != "" {
		err = CreateEntryFromFile(zipFile, p.Manifest, "upack.json")
		if err != nil {
			fmt.Fprintln(os.Stderr, err)
			return 1
		}
	} else {
		var buf bytes.Buffer
		err = json.NewEncoder(&buf).Encode(&p.Metadata)
		if err != nil {
			fmt.Fprintln(os.Stderr, err)
			return 1
		}

		err = CreateEntryFromStream(zipFile, &buf, "upack.json")
		if err != nil {
			fmt.Fprintln(os.Stderr, err)
			return 1
		}
	}

	err = AddDirectory(zipFile, p.SourceDirectory, "package/")
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
	}

	err = zipFile.Close()
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
	}

	err = os.MkdirAll(filepath.Dir(targetFileName), 0755)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
	}
	err = os.Remove(targetFileName)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
	}
	err = tmpFile.Close()
	tmpFile = nil
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
	}
	err = os.Rename(targetFileName, tmpPath)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
	}

	return 0
}

func (p *Pack) ReadManifest() (*UniversalPackageMetadata, error) {
	f, err := os.Open(p.Manifest)
	if err != nil {
		return nil, err
	}
	defer f.Close()

	return ReadManifest(f)
}
