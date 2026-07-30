export function createThemeApplyCoordinator({
  applyTheme,
  schedule = (callback, delay) => globalThis.setTimeout(callback, delay),
  cancel = timer => globalThis.clearTimeout(timer),
  settleDelay = 180,
}) {
  let appliedTheme = null;
  let timer = null;
  let applyToken = 0;

  function clearPending() {
    if (timer === null) return;
    cancel(timer);
    timer = null;
  }

  function apply(dark) {
    timer = null;
    if (appliedTheme === dark) return;
    appliedTheme = dark;
    const token = ++applyToken;
    try {
      Promise.resolve(applyTheme(dark)).catch(() => {
        if (applyToken === token && appliedTheme === dark) appliedTheme = null;
      });
    } catch {
      if (applyToken === token && appliedTheme === dark) appliedTheme = null;
    }
  }

  return {
    request(dark) {
      const nextTheme = Boolean(dark);
      if (appliedTheme === null) {
        clearPending();
        apply(nextTheme);
        return;
      }
      clearPending();
      if (appliedTheme === nextTheme) return;
      timer = schedule(() => apply(nextTheme), settleDelay);
    },
  };
}
