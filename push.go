package main

import (
	"archive/zip"
	"context"
	"encoding/base64"
	"flag"
	"fmt"
	"io"
	"net/http"
	"os"

	"github.com/google/subcommands"
)

type pushCommand struct {
	user string
}

func (cmd *pushCommand) Name() string { return "push" }

func (cmd *pushCommand) Synopsis() string {
	return "Pushes a ProGet universal package to the specified ProGet feed."
}

func (cmd *pushCommand) Usage() string {
	return "upack push «package» «target» [--user=«authentication»]\n\n" +
		"package: Path of a valid .upack file.\n" +
		"target - URL of a upack API endpoint.\n"
}

func (cmd *pushCommand) SetFlags(f *flag.FlagSet) {
	f.StringVar(&cmd.user, "user", "", "User name and password for servers that require authentication. Example: username:password")
}

func (cmd *pushCommand) Execute(ctx context.Context, f *flag.FlagSet, args ...interface{}) (exit subcommands.ExitStatus) {
	if f.NArg() != 2 {
		return subcommands.ExitUsageError
	}

	zf, err := os.Open(f.Arg(0))
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return subcommands.ExitFailure
	}
	defer func() {
		if err := zf.Close(); err != nil {
			fmt.Fprintln(os.Stderr, err)
			exit = subcommands.ExitFailure
		}
	}()

	fi, err := zf.Stat()
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return subcommands.ExitFailure
	}

	zr, err := zip.NewReader(zf, fi.Size())
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return subcommands.ExitFailure
	}

	info, err := readZipMetadata(zr)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return subcommands.ExitFailure
	}

	_, err = zf.Seek(0, io.SeekStart)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return subcommands.ExitFailure
	}

	info.print()

	req, err := http.NewRequest("PUT", f.Arg(1), zf)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return subcommands.ExitFailure
	}

	setBasicAuth(req, cmd.user)
	req.Header.Set("Content-Type", "application/octet-stream")

	resp, err := http.DefaultClient.Do(req)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return subcommands.ExitFailure
	}
	defer func() {
		if err := resp.Body.Close(); err != nil {
			fmt.Fprintln(os.Stderr, err)
			exit = subcommands.ExitFailure
		}
	}()

	if resp.StatusCode == http.StatusCreated {
		fmt.Printf("%s:%s %s published!\n", info.Group, info.Name, info.Version)
		return subcommands.ExitSuccess
	}

	fmt.Fprintf(os.Stderr, "upack API returned %s\n", resp.Status)
	_, err = io.Copy(os.Stderr, resp.Body)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
	}
	return subcommands.ExitFailure

}

func setBasicAuth(req *http.Request, user string) {
	if user != "" {
		req.Header.Set("Authorization", "Basic "+base64.StdEncoding.EncodeToString([]byte(user)))
	}
}
