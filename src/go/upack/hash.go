package upack

import (
	"fmt"
	"os"
)

type Hash struct {
	PackagePath string
}

func (*Hash) Name() string { return "hash" }
func (*Hash) Description() string {
	return "Calculates the SHA1 hash of a local package and writes it to standard output."
}

func (h *Hash) Help() string  { return defaultCommandHelp(h) }
func (h *Hash) Usage() string { return defaultCommandUsage(h) }

func (*Hash) PositionalArguments() []PositionalArgument {
	return []PositionalArgument{
		{
			Name:        "package",
			Description: "Path of a valid .upack file.",
			Index:       0,
			TrySetValue: trySetPathValue("package", func(cmd Command) *string {
				return &cmd.(*Hash).PackagePath
			}),
		},
	}
}

func (*Hash) ExtraArguments() []ExtraArgument { return nil }

func (h *Hash) Run() int {
	sha1, err := GetSHA1(h.PackagePath)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
	}

	fmt.Println(sha1)

	return 0
}
