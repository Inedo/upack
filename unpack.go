package main

import (
	"archive/zip"
	"context"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"strings"

	"github.com/google/subcommands"
)

type unpackCommand struct {
	overwrite bool
}

func (cmd *unpackCommand) Name() string { return "unpack" }

func (cmd *unpackCommand) Synopsis() string {
	return "Extracts the contents of a ProGet universal package to a directory."
}

func (cmd *unpackCommand) Usage() string {
	return "upack unpack «package» «target» [--overwrite]\n\n" +
		"package: Path of a valid .upack file.\n" +
		"target: Directory where the contents of the package will be extracted.\n"
}

func (cmd *unpackCommand) SetFlags(flagSet *flag.FlagSet) {
	flagSet.BoolVar(&cmd.overwrite, "overwrite", false, "When specified, Overwrite files in the target directory.")
}

func (cmd *unpackCommand) Execute(ctx context.Context, f *flag.FlagSet, args ...interface{}) (exit subcommands.ExitStatus) {
	if f.NArg() != 2 {
		f.Usage()
		return subcommands.ExitUsageError
	}

	zr, err := zip.OpenReader(f.Arg(0))
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return subcommands.ExitFailure
	}

	info, err := readZipMetadata(&zr.Reader)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return subcommands.ExitFailure
	}

	info.print()

	if err = unpack(&zr.Reader, cmd.overwrite, f.Arg(1)); err != nil {
		fmt.Fprintln(os.Stderr, err)
		return subcommands.ExitFailure
	}

	return subcommands.ExitSuccess
}

func unpack(zr *zip.Reader, overwrite bool, path string) error {
	mode := os.O_CREATE | os.O_WRONLY | os.O_EXCL
	if overwrite {
		mode = os.O_CREATE | os.O_WRONLY | os.O_TRUNC
	}

	for _, file := range zr.File {
		if !strings.HasPrefix(file.Name, "package/") {
			continue
		}
		target := filepath.Join(path, file.Name[len("package/"):])
		if file.Mode().IsDir() {
			if err := os.MkdirAll(target, 0755); err != nil {
				return err
			}
		} else {
			if err := os.MkdirAll(filepath.Dir(target), 0755); err != nil {
				return err
			}

			if err := writeFile(file, target, mode); err != nil {
				return err
			}
		}
	}

	return nil
}

func writeFile(file *zip.File, target string, mode int) (err error) {
	r, err := file.Open()
	if err != nil {
		return err
	}
	defer func() {
		if e := r.Close(); err == nil {
			err = e
		}
	}()

	w, err := os.OpenFile(target, mode, 0644)
	if err != nil {
		return err
	}
	defer func() {
		if e := w.Close(); err == nil {
			err = e
		}
	}()

	_, err = io.Copy(w, r)
	return
}

func readZipMetadata(zr *zip.Reader) (info packageMetadata, err error) {
	var metadataFile *zip.File

	for _, file := range zr.File {
		if file.Name == "upack.json" {
			metadataFile = file
			break
		}
	}

	if metadataFile == nil {
		err = errors.New("Invalid upack file: missing upack.json.")
		return
	}

	mf, err := metadataFile.Open()
	if err != nil {
		return
	}
	defer func() {
		if e := mf.Close(); err == nil {
			err = e
		}
	}()

	err = json.NewDecoder(mf).Decode(&info)
	return
}
