package main

import (
	"archive/zip"
	"encoding/json"
	"fmt"
	"io"
	"io/ioutil"
	"net/http"
	"net/url"
	"os"
	"path/filepath"
	"strings"
)

type Command interface {
	Name() string
	Description() string

	Help() string
	Usage() string

	PositionalArguments() []PositionalArgument
	ExtraArguments() []ExtraArgument

	Run() int
}

type PositionalArgument struct {
	Index       int
	Name        string
	Optional    bool
	Description string
	TrySetValue func(Command, *string) bool
}

type ExtraArgument struct {
	Name        string
	Required    bool
	Description string
	Flag        bool
	TrySetValue func(Command, *string) bool
}

func (a PositionalArgument) Help() string {
	return a.Name + " - " + a.Description
}

func (a ExtraArgument) Help() string {
	return a.Name + " - " + a.Description
}

func trySetBoolValue(name string, f func(Command) *bool) func(Command, *string) bool {
	return func(cmd Command, value *string) bool {
		if value == nil || *value == "" {
			*f(cmd) = true
			return true
		}

		if strings.EqualFold(*value, "true") {
			*f(cmd) = true
			return true
		}

		if strings.EqualFold(*value, "false") {
			*f(cmd) = false
			return true
		}
		fmt.Println("--"+name, "must be \"true\" or \"false\".")
		return false
	}
}

func trySetStringValue(name string, f func(Command) *string) func(Command, *string) bool {
	return func(cmd Command, value *string) bool {
		if value == nil {
			return false
		}

		*f(cmd) = *value
		return true
	}
}

func trySetBasicAuthValue(name string, f func(Command) **[2]string) func(Command, *string) bool {
	return func(cmd Command, value *string) bool {
		if value == nil || *value == "" {
			*f(cmd) = nil
			return true
		}

		parts := strings.SplitN(*value, ":", 2)
		if len(parts) != 2 {
			fmt.Println("--"+name, "must be in the format \"username:password\".")
			return false
		}

		*f(cmd) = &[2]string{parts[0], parts[1]}
		return true
	}
}

func (a PositionalArgument) Usage() string {
	s := "«" + a.Name + "»"

	if a.Optional {
		s = "[" + s + "]"
	}

	return s
}

func (a ExtraArgument) Usage() string {
	if !a.Required && a.Flag {
		return "[--" + a.Name + "]"
	}

	s := "--" + a.Name + "=«" + a.Name + "»"

	if !a.Required {
		s = "[" + s + "]"
	}

	return s
}

func defaultCommandUsage(cmd Command) string {
	s := []byte("upack ")

	s = append(s, cmd.Name()...)

	for _, arg := range cmd.PositionalArguments() {
		s = append(s, ' ')
		s = append(s, arg.Usage()...)
	}

	for _, arg := range cmd.ExtraArguments() {
		s = append(s, ' ')
		s = append(s, arg.Usage()...)
	}

	return string(s)
}

func defaultCommandHelp(cmd Command) string {
	s := []byte("Usage: ")

	s = append(s, cmd.Usage()...)
	s = append(s, '\n', '\n')
	s = append(s, cmd.Description()...)
	s = append(s, '\n')

	for _, arg := range cmd.PositionalArguments() {
		s = append(s, '\n')
		s = append(s, arg.Help()...)
	}

	for _, arg := range cmd.ExtraArguments() {
		s = append(s, '\n')
		s = append(s, arg.Help()...)
	}

	return string(s)
}

func ReadManifest(r io.Reader) (*PackageMetadata, error) {
	var meta PackageMetadata
	err := json.NewDecoder(r).Decode(&meta)
	if err != nil {
		return nil, err
	}
	return &meta, nil
}

func PrintManifest(info *PackageMetadata) {
	fmt.Println("Package:", info.groupAndName())
	fmt.Println("Version:", info.Version)
}

