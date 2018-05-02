package main

import (
	"archive/zip"
	"encoding/json"
	"fmt"
	"io"
	"io/ioutil"
	"os"
	"path/filepath"
	"strings"

	"github.com/pkg/errors"
)

type Repack struct {
	Manifest           string
	SourcePath         string
	TargetDirectory    string
	Group              string
	PackageName        string
	Version            string
	Title              string
	PackageDescription string
	IconUrl            string
	Overwrite          bool
}

func (*Repack) Name() string { return "repack" }
func (*Repack) Description() string {
	return "Creates a new ProGet universal package from an existing package with optionally modified metadata."
}

func (r *Repack) Help() string  { return defaultCommandHelp(r) }
func (r *Repack) Usage() string { return defaultCommandUsage(r) }

func (*Repack) PositionalArguments() []PositionalArgument {
	return []PositionalArgument{
		{
			Name:        "source",
			Description: "The path of the existing upack file.",
			Index:       0,
			TrySetValue: trySetPathValue("source", func(cmd Command) *string {
				return &cmd.(*Repack).SourcePath
			}),
		},
	}
}

func (*Repack) ExtraArguments() []ExtraArgument {
	return []ExtraArgument{
		{
			Name:        "manifest",
			Alias:       []string{"metadata"},
			Description: "Path of upack.json file to merge.",
			TrySetValue: trySetPathValue("manifest", func(cmd Command) *string {
				return &cmd.(*Repack).Manifest
			}),
		},
		{
			Name:        "targetDirectory",
			Description: "Directory where the .upack file will be created. If not specified, the current working directory is used.",
			TrySetValue: trySetPathValue("targetDirectory", func(cmd Command) *string {
				return &cmd.(*Repack).TargetDirectory
			}),
		},
		{
			Name:        "group",
			Description: "Package group. If metadata file is provided, value will be ignored.",
			TrySetValue: trySetStringValue("group", func(cmd Command) *string {
				return &cmd.(*Repack).Group
			}),
		},
		{
			Name:        "name",
			Description: "Package name. If metadata file is provided, value will be ignored.",
			TrySetValue: trySetStringValue("name", func(cmd Command) *string {
				return &cmd.(*Repack).PackageName
			}),
		},
		{
			Name:        "version",
			Description: "Package version. If metadata file is provided, value will be ignored.",
			TrySetValue: trySetStringValue("version", func(cmd Command) *string {
				return &cmd.(*Repack).Version
			}),
		},
		{
			Name:        "title",
			Description: "Package title. If metadata file is provided, value will be ignored.",
			TrySetValue: trySetStringValue("title", func(cmd Command) *string {
				return &cmd.(*Repack).Title
			}),
		},
		{
			Name:        "description",
			Description: "Package description. If metadata file is provided, value will be ignored.",
			TrySetValue: trySetStringValue("description", func(cmd Command) *string {
				return &cmd.(*Repack).PackageDescription
			}),
		},
		{
			Name:        "icon",
			Description: "Icon absolute Url. If metadata file is provided, value will be ignored.",
			TrySetValue: trySetStringValue("icon", func(cmd Command) *string {
				return &cmd.(*Repack).IconUrl
			}),
		},
		{
			Name:        "overwrite",
			Description: "Overwrite existing package file if it already exists.",
			Flag:        true,
			TrySetValue: trySetBoolValue("overwrite", func(cmd Command) *bool {
				return &cmd.(*Repack).Overwrite
			}),
		},
	}
}

func (r *Repack) Run() int {
	info, err := GetPackageMetadata(r.SourcePath)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
	}
	infoToMerge, err := r.GetMetadataToMerge()
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
	}

	prop := func(dest *string, src string) {
		if src != "" {
			*dest = src
		}
	}
	prop(&info.Group, infoToMerge.Group)
	prop(&info.Name, infoToMerge.Name)
	prop(&info.Version, infoToMerge.Version)
	prop(&info.Title, infoToMerge.Title)
	prop(&info.Description, infoToMerge.Description)
	prop(&info.IconURL, infoToMerge.IconURL)
	if len(infoToMerge.Dependencies) != 0 {
		info.Dependencies = infoToMerge.Dependencies
	}
	err = ValidateManifest(info)
	if err != nil {
		thing := "upack.json:"
		if strings.TrimSpace(r.Manifest) == "" {
			thing = "parameters:"
		}
		fmt.Fprintln(os.Stderr, "Invalid", thing, err)
		return 2
	}

	PrintManifest(info)

	relativePackageFileName := info.Name + "-" + info.BareVersion() + ".upack"
	targetFileName, err := filepath.Abs(filepath.Join(r.TargetDirectory, relativePackageFileName))
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
	}

	if !r.Overwrite {
		_, err = os.Stat(targetFileName)
		if err != nil {
			if os.IsNotExist(err) {
				fmt.Fprintf(os.Stderr, "Target file '%s' exists and overwrite was set to false.", targetFileName)
				return 1
			}
			fmt.Fprintln(os.Stderr, err)
			return 1
		}
	}

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

	existingPackage, err := zip.OpenReader(r.SourcePath)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
	}
	builder := zip.NewWriter(tmpFile)
	w, err := builder.Create("upack.json")
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
	}
	err = json.NewEncoder(w).Encode(info)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
	}

	for _, entry := range existingPackage.File {
		if entry.Name == "upack.json" {
			continue
		}

		w, err = builder.CreateHeader(&entry.FileHeader)
		if err != nil {
			fmt.Fprintln(os.Stderr, err)
			return 1
		}

		if !entry.Mode().IsDir() {
			stream, err := entry.Open()
			if err != nil {
				fmt.Fprintln(os.Stderr, err)
				return 1
			}

			_, err = io.Copy(w, stream)
			if err != nil {
				_ = stream.Close()
				fmt.Fprintln(os.Stderr, err)
				return 1
			}

			err = stream.Close()
			if err != nil {
				fmt.Fprintln(os.Stderr, err)
				return 1
			}
		}
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

func (r *Repack) GetMetadataToMerge() (metadata *UniversalPackageMetadata, err error) {
	if strings.TrimSpace(r.Manifest) != "" {
		return &UniversalPackageMetadata{
			Group:       r.Group,
			Name:        r.PackageName,
			Version:     r.Version,
			Title:       r.Title,
			Description: r.PackageDescription,
			IconURL:     r.IconUrl,
		}, nil
	}
	metadataStream, err := os.Open(r.Manifest)
	if err != nil {
		return nil, errors.Wrapf(err, "The manifest file '%s' does not exist or could not be opened.", r.Manifest)
	}
	defer func() {
		if e := metadataStream.Close(); err == nil {
			err = errors.Wrapf(e, "The manifest file '%s' does not exist or could not be opened.", r.Manifest)
		}
	}()

	metadata, err = ReadManifest(metadataStream)
	if err != nil {
		err = errors.Wrapf(err, "The manifest file '%s' does not exist or could not be opened.", r.Manifest)
	}
	return
}
