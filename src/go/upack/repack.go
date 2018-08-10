package upack

import (
	"archive/zip"
	"encoding/json"
	"fmt"
	"io"
	"io/ioutil"
	"os"
	"os/user"
	"path/filepath"
	"strings"
	"time"

	"github.com/pkg/errors"
)

type Repack struct {
	Manifest        string
	SourcePath      string
	TargetDirectory string
	Metadata        UniversalPackageMetadata
	Note            string
	NoAudit         bool
	Overwrite       bool
}

func (*Repack) Name() string { return "repack" }
func (*Repack) Description() string {
	return "Creates a new ProGet universal package by repackaging an existing package with a new version number and audit information."
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
			Alias:       []string{"manifest", "metadata"},
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
			Alias: []string{"group"},
			TrySetValue: trySetStringFnValue("group", func(cmd Command) func(string) {
				return (&cmd.(*Repack).Metadata).SetGroup
			}),
		},
		{
			Alias: []string{"name"},
			TrySetValue: trySetStringFnValue("name", func(cmd Command) func(string) {
				return (&cmd.(*Repack).Metadata).SetName
			}),
		},
		{
			Name:        "newVersion",
			Alias:       []string{"version"},
			Description: "New package version to use.",
			TrySetValue: trySetStringFnValue("newVersion", func(cmd Command) func(string) {
				return (&cmd.(*Repack).Metadata).SetVersion
			}),
		},
		{
			Alias: []string{"title"},
			TrySetValue: trySetStringFnValue("title", func(cmd Command) func(string) {
				return (&cmd.(*Repack).Metadata).SetTitle
			}),
		},
		{
			Alias: []string{"description"},
			TrySetValue: trySetStringFnValue("description", func(cmd Command) func(string) {
				return (&cmd.(*Repack).Metadata).SetDescription
			}),
		},
		{
			Alias: []string{"icon"},
			TrySetValue: trySetStringFnValue("icon", func(cmd Command) func(string) {
				return (&cmd.(*Repack).Metadata).SetIconURL
			}),
		},
		{
			Name:        "note",
			Description: "A description of the purpose for repackaging that will be entered as the audit note.",
			TrySetValue: trySetStringValue("note", func(cmd Command) *string {
				return &cmd.(*Repack).Note
			}),
		},
		{
			Alias: []string{"no-audit"},
			Flag:  true,
			TrySetValue: trySetBoolValue("no-audit", func(cmd Command) *bool {
				return &cmd.(*Repack).NoAudit
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
	if r.NoAudit && r.Note != "" {
		fmt.Fprintln(os.Stderr, "--no-audit cannot be used with --note.")
		return 2
	}

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
	hash, err := GetSHA1(r.SourcePath)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
	}

	id := info.groupAndName() + ":" + info.Version() + ":" + hash

	prop := func(dest func(string), src string) {
		if src != "" {
			dest(src)
		}
	}
	prop(info.SetGroup, infoToMerge.Group())
	prop(info.SetName, infoToMerge.Name())
	prop(info.SetVersion, infoToMerge.Version())
	prop(info.SetTitle, infoToMerge.Title())
	prop(info.SetDescription, infoToMerge.Description())
	prop(info.SetIconURL, infoToMerge.IconURL())
	if len(infoToMerge.Dependencies()) != 0 {
		info.SetDependencies(infoToMerge.Dependencies())
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

	if !r.NoAudit {
		var history []interface{}
		if h, ok := (*info)["repackageHistory"]; ok {
			history = h.([]interface{})
		} else {
			history = make([]interface{}, 0, 1)
		}

		entry := map[string]interface{}{
			"id":    id,
			"date":  time.Now().UTC().Format(time.RFC3339),
			"using": "upack/" + Version,
		}

		currentUser, err := user.Current()
		if err == nil {
			entry["by"] = currentUser.Name
		}

		if r.Note != "" {
			entry["reason"] = r.Note
		}

		history = append(history, entry)
		(*info)["repackageHistory"] = history
	}

	relativePackageFileName := info.Name() + "-" + info.BareVersion() + ".upack"
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
		return &r.Metadata, nil
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
