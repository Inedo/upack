package main

import (
	"archive/zip"
	"context"
	"encoding/json"
	"flag"
	"fmt"
	"io"
	"os"
	"path/filepath"

	"github.com/google/subcommands"
)

type packCommand struct {
	target string
}

func (cmd *packCommand) Name() string { return "pack" }

func (cmd *packCommand) Synopsis() string {
	return "Creates a new ProGet universal package using specified metadata and source directory."
}

func (cmd *packCommand) Usage() string {
	return "upack pack «metadata» «source» [--targetDirectory=«targetDirectory»]\n\n" +
		"metadata: Path of a valid upack.json metadata file.\n" +
		"source: Directory containing files to add to the package.\n"
}

func (cmd *packCommand) SetFlags(flagSet *flag.FlagSet) {
	flagSet.StringVar(&cmd.target, "targetDirectory", ".", "Directory where the .upack file will be created. If not specified, the current working directory is used.")
}

func (cmd *packCommand) Execute(ctx context.Context, f *flag.FlagSet, args ...interface{}) (exit subcommands.ExitStatus) {
	if f.NArg() != 2 {
		return subcommands.ExitUsageError
	}

	var info packageMetadata

	m, err := os.Open(f.Arg(0))
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return subcommands.ExitFailure
	}
	defer func() {
		if err := m.Close(); err != nil {
			fmt.Fprintln(os.Stderr, err)
			exit = subcommands.ExitFailure
		}
	}()

	if err = json.NewDecoder(m).Decode(&info); err != nil {
		fmt.Fprintln(os.Stderr, err)
		return subcommands.ExitFailure
	}

	info.print()

	if _, err = m.Seek(0, io.SeekStart); err != nil {
		fmt.Fprintln(os.Stderr, err)
		return subcommands.ExitFailure
	}

	fileName := filepath.Join(cmd.target, info.Name+"-"+info.Version+".upack")
	zf, err := os.OpenFile(fileName, os.O_CREATE|os.O_EXCL|os.O_WRONLY, 0644)
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

	zw := zip.NewWriter(zf)
	defer func() {
		if err := zw.Close(); err != nil {
			fmt.Fprintln(os.Stderr, err)
			exit = subcommands.ExitFailure
		}
	}()

	if err := createEntryFromFile(zw, f.Arg(0), "upack.json"); err != nil {
		fmt.Fprintln(os.Stderr, err)
		return subcommands.ExitFailure
	}

	if err := addDirectory(zw, f.Arg(1), "package/"); err != nil {
		fmt.Fprintln(os.Stderr, err)
		return subcommands.ExitFailure
	}

	return subcommands.ExitSuccess
}

func createEntryFromFile(zw *zip.Writer, name, entry string) (err error) {
	f, err := os.Open(name)
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

	zh, err := zip.FileInfoHeader(fi)
	if err != nil {
		return
	}

	zh.Name = entry

	w, err := zw.CreateHeader(zh)
	if err != nil {
		return
	}

	_, err = io.Copy(w, f)

	return
}

func addDirectory(zw *zip.Writer, name, entry string) (err error) {
	f, err := os.Open(name)
	if err != nil {
		return
	}
	defer func() {
		if e := f.Close(); err == nil {
			err = e
		}
	}()

	contents, err := f.Readdir(0)
	if err != nil {
		return
	}

	fi, err := f.Stat()
	if err != nil {
		return
	}

	zh, err := zip.FileInfoHeader(fi)
	if err != nil {
		return
	}

	zh.Name = entry

	_, err = zw.CreateHeader(zh)
	if err != nil {
		return
	}

	for _, fi := range contents {
		if fi.IsDir() {
			err = addDirectory(zw, filepath.Join(name, fi.Name()), entry+fi.Name()+"/")
		} else {
			err = createEntryFromFile(zw, filepath.Join(name, fi.Name()), entry+fi.Name())
		}
		if err != nil {
			return
		}
	}

	return
}
