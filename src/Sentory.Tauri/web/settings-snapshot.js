export function mergeSettingsSnapshot(
  current,
  incoming,
  { replaceTheme = false, pendingSources = null } = {},
) {
  const merged = replaceTheme || !current?.themeMode
    ? incoming
    : { ...incoming, themeMode: current.themeMode };
  if (!pendingSources?.size) return merged;

  const sources = { ...merged.sources };
  for (const [source, enabled] of pendingSources) {
    sources[source] = enabled;
  }
  return { ...merged, sources };
}
