package upack

import (
	"bytes"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"io/ioutil"
	"net/http"
	"net/url"
	"os"
	"os/user"
	"path/filepath"
	"runtime"
	"strings"
	"time"

	"github.com/google/uuid"
)

type Registry string

var (
	Machine = func() Registry {
		if runtime.GOOS == "windows" {
			return Registry(filepath.Join(os.Getenv("ProgramData"), "upack"))
		}
		return "/var/lib/upack"
	}()
	User = func() Registry {
		u, err := user.Current()
		if err != nil {
			panic(err)
		}
		return Registry(filepath.Join(u.HomeDir, ".upack"))
	}()
	Unregistered = Registry("")
)

func (r Registry) retry(task func() error) error {
	var err error

	for tries := 0; tries < 1000; tries++ {
		err = task()
		if err == nil {
			return nil
		}

		if _, ok := err.(RegistryLocked); !ok {
			return err
		}

		fmt.Fprint(os.Stderr, err)
		time.Sleep(time.Second)
		fmt.Fprint(os.Stderr, ".")
		time.Sleep(time.Second)
		fmt.Fprint(os.Stderr, ".")
		time.Sleep(time.Second)
		fmt.Fprintln(os.Stderr, ".")
	}

	return err
}

func (r Registry) withLock(task func() error, description string) (err error) {
	if description != "" && strings.Contains(description, "\n") {
		return errors.New("description must not contain line breaks")
	}

	err = os.MkdirAll(string(r), 0777)
	if err != nil {
		return err
	}

	lockPath := filepath.Join(string(r), ".lock")
	f, err := os.OpenFile(lockPath, os.O_CREATE|os.O_EXCL|os.O_WRONLY, 0666)
	if err != nil {
		if !os.IsExist(err) {
			return err
		}

		fi, err := os.Stat(lockPath)
		if err != nil {
			if os.IsNotExist(err) {
				return RegistryLocked{"Registry lock deleted while checking for lock."}
			}
			return err
		}
		lastWrite := fi.ModTime()
		if lastWrite.Add(10 * time.Second).Before(time.Now()) {
			f, err = os.OpenFile(lockPath, os.O_CREATE|os.O_RDWR, 0666)
			if err != nil {
				return err
			}
			fi, err = f.Stat()
			if err != nil {
				_ = f.Close()
				return err
			}
			if lastWrite != fi.ModTime() && fi.Size() != 0 {
				_ = f.Close()
				return registryLocked(lockPath)
			}
			err = f.Truncate(0)
			if err != nil {
				_ = f.Close()
				return err
			}
		} else {
			return registryLocked(lockPath)
		}
	}

	guid := uuid.New()

	if description == "" {
		description = os.Args[0]
	}
	_, err = fmt.Fprintf(f, "[%d] %s\n%v\n", os.Getpid(), description, guid)
	if err != nil {
		_ = f.Close()
		return err
	}
	err = f.Close()
	if err != nil {
		return err
	}

	defer func() {
		b, e := ioutil.ReadFile(lockPath)
		if e != nil {
			if os.IsNotExist(e) {
				e = errors.New("Registry lock file was deleted by another process.")
			}
			if err == nil {
				err = e
			}
			return
		}
		lockLines := strings.Split(strings.TrimSuffix(string(b), "\n"), "\n")
		if len(lockLines) != 2 || lockLines[1] != guid.String() {
			e = errors.New("Registry lock token did not match.")
			if err == nil {
				err = e
			}
		}
		e = os.Remove(lockPath)
		if err == nil {
			err = e
		}
	}()

	return task()
}

type RegistryLocked struct {
	Err string
}

func (err RegistryLocked) Error() string { return err.Err }

func registryLocked(lockPath string) error {
	b, err := ioutil.ReadFile(lockPath)
	if err != nil {
		b = nil
	}
	i := bytes.IndexAny(b, "\r\n")
	if i != -1 {
		b = b[:i]
	}
	lockDescription := string(b)
	if lockDescription == "" {
		lockDescription = "No description provided."
	}
	return RegistryLocked{"Registry is locked: " + lockDescription}
}

func (r Registry) ListInstalledPackages() ([]*InstalledPackage, error) {
	if r == "" {
		return nil, nil
	}

	var installedPackages []*InstalledPackage
	err := r.retry(func() error {
		return r.withLock(func() error {
			f, err := os.Open(filepath.Join(string(r), "installedPackages.json"))
			if err != nil {
				if os.IsNotExist(err) {
					return nil
				}
				return err
			}
			defer f.Close()
			return json.NewDecoder(f).Decode(&installedPackages)
		}, "listing installed packages")
	})
	if err != nil {
		return nil, err
	}
	return installedPackages, nil
}

func (r Registry) getCachedPackagePath(group, name string, version *UniversalPackageVersion) string {
	return filepath.Join(string(r), "packageCache", strings.Replace(group, "/", "$", -1)+"$"+name, name+"."+version.String()+".upack")
}

