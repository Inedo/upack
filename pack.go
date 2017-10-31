package main

import (
	"archive/zip"
	"bytes"
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
)

type Pack struct {
	Manifest        string
	Metadata        PackageMetadata
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
			TrySetValue: trySetStringValue("source", func(cmd Command) *string {
				return &cmd.(*Pack).SourceDirectory
			}),
		},
	}
}
func (*Pack) ExtraArguments() []ExtraArgument {
	return []ExtraArgument{
		{
			Name:        "metadata",
			Description: "Path of a valid upack.json metadata file.",
			TrySetValue: trySetStringValue("manifest", func(cmd Command) *string {
				return &cmd.(*Pack).Manifest
			}),
		},
		{
			Name:        "targetDirectory",
			Description: "Directory where the .upack file will be created. If not specified, the current working directory is used.",
			TrySetValue: trySetStringValue("targetDirectory", func(cmd Command) *string {
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
	if info.Name == "" {
		fmt.Fprintln(os.Stderr, "Missing package name.")
		return 2
	}
	if info.Version == "" {
		fmt.Fprintln(os.Stderr, "Missing package version.")
		return 2
	}

	PrintManifest(info)

	fileName := filepath.Join(p.TargetDirectory, info.Name+"-"+info.BareVersion()+".upack")
	zipStream, err := os.Create(fileName)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
	}
	defer zipStream.Close()

	zipFile := zip.NewWriter(zipStream)

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

	return 0
}

func (p *Pack) ReadManifest() (*PackageMetadata, error) {
	f, err := os.Open(p.Manifest)
	if err != nil {
		return nil, err
	}
	defer f.Close()

	return ReadManifest(f)
}
