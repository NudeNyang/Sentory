export function mergeSettingsSnapshot(current, incoming, { replaceTheme = false } = {}) {
  if (replaceTheme || !current?.themeMode) return incoming;
  return { ...incoming, themeMode: current.themeMode };
}
