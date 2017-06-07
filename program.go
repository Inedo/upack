package main

import "os"

const Version = "2.1.0"

func main() {
	commands.Main(os.Args[1:])
}
