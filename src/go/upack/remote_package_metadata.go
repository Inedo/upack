package upack

type RemotePackageMetadata struct {
	Group         string   `json:"group,omitempty"`
	Name          string   `json:"name"`
	LatestVersion string   `json:"latestVersion,omitempty"`
	Versions      []string `json:"versions"`
}