func (r Registry) RegisterPackage(group, name string, version *UniversalPackageVersion, intendedPath, feedURL string, feedAuthentication *[2]string, installationReason, installedUsing, installedBy *string) error {
	if r == "" {
		return nil
	}

	return r.retry(func() error {
		return r.withLock(func() error {
			var packages []*InstalledPackage
			f, err := os.Open(filepath.Join(string(r), "installedPackages.json"))
			if err == nil {
				err = json.NewDecoder(f).Decode(&packages)
				if err != nil {
					_ = f.Close()
					return err
				}
				err = f.Close()
				if err != nil {
					return err
				}
			} else if !os.IsNotExist(err) {
				return err
			}

			for _, pkg := range packages {
				if strings.EqualFold(pkg.Group, group) && strings.EqualFold(pkg.Name, name) && pkg.Version.Equals(version) {
					return nil
				}
			}

			if installedUsing == nil {
				installedUsing = new(string)
				*installedUsing = "upack/" + Version
			}

			packages = append(packages, &InstalledPackage{
				Group:              group,
				Name:               name,
				Version:            version,
				Path:               &intendedPath,
				FeedURL:            &feedURL,
				InstallationDate:   &InstalledPackageDate{time.Now().UTC(), ""},
				InstallationReason: installationReason,
				InstalledUsing:     installedUsing,
				InstalledBy:        installedBy,
			})

			f, err = os.Create(filepath.Join(string(r), "installedPackages.json"))
			if err != nil {
				return err
			}
			defer f.Close()

			err = json.NewEncoder(f).Encode(&packages)
			return err
		}, "checking installation status of "+group+"/"+name+" "+version.String())
	})
}

func (r Registry) cachePackageToDisk(w io.Writer, group, name string, version *UniversalPackageVersion, feedURL string, feedAuthentication *[2]string) error {
	encodedName := url.PathEscape(name)
	if group != "" {
		encodedName = url.PathEscape(group) + "/" + encodedName
	}

	req, err := http.NewRequest("GET", strings.TrimRight(feedURL, "/")+"/download/"+encodedName+"/"+url.QueryEscape(version.String()), nil)
	if err != nil {
		return err
	}

	if feedAuthentication != nil {
		req.SetBasicAuth(feedAuthentication[0], feedAuthentication[1])
	}

	resp, err := http.DefaultClient.Do(req)
	if err != nil {
		return err
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		return fmt.Errorf("downloading package: %s", resp.Status)
	}

	_, err = io.Copy(w, resp.Body)
	return err
}

func (r Registry) GetOrDownload(group, name string, version *UniversalPackageVersion, feedURL string, feedAuthentication *[2]string, cache bool) (*os.File, func() error, error) {
	if r == "" || !cache {
		f, err := ioutil.TempFile("", "upack")
		if err != nil {
			return nil, nil, err
		}
		name := f.Name()

		err = r.cachePackageToDisk(f, group, name, version, feedURL, feedAuthentication)
		if err == nil {
			_, err = f.Seek(0, io.SeekStart)
		}
		if err != nil {
			_ = f.Close()
			_ = os.Remove(name)
			return nil, nil, err
		}

		return f, func() error {
			err := f.Close()
			if e := os.Remove(name); err == nil {
				err = e
			}
			return err
		}, nil
	}

	cachePath := r.getCachedPackagePath(group, name, version)

	f, err := os.Open(cachePath)
	if err == nil {
		return f, f.Close, nil
	}

	if !os.IsNotExist(err) {
		return nil, nil, err
	}

	err = os.MkdirAll(filepath.Dir(cachePath), 0777)
	if err != nil {
		return nil, nil, err
	}

	f, err = os.OpenFile(cachePath, os.O_CREATE|os.O_EXCL|os.O_RDWR, 0666)
	if err != nil {
		return nil, nil, err
	}

	err = r.cachePackageToDisk(f, group, name, version, feedURL, feedAuthentication)
	if err != nil {
		_ = f.Close()
		_ = os.Remove(cachePath)
		return nil, nil, err
	}

	_, err = f.Seek(0, io.SeekStart)
	if err != nil {
		_ = f.Close()
		_ = os.Remove(cachePath)
		return nil, nil, err
	}

	return f, f.Close, nil
}

type InstalledPackage struct {
	Group   string                   `json:"group,omitempty"`
	Name    string                   `json:"name"`
	Version *UniversalPackageVersion `json:"version"`

	// The absolute path on disk where the package was installed to.
	Path *string `json:"path"`

	// An absolute URL of the universal feed where the package was installed from.
	FeedURL *string `json:"feedURL,omitempty"`

	// The UTC date when the package was installed.
	InstallationDate *InstalledPackageDate `json:"installationDate,omitempty"`

	// The reason or purpose of the installation.
	InstallationReason *string `json:"installationReason"`

	// The mechanism used to install the package. There are no format restrictions, but we recommend treating it like a User Agent string and including the tool name and version.
	InstalledUsing *string `json:"installedUsing,omitempty"`

	// The person or service that performed the installation.
	InstalledBy *string `json:"installedBy,omitempty"`
}

func (i InstalledPackage) groupAndName() string {
	if i.Group != "" {
		return i.Group + "/" + i.Name
	}
	return i.Name
}

type InstalledPackageDate struct {
	Date time.Time

	originalText string
}

const installedPackageDateLegacyFormat = "2006-01-02T15:04:05"

func (i InstalledPackageDate) MarshalText() ([]byte, error) {
	if i.originalText != "" {
		return []byte(i.originalText), nil
	}
	return []byte(i.Date.Format(time.RFC3339Nano)), nil
}

func (i *InstalledPackageDate) UnmarshalText(b []byte) error {
	i.originalText = string(b)
	t, err := time.ParseInLocation(installedPackageDateLegacyFormat, i.originalText, time.UTC)
	if err != nil {
		t, err = time.Parse(time.RFC3339Nano, i.originalText)
	}
	if err != nil {
		return err
	}
	i.Date = t
	return nil
}
