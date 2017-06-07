package main

import (
	"archive/zip"
	"fmt"
	"os"
	"path/filepath"
)

type Pack struct {
	Manifest        string
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
			Name:        "metadata",
			Description: "Path of a valid upack.json metadata file.",
			Index:       0,
			TrySetValue: trySetStringValue("manifest", func(cmd Command) *string {
				return &cmd.(*Pack).Manifest
			}),
		},
		{
			Name:        "source",
			Description: "Directory containing files to add to the package.",
			Index:       1,
			TrySetValue: trySetStringValue("source", func(cmd Command) *string {
				return &cmd.(*Pack).SourceDirectory
			}),
		},
	}
}
func (*Pack) ExtraArguments() []ExtraArgument {
	return []ExtraArgument{
		{
			Name:        "targetDirectory",
			Description: "Directory where the .upack file will be created. If not specified, the current working directory is used.",
			TrySetValue: trySetStringValue("targetDirectory", func(cmd Command) *string {
				return &cmd.(*Pack).TargetDirectory
			}),
		},
	}
}

func (p *Pack) Run() int {
	if p.TargetDirectory == "" {
		p.TargetDirectory = "."
	}

	info, err := p.ReadManifest()
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
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

	err = CreateEntryFromFile(zipFile, p.Manifest, "upack.json")
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
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
