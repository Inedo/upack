package upack

import (
	"errors"
	"math/big"
	"regexp"
	"strings"
)

var semanticVersionRegex = regexp.MustCompile(`\A([0-9]+)\.([0-9]+)\.([0-9]+)(?:-([0-9a-zA-Z\.-]+))?(?:\+([0-9a-zA-Z\.-]+))?\z`)

type UniversalPackageVersion struct {
	Major, Minor, Patch big.Int
	Prerelease, Build   string
}

func NewUniversalPackageVersion(major, minor, patch *big.Int, prerelease, build string) *UniversalPackageVersion {
	v := &UniversalPackageVersion{Prerelease: prerelease, Build: build}
	v.Major.Set(major)
	v.Minor.Set(minor)
	v.Patch.Set(patch)
	return v
}

func (v *UniversalPackageVersion) Equals(o *UniversalPackageVersion) bool {
	if v == o {
		return true
	}
	if v == nil || o == nil {
		return false
	}

	return v.Major.Cmp(&o.Major) == 0 &&
		v.Minor.Cmp(&o.Minor) == 0 &&
		v.Patch.Cmp(&o.Patch) == 0 &&
		strings.EqualFold(v.Prerelease, o.Prerelease) &&
		strings.EqualFold(v.Build, o.Build)
}

func comparePrerelease(a, b string) int {
	if a == "" && b == "" {
		return 0
	}
	if a == "" {
		return 1
	}
	if b == "" {
		return -1
	}

	A := strings.Split(a, ".")
	B := strings.Split(b, ".")

	var index int
	for {
		if index >= len(A) && index >= len(B) {
			break
		}
		if index >= len(A) {
			return -1
		}
		if index >= len(B) {
			return 1
		}

		aIdentifier, bIdentifier := A[index], B[index]

		var aInt, bInt big.Int
		_, aIntParsed := aInt.SetString(aIdentifier, 10)
		_, bIntParsed := bInt.SetString(bIdentifier, 10)

		if aIntParsed && bIntParsed {
			diff := aInt.Cmp(&bInt)
			if diff != 0 {
				return diff
			}
		} else if !aIntParsed && bIntParsed {
			return 1
		} else if aIntParsed {
			return -1
		} else {
			diff := strings.Compare(strings.ToLower(aIdentifier), strings.ToLower(bIdentifier))
			if diff != 0 {
				return diff
			}
		}

		index++
	}

	return 0
}

func compareBuild(a, b string) int {
	if a == "" && b == "" {
		return 0
	}
	if a == "" {
		return 1
	}
	if b == "" {
		return -1
	}

	var leftNumeric big.Int
	_, isLeftNumeric := leftNumeric.SetString(a, 10)

	var rightNumeric big.Int
	_, isRightNumeric := rightNumeric.SetString(b, 10)

	if isLeftNumeric && isRightNumeric {
		return leftNumeric.Cmp(&rightNumeric)
	}

	return strings.Compare(strings.ToLower(a), strings.ToLower(b))
}

func (v *UniversalPackageVersion) Compare(o *UniversalPackageVersion) int {
	if v == o {
		return 0
	}
	if v == nil {
		return -1
	}
	if o == nil {
		return 1
	}

	diff := v.Major.Cmp(&o.Major)
	if diff != 0 {
		return diff
	}

	diff = v.Minor.Cmp(&o.Minor)
	if diff != 0 {
		return diff
	}

	diff = v.Patch.Cmp(&o.Patch)
	if diff != 0 {
		return diff
	}

	diff = comparePrerelease(v.Prerelease, o.Prerelease)
	if diff != 0 {
		return diff
	}

	diff = compareBuild(v.Build, o.Build)
	if diff != 0 {
		return diff
	}

	return 0
}

func (v *UniversalPackageVersion) HashCode() uint32 {
	return uint32(v.Major.Int64()<<20) |
		uint32(v.Minor.Int64()<<10) |
		uint32(v.Patch.Int64())
}

func (v *UniversalPackageVersion) String() string {
	buf := make([]byte, 0, 50)
	buf = v.Major.Append(buf, 10)
	buf = append(buf, '.')
	buf = v.Minor.Append(buf, 10)
	buf = append(buf, '.')
	buf = v.Patch.Append(buf, 10)

	if v.Prerelease != "" {
		buf = append(buf, '-')
		buf = append(buf, v.Prerelease...)
	}

	if v.Build != "" {
		buf = append(buf, '+')
		buf = append(buf, v.Build...)
	}

	return string(buf)
}

func ParseUniversalPackageVersion(s string) (*UniversalPackageVersion, error) {
	match := semanticVersionRegex.FindStringSubmatch(s)
	if match == nil {
		return nil, errors.New("String is not a valid semantic version.")
	}

	var major, minor, patch big.Int

	major.SetString(match[1], 10)
	minor.SetString(match[2], 10)
	patch.SetString(match[3], 10)

	return NewUniversalPackageVersion(&major, &minor, &patch, match[4], match[5]), nil
}

func (v *UniversalPackageVersion) MarshalText() ([]byte, error) {
	return []byte(v.String()), nil
}

func (v *UniversalPackageVersion) UnmarshalText(b []byte) error {
	o, err := ParseUniversalPackageVersion(string(b))
	if err != nil {
		return err
	}
	v.Major.Set(&o.Major)
	v.Minor.Set(&o.Minor)
	v.Patch.Set(&o.Patch)
	v.Prerelease = o.Prerelease
	v.Build = o.Build
	return nil
}
