package upack

import (
	"archive/zip"
	"crypto/sha1"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"io"
	"io/ioutil"
	"net/http"
	"net/url"
	"os"
	"path/filepath"
	"strings"

	"github.com/pkg/errors"
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
	Description string
	TrySetValue func(Command, *string) bool
	Optional    bool
}

type ExtraArgument struct {
	Name        string
	Alias       []string
	Description string
	TrySetValue func(Command, *string) bool
	Required    bool
	Flag        bool
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

func trySetStringFnValue(name string, f func(Command) func(string)) func(Command, *string) bool {
	return func(cmd Command, value *string) bool {
		if value == nil {
			return false
		}

		f(cmd)(*value)
		return true
	}
}

func trySetPathValue(name string, f func(Command) *string) func(Command, *string) bool {
	return func(cmd Command, value *string) bool {
		if value == nil {
			return false
		}

		p, err := filepath.Abs(*value)
		if err != nil {
			fmt.Println("--"+name, "must be a valid path.")
			return false
		}

		*f(cmd) = p
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

func ReadManifest(r io.Reader) (*UniversalPackageMetadata, error) {
	var meta UniversalPackageMetadata
	err := json.NewDecoder(r).Decode(&meta)
	if err != nil {
		return nil, err
	}
	return &meta, nil
}

func PrintManifest(info *UniversalPackageMetadata) {
	fmt.Println("Package:", info.groupAndName())
	fmt.Println("Version:", info.Version())
}

func UnpackZip(targetDirectory string, overwrite bool, zipFile *zip.Reader, preserveTimestamps bool) error {
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
			err = os.MkdirAll(targetPath, 0777)
			if err != nil {
				return err
			}
			fi, err := os.Stat(targetPath)
			if err != nil {
				return err
			}
			// Honor umask and make sure directory execute is set if directory read is set.
			mode := (entry.Mode() | (entry.Mode()&0444)>>2) & fi.Mode()
			err = os.Chmod(targetPath, mode)
			if err != nil {
				return err
			}

			directories++
		} else {
			err = os.MkdirAll(filepath.Dir(targetPath), 0777)
			if err != nil {
				return err
			}
			err = saveEntryToFile(entry, targetPath, overwrite, preserveTimestamps)
			if err != nil {
				return err
			}

			files++
		}
	}

	fmt.Println("Extracted", files, "files and", directories, "directories.")
	return nil
}

func saveEntryToFile(entry *zip.File, targetPath string, overwrite, preserveTimestamps bool) (err error) {
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
	if err != nil {
		return
	}

	if preserveTimestamps && entry.Modified.Year() > 1980 {
		err = os.Chtimes(targetPath, entry.Modified, entry.Modified)
		if err != nil {
			return
		}
	}

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

	if len(data.Versions) == 0 {
		groupAndName := name
		if group != "" {
			groupAndName = group + "/" + name
		}
		return "", fmt.Errorf("No versions of package %s found.", groupAndName)
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

func GetSHA1(filePath string) (h string, err error) {
	f, err := os.Open(filePath)
	if err != nil {
		return
	}
	defer func() {
		if e := f.Close(); err == nil {
			err = e
		}
	}()

	hash := sha1.New()
	_, err = io.Copy(hash, f)
	if err != nil {
		return
	}

	h = hex.EncodeToString(hash.Sum(nil))
	return
}

func GetPackageMetadata(packagePath string) (metadata *UniversalPackageMetadata, err error) {
	pkg, err := zip.OpenReader(packagePath)
	if err != nil {
		return nil, errors.Wrapf(err, "The source package '%s' does not exist or could not be opened.", packagePath)
	}
	defer func() {
		if e := pkg.Close(); err == nil {
			err = errors.Wrapf(e, "The source package '%s' does not exist or could not be opened.", packagePath)
		}
	}()

	for _, entry := range pkg.File {
		if entry.Name == "upack.json" {
			var r io.ReadCloser
			r, err = entry.Open()
			if err != nil {
				return nil, errors.Wrapf(err, "The source package '%s' does not exist or could not be opened.", packagePath)
			}
			defer func() {
				if e := r.Close(); err == nil {
					err = errors.Wrapf(e, "The source package '%s' does not exist or could not be opened.", packagePath)
				}
			}()

			metadata, err = ReadManifest(r)
			if err != nil {
				err = errors.Wrapf(err, "The source package '%s' does not exist or could not be opened.", packagePath)
			}
			return
		}
	}
	return nil, errors.Errorf("The source package '%s' does not exist or could not be opened.", packagePath)
}

func findChars(s string, f func(rune) bool) []string {
	var chars []string
	seen := make(map[rune]bool)

	for _, r := range s {
		if f(r) && !seen[r] {
			chars = append(chars, string(r))
			seen[r] = true
		}
	}

	return chars
}

func ValidateManifest(info *UniversalPackageMetadata) error {
	if info.Group() != "" {
		if len(info.Group()) > 250 {
			return errors.New("group must be between 0 and 250 characters long.")
		}

		invalid := findChars(info.Group(), func(c rune) bool {
			return (c < '0' || c > '9') && (c < 'a' || c > 'z') && (c < 'A' || c > 'Z') && c != '-' && c != '.' && c != '/' && c != '_'
		})
		if len(invalid) == 1 {
			return errors.New("group contains invalid character: '" + invalid[0] + "'")
		} else if len(invalid) > 1 {
			return errors.New("group contains invalid characters: '" + strings.Join(invalid, "', '") + "'")
		}

		if strings.HasPrefix(info.Group(), "/") || strings.HasSuffix(info.Group(), "/") {
			return errors.New("group must not start or end with a slash.")
		}
	}

	{
		if info.Name() == "" {
			return errors.New("missing name.")
		}
		if len(info.Name()) > 50 {
			return errors.New("name must be between 1 and 50 characters long.")
		}

		invalid := findChars(info.Name(), func(c rune) bool {
			return (c < '0' || c > '9') && (c < 'a' || c > 'z') && (c < 'A' || c > 'Z') && c != '-' && c != '.' && c != '_'
		})
		if len(invalid) == 1 {
			return errors.New("name contains invalid character: '" + invalid[0] + "'")
		} else if len(invalid) > 1 {
			return errors.New("name contains invalid characters: '" + strings.Join(invalid, "', '") + "'")
		}
	}

	_, err := ParseUniversalPackageVersion(info.Version())
	if err != nil {
		return errors.New("missing or invalid version.")
	}

	if len(info.Title()) > 50 {
		return errors.New("title must be between 0 and 50 characters long.")
	}

	return nil
}
