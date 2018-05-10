package main

import "os"

const Version = "2.2.2"

func main() {
	commands.Main(os.Args[1:])
}
