package upack

import (
	"fmt"
	"os"
	"strings"
)

var commands = CommandDispatcher{
	&Pack{},
	&Push{},
	&Unpack{},
	&Install{},
	&List{},
	&Repack{},
	&Verify{},
	&Hash{},
}

type CommandDispatcher []Command

func (cd CommandDispatcher) Main(args []string) {
	var onlyPositional bool
	var hadError bool

	var positional []string
	extra := make(map[string]*string)

	for _, arg := range args {
		if onlyPositional || !strings.HasPrefix(arg, "--") {
			positional = append(positional, arg)
		} else if arg == "--" {
			onlyPositional = true
			continue
		} else {
			parts := strings.SplitN(arg[len("--"):], "=", 2)
			if _, ok := extra[strings.ToLower(parts[0])]; ok {
				hadError = true
			}

			if len(parts) == 1 {
				extra[parts[0]] = nil
			} else {
				extra[parts[0]] = &parts[1]
			}
		}
	}

	if len(positional) > 0 && strings.EqualFold("help", positional[0]) {
		hadError = true
		positional = positional[1:]
	}

	var cmd Command
	if len(positional) == 0 {
		hadError = true
	} else {
		for _, command := range cd {
			cmd = command
			if !strings.EqualFold(command.Name(), positional[0]) {
				cmd = nil
				continue
			}

			if hadError {
				break
			}

			positional = positional[1:]

			for _, arg := range cmd.PositionalArguments() {
				if arg.Index < len(positional) {
					if !arg.TrySetValue(cmd, &positional[arg.Index]) {
						hadError = true
					}
				} else if !arg.Optional {
					hadError = true
				}
			}

			if len(positional) > len(cmd.PositionalArguments()) {
				hadError = true
			}

			for _, arg := range cmd.ExtraArguments() {
				if s, ok := extra[strings.ToLower(arg.Name)]; ok {
					if !arg.TrySetValue(cmd, s) {
						hadError = true
					}
					delete(extra, strings.ToLower(arg.Name))
				} else {
					any := false
					for _, a := range arg.Alias {
						if s, ok := extra[strings.ToLower(a)]; ok {
							if !arg.TrySetValue(cmd, s) {
								hadError = true
							}
							delete(extra, strings.ToLower(a))
							any = true
							break
						}
					}
					if !any && arg.Required {
						hadError = true
					}
				}
			}

			if len(extra) != 0 {
				hadError = true
			}

			break
		}
	}

	if hadError || cmd == nil {
		if cmd != nil {
			cd.ShowHelp(cmd)
		} else {
			cd.ShowGenericHelp()
		}
		os.Exit(2)
	} else {
		os.Exit(cmd.Run())
	}
}

func (cd CommandDispatcher) ShowGenericHelp() {
	fmt.Fprintln(os.Stderr, "upack", Version)
	fmt.Fprintln(os.Stderr, "Usage: upack «command»")
	fmt.Fprintln(os.Stderr)

	for _, command := range commands {
		fmt.Fprintln(os.Stderr, command.Name(), "-", command.Description())
	}
}

func (cd CommandDispatcher) ShowHelp(cmd Command) {
	fmt.Fprintln(os.Stderr, cmd.Help())
}
