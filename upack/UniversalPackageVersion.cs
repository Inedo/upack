using System;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace Inedo.ProGet.UPack
{
    internal sealed class UniversalPackageVersion : IEquatable<UniversalPackageVersion>, IComparable<UniversalPackageVersion>, IComparable
    {
        private static readonly char[] Dot = new[] { '.' };
        private static readonly Regex SemanticVersionRegex = new Regex(
            @"^(?<1>[0-9]+)\.(?<2>[0-9]+)\.(?<3>[0-9]+)(-(?<4>[0-9a-zA-Z\.-]+))?(\+(?<5>[0-9a-zA-Z\.-]+))?$",
            RegexOptions.Compiled | RegexOptions.ExplicitCapture
        );

        public UniversalPackageVersion(BigInteger major, BigInteger minor, BigInteger patch, string prerelease, string build)
        {
            this.Major = major;
            this.Minor = minor;
            this.Patch = patch;
            this.Prerelease = NullIf(prerelease, string.Empty);
            this.Build = NullIf(build, string.Empty);
        }
        public UniversalPackageVersion(BigInteger major, BigInteger minor, BigInteger patch, string prerelease)
            : this(major, minor, patch, prerelease, null)
        {
        }
        public UniversalPackageVersion(BigInteger major, BigInteger minor, BigInteger patch)
            : this(major, minor, patch, null, null)
        {
        }

        public static bool operator ==(UniversalPackageVersion a, UniversalPackageVersion b)
        {
            return Equals(a, b);
        }
        public static bool operator !=(UniversalPackageVersion a, UniversalPackageVersion b)
        {
            return !Equals(a, b);
        }
        public static bool operator <(UniversalPackageVersion a, UniversalPackageVersion b)
        {
            return Compare(a, b) < 0;
        }
        public static bool operator >(UniversalPackageVersion a, UniversalPackageVersion b)
        {
            return Compare(a, b) > 0;
        }
        public static bool operator <=(UniversalPackageVersion a, UniversalPackageVersion b)
        {
            return Compare(a, b) <= 0;
        }
        public static bool operator >=(UniversalPackageVersion a, UniversalPackageVersion b)
        {
            return Compare(a, b) >= 0;
        }

        public BigInteger Major { get; }
        public BigInteger Minor { get; }
        public BigInteger Patch { get; }
        public string Prerelease { get; }
        public string Build { get; }

        public static UniversalPackageVersion TryParse(string s)
        {
            if (string.IsNullOrEmpty(s))
                return null;

            string error;
            return ParseInternal(s, out error);
        }
        public static UniversalPackageVersion Parse(string s)
        {
            if (string.IsNullOrEmpty(s))
                throw new ArgumentNullException("s");

            string error;
            var version = ParseInternal(s, out error);
            if (version != null)
                return version;

            throw new ArgumentException(error);
        }
        public static bool Equals(UniversalPackageVersion a, UniversalPackageVersion b)
        {
            if (object.ReferenceEquals(a, b))
                return true;
            if (object.ReferenceEquals(a, null) || object.ReferenceEquals(b, null))
                return false;

            return a.Major == b.Major
                && a.Minor == b.Minor
                && a.Patch == b.Patch
                && string.Equals(a.Prerelease, b.Prerelease, StringComparison.OrdinalIgnoreCase);
        }
        public static int Compare(UniversalPackageVersion a, UniversalPackageVersion b)
        {
            if (object.ReferenceEquals(a, b))
                return 0;
            if (object.ReferenceEquals(a, null))
                return -1;
            if (object.ReferenceEquals(b, null))
                return 1;

            int diff = a.Major.CompareTo(b.Major);
            if (diff != 0)
                return diff;

            diff = a.Minor.CompareTo(b.Minor);
            if (diff != 0)
                return diff;

            diff = a.Patch.CompareTo(b.Patch);
            if (diff != 0)
                return diff;

            if (a.Prerelease == null && b.Prerelease == null)
                return 0;
            if (a.Prerelease == null && b.Prerelease != null)
                return 1;
            if (a.Prerelease != null && b.Prerelease == null)
                return -1;

            var prereleaseA = a.Prerelease.Split(Dot);
            var prereleaseB = b.Prerelease.Split(Dot);

            int index = 0;
            while (true)
            {
                var aIdentifier = index < prereleaseA.Length ? prereleaseA[index] : null;
                var bIdentifier = index < prereleaseB.Length ? prereleaseB[index] : null;

                if (aIdentifier == null && bIdentifier == null)
                    break;
                if (aIdentifier == null)
                    return -1;
                if (bIdentifier == null)
                    return 1;

                BigInteger aInt;
                BigInteger bInt;
                bool aIntParsed = BigInteger.TryParse(aIdentifier, out aInt);
                bool bIntParsed = BigInteger.TryParse(bIdentifier, out bInt);

                if (aIntParsed && bIntParsed)
                {
                    diff = aInt.CompareTo(bInt);
                    if (diff != 0)
                        return diff;
                }
                else if (!aIntParsed && bIntParsed)
                {
                    return 1;
                }
                else if (aIntParsed && !bIntParsed)
                {
                    return -1;
                }
                else
                {
                    diff = string.Compare(aIdentifier, bIdentifier);
                    if (diff != 0)
                        return diff;
                }

                index++;
            }

            return 0;
        }

        public bool Equals(UniversalPackageVersion other)
        {
            return Equals(this, other);
        }
        public override bool Equals(object obj)
        {
            return this.Equals(obj as UniversalPackageVersion);
        }
        public override int GetHashCode()
        {
            return ((int)this.Major << 20) | ((int)this.Minor << 10) | (int)this.Patch;
        }
        public override string ToString()
        {
            var buffer = new StringBuilder(50);
            buffer.Append(this.Major);
            buffer.Append('.');
            buffer.Append(this.Minor);
            buffer.Append('.');
            buffer.Append(this.Patch);

            if (this.Prerelease != null)
            {
                buffer.Append('-');
                buffer.Append(this.Prerelease);
            }

            if (this.Build != null)
            {
                buffer.Append('+');
                buffer.Append(this.Build);
            }

            return buffer.ToString();
        }
        public int CompareTo(UniversalPackageVersion other)
        {
            return Compare(this, other);
        }
        int IComparable.CompareTo(object obj)
        {
            return this.CompareTo(obj as UniversalPackageVersion);
        }

        private static UniversalPackageVersion ParseInternal(string s, out string error)
        {
            var match = SemanticVersionRegex.Match(s);
            if (!match.Success)
            {
                error = "String is not a valid semantic version.";
                return null;
            }

            var major = BigInteger.Parse(match.Groups[1].Value);
            var minor = BigInteger.Parse(match.Groups[2].Value);
            var patch = BigInteger.Parse(match.Groups[3].Value);

            var prerelease = NullIf(match.Groups[4].Value, string.Empty);
            var build = NullIf(match.Groups[5].Value, string.Empty);

            error = null;
            return new UniversalPackageVersion(major, minor, patch, prerelease, build);
        }
        private static string NullIf(string s1, string s2) => s1 == s2 ? null : s1;
    }
}
