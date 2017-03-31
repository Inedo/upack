package main

import (
	"archive/zip"
	"context"
	"encoding/json"
	"flag"
	"fmt"
	"io"
	"io/ioutil"
	"net/http"
	"net/url"
	"os"
	"strings"

	"github.com/google/subcommands"
)

type installCommand struct {
	sourceURL  string
	target     string
	user       string
	overwrite  bool
	prerelease bool
}

func (cmd *installCommand) Name() string { return "install" }

func (cmd *installCommand) Synopsis() string {
	return "Downloads the specified ProGet universal package and extracts its contents to a directory."
}

func (cmd *installCommand) Usage() string {
	return "upack install «package» [«version»] --source=«sourceUrl»\n" +
		"    --target=«targetDirectory» [--user=«authentication»] [--overwrite]\n\n" +
		"package: Package name and group, such as group:name.\n" +
		"version: Package version. If not specified, the latest version is retrieved.\n"
}

func (cmd *installCommand) SetFlags(f *flag.FlagSet) {
	f.StringVar(&cmd.sourceURL, "sourceUrl", "", "URL of a upack API endpoint.")
	f.StringVar(&cmd.target, "target", "", "Directory where the contents of the package will be extracted.")
	f.StringVar(&cmd.user, "user", "", "User name and password to use for servers that require authentication. Example: username:password")
	f.BoolVar(&cmd.overwrite, "overwrite", false, "When specified, Overwrite files in the target directory.")
	f.BoolVar(&cmd.prerelease, "prerelease", false, "When version is not specified, will install the latest prerelase version instead of the latest stable version.")
}

func (cmd *installCommand) Execute(ctx context.Context, f *flag.FlagSet, args ...interface{}) (exit subcommands.ExitStatus) {
	if f.NArg() < 1 || f.NArg() > 2 || cmd.sourceURL == "" || cmd.target == "" {
		f.Usage()
		return subcommands.ExitUsageError
	}

	packageName := f.Arg(0)
	var version string
	if f.NArg() >= 2 {
		version = f.Arg(1)
	}

	url, err := formatDownloadURL(cmd.sourceURL, packageName, version, cmd.user, cmd.prerelease)
	if err != nil {
		fmt.Fprintln(os.Stderr, "Preparing download URL:", err)
		return subcommands.ExitFailure
	}

	tf, err := ioutil.TempFile("", "upack")
	if err != nil {
		fmt.Fprintln(os.Stderr, "Creating temporary file:", err)
		return subcommands.ExitFailure
	}
	defer func() {
		if err := os.Remove(tf.Name()); err != nil {
			fmt.Fprintln(os.Stderr, "Removing temporary file:", err)
			exit = subcommands.ExitFailure
		}
	}()
	defer func() {
		if err := tf.Close(); err != nil {
			fmt.Fprintln(os.Stderr, "Closing temporary file:", err)
			exit = subcommands.ExitFailure
		}
	}()

	req, err := http.NewRequest("GET", url, nil)
	if err != nil {
		fmt.Fprintln(os.Stderr, "Preparing to download upack package:", err)
		return subcommands.ExitFailure
	}

	setBasicAuth(req, cmd.user)

	resp, err := http.DefaultClient.Do(req)
	if err != nil {
		fmt.Fprintln(os.Stderr, "Downloading upack package:", err)
		return subcommands.ExitFailure
	}
	defer func() {
		if err := resp.Body.Close(); err != nil {
			fmt.Fprintln(os.Stderr, "Closing HTTP response:", err)
			exit = subcommands.ExitFailure
		}
	}()

	if resp.StatusCode != http.StatusOK {
		fmt.Fprintf(os.Stderr, "upack API returned %s\n", resp.Status)
		return subcommands.ExitFailure
	}
	_, err = io.Copy(tf, resp.Body)
	if err != nil {
		fmt.Fprintln(os.Stderr, "Copying upack package to a temporary file:", err)
		return subcommands.ExitFailure
	}

	fi, err := tf.Stat()
	if err != nil {
		fmt.Fprintln(os.Stderr, "Computing the size of the package:", err)
		return subcommands.ExitFailure
	}

	zr, err := zip.NewReader(tf, fi.Size())
	if err != nil {
		fmt.Fprintln(os.Stderr, "Reading the package:", err)
		return subcommands.ExitFailure
	}

	info, err := readZipMetadata(zr)
	if err != nil {
		fmt.Fprintln(os.Stderr, "Reading upack metadata:", err)
		return subcommands.ExitFailure
	}
	info.print()

	if err = unpack(zr, cmd.overwrite, cmd.target); err != nil {
		fmt.Fprintln(os.Stderr, "Unpacking the package:", err)
		return subcommands.ExitFailure
	}

	return subcommands.ExitSuccess
}

func formatDownloadURL(source, packageName, version, credentials string, prerelease bool) (s string, err error) {
	parts := strings.Split(packageName, ":")
	encodedName := url.PathEscape(parts[0])
	if len(parts) > 1 {
		encodedName += "/" + url.PathEscape(parts[1])
	}

	if version != "" || !prerelease {
		if version == "" || strings.EqualFold(version, "latest") {
			return strings.TrimRight(source, "/") + "/download/" + encodedName + "?latest", nil
		}
		return strings.TrimRight(source, "/") + "/download/" + encodedName + "/" + url.QueryEscape(version), nil
	}
	legacyGroupParts := strings.Split(encodedName, "/")
	group := ""
	if len(legacyGroupParts) > 1 {
		group = strings.Join(legacyGroupParts[:len(legacyGroupParts)-1], "%2f")
		encodedName = legacyGroupParts[len(legacyGroupParts)-1]
	}

	req, err := http.NewRequest("GET", strings.TrimRight(source, "/")+"/packages?group="+group+"&name="+encodedName, nil)
	if err != nil {
		return
	}

	setBasicAuth(req, credentials)

	resp, err := http.DefaultClient.Do(req)
	if err != nil {
		return
	}
	defer func() {
		if e := resp.Body.Close(); err == nil {
			err = e
		}
	}()

	if resp.StatusCode != http.StatusOK {
		err = fmt.Errorf("upack API returned %s", resp.Status)
		return
	}

	var data remotePackageMetadata
	err = json.NewDecoder(resp.Body).Decode(&data)
	if err != nil {
		return
	}

	return formatDownloadURL(source, packageName, data.latestPrereleaseVersion(), credentials, false)
}
