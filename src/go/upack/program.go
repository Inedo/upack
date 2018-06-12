package upack

import "os"

const Version = "2.2.2"

func Main() {
	commands.Main(os.Args[1:])
}
