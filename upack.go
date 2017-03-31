package main

import (
	"context"
	"os"

	"github.com/google/subcommands"
)

func main() {
	subcommands.Register(&packCommand{}, "")
	subcommands.Register(&pushCommand{}, "")
	subcommands.Register(&unpackCommand{}, "")
	subcommands.Register(&installCommand{}, "")
	os.Exit(int(subcommands.Execute(context.Background())))
}