func UnpackZip(targetDirectory string, overwrite bool, zipFile *zip.Reader) error {
	err := os.MkdirAll(targetDirectory, 0777)
	if err != nil {
		return err
	}

	var files int
	var directories int

	for _, entry := range zipFile.File {
		if !strings.HasPrefix(strings.ToLower(entry.Name), "package/") {
			continue
		}

		targetPath := filepath.Join(targetDirectory, entry.Name[len("package/"):])

		if entry.Mode().IsDir() {
			err = os.MkdirAll(targetPath, entry.Mode())
			if err != nil {
				return err
			}
			directories++
		} else {
			err = os.MkdirAll(filepath.Dir(targetPath), entry.Mode())
			if err != nil {
				return err
			}
			err = saveEntryToFile(entry, targetPath, overwrite)
			if err != nil {
				return err
			}

			files++
		}
	}

	fmt.Println("Extracted", files, "files and", directories, "directories.")
	return nil
}

func saveEntryToFile(entry *zip.File, targetPath string, overwrite bool) (err error) {
	r, err := entry.Open()
	if err != nil {
		return
	}
	defer func() {
		if e := r.Close(); err == nil {
			err = e
		}
	}()

	flags := os.O_WRONLY | os.O_TRUNC | os.O_CREATE
	if !overwrite {
		flags |= os.O_EXCL
	}

	f, err := os.OpenFile(targetPath, flags, entry.Mode())
	if err != nil {
		return
	}
	defer func() {
		if e := f.Close(); err == nil {
			err = e
		}
	}()

	_, err = io.Copy(f, r)
	return
}

func CreateEntryFromFile(zipFile *zip.Writer, fileName, entryPath string) (err error) {
	f, err := os.Open(fileName)
	if err != nil {
		return
	}
	defer func() {
		if e := f.Close(); err == nil {
			err = e
		}
	}()

	fi, err := f.Stat()
	if err != nil {
		return
	}

	h, err := zip.FileInfoHeader(fi)
	if err != nil {
		return
	}

	h.Name = entryPath
	h.Method = zip.Deflate

	w, err := zipFile.CreateHeader(h)
	if err != nil {
		return
	}

	_, err = io.Copy(w, f)
	return
}

func CreateEntryFromStream(zipFile *zip.Writer, file io.Reader, entryPath string) (err error) {
	w, err := zipFile.Create(entryPath)
	if err != nil {
		return
	}

	_, err = io.Copy(w, file)
	return
}

func AddDirectory(zipFile *zip.Writer, sourceDirectory, entryRootPath string) (err error) {
	fi, err := os.Stat(sourceDirectory)
	if err != nil {
		return
	}

	h, err := zip.FileInfoHeader(fi)
	if err != nil {
		return
	}

	h.Name = entryRootPath

	_, err = zipFile.CreateHeader(h)
	if err != nil {
		return
	}

	infos, err := ioutil.ReadDir(sourceDirectory)
	if err != nil {
		return
	}
	for _, fi := range infos {
		if fi.IsDir() {
			err = AddDirectory(zipFile, filepath.Join(sourceDirectory, fi.Name()), entryRootPath+fi.Name()+"/")
		} else {
			err = CreateEntryFromFile(zipFile, filepath.Join(sourceDirectory, fi.Name()), entryRootPath+fi.Name())
		}

		if err != nil {
			return
		}
	}

	return
}

func GetVersion(source, group, name, version string, credentials *[2]string, prerelease bool) (string, error) {
	if version != "" && !strings.EqualFold(version, "latest") && !prerelease {
		return version, nil
	}

	req, err := http.NewRequest("GET", strings.TrimRight(source, "/")+"/packages?"+(url.Values{"group": {group}, "name": {name}}).Encode(), nil)
	if err != nil {
		return "", err
	}

	if credentials != nil {
		req.SetBasicAuth(credentials[0], credentials[1])
	}

	resp, err := http.DefaultClient.Do(req)
	if err != nil {
		return "", err
	}
	defer resp.Body.Close()

	if resp.StatusCode >= 400 {
		return "", fmt.Errorf("ProGet returned HTTP error: %s", resp.Status)
	}

	var data RemotePackageMetadata
	err = json.NewDecoder(resp.Body).Decode(&data)
	if err != nil {
		return "", err
	}

	var latestVersion *UniversalPackageVersion
	for _, v := range data.Versions {
		version, err := ParseUniversalPackageVersion(v)
		if err != nil {
			return "", err
		}
		if !prerelease && version.Prerelease != "" {
			continue
		}
		if latestVersion == nil || latestVersion.Compare(version) < 0 {
			latestVersion = version
		}
	}
	return latestVersion.String(), nil
}
