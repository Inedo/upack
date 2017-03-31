package main

import (
	"context"
	"flag"
	"os"

	"github.com/google/subcommands"
)

func main() {
	subcommands.Register(&packCommand{}, "")
	subcommands.Register(&pushCommand{}, "")
	subcommands.Register(&unpackCommand{}, "")
	subcommands.Register(&installCommand{}, "")
	flag.Parse()
	os.Exit(int(subcommands.Execute(context.Background())))
}
