package upack

type UniversalPackageMetadata map[string]interface{}

func (meta UniversalPackageMetadata) getString(key string) string {
	if value, ok := meta[key]; ok {
		return value.(string)
	}
	return ""
}

func (meta *UniversalPackageMetadata) setString(key, value string) {
	if *meta == nil {
		*meta = make(UniversalPackageMetadata)
	}
	(*meta)[key] = value
}

func (meta *UniversalPackageMetadata) setStringOmitEmpty(key, value string) {
	if value == "" {
		if *meta != nil {
			delete(*meta, key)
		}
		return
	}

	meta.setString(key, value)
}

func (meta UniversalPackageMetadata) Group() string {
	return meta.getString("group")
}

func (meta *UniversalPackageMetadata) SetGroup(group string) {
	meta.setStringOmitEmpty("group", group)
}

func (meta UniversalPackageMetadata) Name() string {
	return meta.getString("name")
}

func (meta *UniversalPackageMetadata) SetName(name string) {
	meta.setString("name", name)
}

func (meta UniversalPackageMetadata) Version() string {
	return meta.getString("version")
}

func (meta UniversalPackageMetadata) SetVersion(version string) {
	meta.setString("version", version)
}

func (meta UniversalPackageMetadata) Title() string {
	return meta.getString("title")
}

func (meta *UniversalPackageMetadata) SetTitle(title string) {
	meta.setStringOmitEmpty("title", title)
}

func (meta UniversalPackageMetadata) Description() string {
	return meta.getString("description")
}

func (meta *UniversalPackageMetadata) SetDescription(description string) {
	meta.setStringOmitEmpty("description", description)
}

func (meta UniversalPackageMetadata) IconURL() string {
	return meta.getString("icon")
}

func (meta *UniversalPackageMetadata) SetIconURL(iconURL string) {
	meta.setStringOmitEmpty("icon", iconURL)
}

func (meta UniversalPackageMetadata) Dependencies() []string {
	if deps, ok := meta["dependencies"]; ok {
		ideps := deps.([]interface{})
		sdeps := make([]string, len(ideps))
		for i, d := range ideps {
			sdeps[i] = d.(string)
		}
		return sdeps
	}
	return nil
}

func (meta *UniversalPackageMetadata) SetDependencies(dependencies []string) {
	if len(dependencies) == 0 {
		if *meta != nil {
			delete(*meta, "dependencies")
		}
		return
	}

	if *meta == nil {
		*meta = make(UniversalPackageMetadata)
	}
	ideps := make([]interface{}, len(dependencies))
	for i, d := range dependencies {
		ideps[i] = d
	}
	(*meta)["dependencies"] = ideps
}

func (meta UniversalPackageMetadata) BareVersion() string {
	v, err := ParseUniversalPackageVersion(meta.Version())

	if err == nil {
		v.Prerelease = ""
		v.Build = ""
		return v.String()
	}

	return meta.Version()
}

func (meta UniversalPackageMetadata) groupAndName() string {
	if meta.Group() != "" {
		return meta.Group() + "/" + meta.Name()
	}
	return meta.Name()
}
