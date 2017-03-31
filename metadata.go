package main

import (
	"fmt"
	"math/big"
	"regexp"
	"strings"
)

type packageMetadata struct {
	Group        string   `json:"group,omitempty"`
	Name         string   `json:"name"`
	Version      string   `json:"version"`
	Title        string   `json:"title,omitempty"`
	Description  string   `json:"description,omitempty"`
	IconURL      string   `json:"icon,omitempty"`
	Dependencies []string `json:"dependencies,omitempty"`
}

func (m packageMetadata) print() {
	fmt.Printf("Package %s:%s\n", m.Group, m.Name)
	fmt.Printf("Version: %s\n", m.Version)
}

type remotePackageMetadata struct {
	Group         string   `json:"group,omitempty"`
	Name          string   `json:"name"`
	LatestVersion string   `json:"latestVersion,omitempty"`
	Versions      []string `json:"versions"`
}

func (m remotePackageMetadata) latestPrereleaseVersion() string {
	a, err := parseUniversalPackageVersion(m.Versions[0])
	if err != nil {
		panic(err)
	}

	for i := 1; i < len(m.Versions); i++ {
		b, err := parseUniversalPackageVersion(m.Versions[i])
		if err != nil {
			panic(err)
		}

		if a.Cmp(b) < 0 {
			a = b
		}
	}

	return a.String()
}

var semanticVersionRegex = regexp.MustCompile(`\A([0-9]+)\.([0-9]+)\.([0-9]+)(?:-([0-9a-zA-Z\.-]+))?(?:\+([0-9a-zA-Z\.-]+))?\z`)

type universalPackageVersion struct {
	Major      *big.Int
	Minor      *big.Int
	Patch      *big.Int
	Prerelease string
	Build      string
}

func (v universalPackageVersion) String() string {
	s := v.Major.String() + "." + v.Minor.String() + "." + v.Patch.String()

	if v.Prerelease != "" {
		s += "-" + v.Prerelease
	}

	if v.Build != "" {
		s += "+" + v.Build
	}

	return s
}

func (a universalPackageVersion) Cmp(b universalPackageVersion) int {
	if a == b {
		return 0
	}
	if a == (universalPackageVersion{}) {
		return -1
	}
	if b == (universalPackageVersion{}) {
		return 1
	}
	if diff := a.Major.Cmp(b.Major); diff != 0 {
		return diff
	}
	if diff := a.Minor.Cmp(b.Minor); diff != 0 {
		return diff
	}
	if diff := a.Patch.Cmp(b.Patch); diff != 0 {
		return diff
	}

	if a.Prerelease == "" && b.Prerelease == "" {
		return 0
	}
	if a.Prerelease == "" {
		return 1
	}
	if b.Prerelease == "" {
		return -1
	}
	prereleaseA := strings.Split(a.Prerelease, ".")
	prereleaseB := strings.Split(b.Prerelease, ".")

	var aInt, bInt big.Int

	index := 0
	for {
		if len(prereleaseA) <= index && len(prereleaseB) <= index {
			break
		}
		if len(prereleaseA) <= index {
			return -1
		}
		if len(prereleaseB) <= index {
			return 1
		}

		_, aParsed := aInt.SetString(prereleaseA[index], 10)
		_, bParsed := bInt.SetString(prereleaseB[index], 10)

		if aParsed && bParsed {
			if diff := aInt.Cmp(&bInt); diff != 0 {
				return diff
			}
		} else if aParsed {
			return -1
		} else if bParsed {
			return 1
		} else {
			if diff := strings.Compare(prereleaseA[index], prereleaseB[index]); diff != 0 {
				return diff
			}
		}

		index++
	}

	return 0
}

func parseUniversalPackageVersion(s string) (v universalPackageVersion, err error) {
	match := semanticVersionRegex.FindStringSubmatch(s)
	if match == nil {
		err = fmt.Errorf("%q is not a valid semantic version.", s)
		return
	}

	major, _ := new(big.Int).SetString(match[1], 10)
	minor, _ := new(big.Int).SetString(match[2], 10)
	patch, _ := new(big.Int).SetString(match[3], 10)

	prerelease := match[4]
	build := match[5]

	v = universalPackageVersion{major, minor, patch, prerelease, build}
	return
}
