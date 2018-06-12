package upack

import (
	"encoding/json"
	"fmt"
	"net/http"
	"net/url"
	"os"
	"strings"
)

type Verify struct {
	PackagePath    string
	SourceEndpoint string
	Authentication *[2]string
}

func (*Verify) Name() string { return "verify" }
func (*Verify) Description() string {
	return "Verifies that a specified package hash matches the hash stored in a ProGet Universal feed."
}

func (v *Verify) Help() string  { return defaultCommandHelp(v) }
func (v *Verify) Usage() string { return defaultCommandUsage(v) }

func (*Verify) PositionalArguments() []PositionalArgument {
	return []PositionalArgument{
		{
			Name:        "package",
			Description: "Path of a valid .upack file.",
			Index:       0,
			TrySetValue: trySetPathValue("package", func(cmd Command) *string {
				return &cmd.(*Verify).PackagePath
			}),
		},
		{
			Name:        "source",
			Description: "URL of a upack API endpoint.",
			Index:       1,
			TrySetValue: trySetStringValue("source", func(cmd Command) *string {
				return &cmd.(*Verify).SourceEndpoint
			}),
		},
	}
}

func (v *Verify) ExtraArguments() []ExtraArgument {
	return []ExtraArgument{
		{
			Name:        "user",
			Description: "User name and password to use for servers that require authentication. Example: username:password",
			TrySetValue: trySetBasicAuthValue("user", func(cmd Command) **[2]string {
				return &cmd.(*Verify).Authentication
			}),
		},
	}
}

func (v *Verify) Run() int {
	metadata, err := GetPackageMetadata(v.PackagePath)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
	}
	req, err := http.NewRequest("GET", strings.TrimRight(v.SourceEndpoint, "/")+"/versions?"+(url.Values{"group": {metadata.Group()}, "name": {metadata.Name()}, "version": {metadata.Version()}}).Encode(), nil)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
	}

	if v.Authentication != nil {
		req.SetBasicAuth(v.Authentication[0], v.Authentication[1])
	}

	resp, err := http.DefaultClient.Do(req)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
	}
	defer resp.Body.Close()

	if resp.StatusCode >= 400 {
		fmt.Fprintln(os.Stderr, "ProGet returned HTTP error:", resp.Status)
		return 1
	}

	var remoteVersion struct {
		SHA1 string `json:"sha1"`
	}
	err = json.NewDecoder(resp.Body).Decode(&remoteVersion)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
	}

	if remoteVersion.SHA1 == "" {
		fmt.Fprintln(os.Stderr, "Package", metadata.groupAndName(), "was not found in feed.")
		return 1
	}

	sha1, err := GetSHA1(v.PackagePath)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
	}

	if sha1 != remoteVersion.SHA1 {
		fmt.Fprintln(os.Stderr, "Package SHA1 value", sha1, "did not match remote SHA1 value", remoteVersion.SHA1)
		return 1
	}

	fmt.Println("Hashes for local and remote package match:", sha1)

	return 0
}
