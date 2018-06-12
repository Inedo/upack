package upack

import (
	"fmt"
	"os"
)

type List struct {
	UserRegistry bool
}

func (*List) Name() string        { return "list" }
func (*List) Description() string { return "Lists packages installed in the local registry." }

func (l *List) Help() string  { return defaultCommandHelp(l) }
func (l *List) Usage() string { return defaultCommandUsage(l) }

func (*List) PositionalArguments() []PositionalArgument {
	return nil
}
func (*List) ExtraArguments() []ExtraArgument {
	return []ExtraArgument{
		{
			Name:        "userregistry",
			Description: "List packages in the user registry instead of the machine registry.",
			Flag:        true,
			TrySetValue: trySetBoolValue("userregistry", func(cmd Command) *bool {
				return &cmd.(*List).UserRegistry
			}),
		},
	}
}

func (l *List) Run() int {
	r := Machine
	if l.UserRegistry {
		r = User
	}

	packages, err := r.ListInstalledPackages()
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
	}

	for _, pkg := range packages {
		fmt.Println(pkg.groupAndName() + " " + pkg.Version.String())
		if pkg.FeedURL != nil && *pkg.FeedURL != "" {
			fmt.Println("From", *pkg.FeedURL)
		}
		if (pkg.Path != nil && *pkg.Path != "") || pkg.InstallationDate != nil {
			path, date := "<unknown path>", "<unknown date>"
			if pkg.Path != nil && *pkg.Path != "" {
				path = *pkg.Path
			}
			if pkg.InstallationDate != nil {
				date = pkg.InstallationDate.Date.String()
			}
			fmt.Println("Installed to", path, "on", date)
		}
		if (pkg.InstalledBy != nil && *pkg.InstalledBy != "") || (pkg.InstalledUsing != nil && *pkg.InstalledUsing != "") {
			user, application := "<unknown user>", "<unknown application>"
			if pkg.InstalledBy != nil && *pkg.InstalledBy != "" {
				user = *pkg.InstalledBy
			}
			if pkg.InstalledUsing != nil && *pkg.InstalledUsing != "" {
				application = *pkg.InstalledUsing
			}
			fmt.Println("Installed by", user, "using", application)
		}
		if pkg.InstallationReason != nil && *pkg.InstallationReason != "" {
			fmt.Println("Comment:", *pkg.InstallationReason)
		}
		fmt.Println()
	}

	fmt.Println(len(packages), "packages")

	return 0
}
