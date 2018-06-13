package upack

import (
	"encoding/json"
	"fmt"
	"net/http"
	"net/url"
	"os"
	"strings"
)

type Metadata struct {
	PackageName    string
	Version        string
	SourceURL      string
	Authentication *[2]string
	FilePath       string
}

func (*Metadata) Name() string { return "metadata" }
func (*Metadata) Description() string {
	return "Displays metadata for a remote ProGet universal package."
}

func (m *Metadata) Help() string  { return defaultCommandHelp(m) }
func (m *Metadata) Usage() string { return defaultCommandUsage(m) }

func (*Metadata) PositionalArguments() []PositionalArgument {
	return []PositionalArgument{
		{
			Name:        "package",
			Description: "Package name and group, such as group/name.",
			Index:       0,
			TrySetValue: trySetStringValue("package", func(cmd Command) *string {
				return &cmd.(*Metadata).PackageName
			}),
		},
		{
			Name:        "version",
			Description: "Package version. If not specified, the latest version is retrieved.",
			Optional:    true,
			Index:       1,
			TrySetValue: trySetStringValue("version", func(cmd Command) *string {
				return &cmd.(*Metadata).Version
			}),
		},
	}
}

func (*Metadata) ExtraArguments() []ExtraArgument {
	return []ExtraArgument{
		{
			Name:        "source",
			Description: "URL of a upack API endpoint.",
			Required:    true,
			TrySetValue: trySetStringValue("source", func(cmd Command) *string {
				return &cmd.(*Metadata).SourceURL
			}),
		},
		{
			Name:        "user",
			Description: "User name and password to use for servers that require authentication. Example: username:password",
			TrySetValue: trySetBasicAuthValue("user", func(cmd Command) **[2]string {
				return &cmd.(*Metadata).Authentication
			}),
		},
		{
			Name:        "file",
			Description: "The metadata file to display relative to the .upack root; the default is upack.json.",
			TrySetValue: trySetStringValue("file", func(cmd Command) *string {
				return &cmd.(*Metadata).FilePath
			}),
		},
	}
}

func (m *Metadata) Run() int {
	filePath := m.FilePath
	if filePath == "" {
		filePath = "upack.json"
	}

	addr := strings.TrimRight(m.SourceURL, "/") + "/download-file/" + url.PathEscape(m.PackageName)
	if m.Version == "" {
		addr += "?latest&path=" + url.QueryEscape(filePath)
	} else {
		v, err := ParseUniversalPackageVersion(m.Version)
		if err != nil {
			fmt.Fprintln(os.Stderr, "Invalid UPack version number:", m.Version)
			return 1
		}
		addr += "/" + url.PathEscape(v.String()) + "?path=" + url.QueryEscape(filePath)
	}

	req, err := http.NewRequest("GET", addr, nil)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
	}

	if m.Authentication != nil {
		req.SetBasicAuth(m.Authentication[0], m.Authentication[1])
	}

	resp, err := http.DefaultClient.Do(req)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
	}
	defer resp.Body.Close()

	if resp.StatusCode >= 400 {
		fmt.Fprintln(os.Stderr, "Server returned error:", resp.Status)
		return 1
	}

	dec := json.NewDecoder(resp.Body)
	dec.UseNumber()
	token, err := dec.Token()
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
	}
	if token != json.Delim('{') {
		fmt.Fprintln(os.Stderr, "Expected JSON object")
	}
	for dec.More() {
		token, err = dec.Token()
		if err != nil {
			fmt.Fprintln(os.Stderr, err)
			return 1
		}
		key := token.(string)
		var value json.RawMessage
		err = dec.Decode(&value)
		if err != nil {
			fmt.Fprintln(os.Stderr, err)
			return 1
		}
		fmt.Printf("%s = %s\n", key, string(value))
	}

	return 0
}
