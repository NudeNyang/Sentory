import { SUPPORTED_LANGUAGES, translations } from "./translations.js";

const root = document.documentElement;
root.classList.add("js");
const themeToggle = document.querySelector(".theme-toggle");
const menuToggle = document.querySelector(".menu-toggle");
const navLinks = document.querySelector(".nav-links");
const languagePicker = document.querySelector("[data-language-picker]");
const languageTrigger = languagePicker?.querySelector(".language-trigger");
const languageOptionsPanel = languagePicker?.querySelector(".language-options");
const languageOptions = [...(languagePicker?.querySelectorAll("[data-language-option]") || [])];
const languageCurrent = languagePicker?.querySelector("[data-language-current]");
const languageShortcuts = [...document.querySelectorAll("[data-language-shortcut]")];
const languageNames = {
  ko: "한국어",
  en: "English",
  ja: "日本語",
  "zh-CN": "简体中文"
};

function translate(key, language = root.lang || "ko") {
  return translations[language]?.[key] ?? translations.ko[key] ?? key;
}

function initialLanguage() {
  try {
    const savedLanguage = localStorage.getItem("sentory-site-language");
    return SUPPORTED_LANGUAGES.includes(savedLanguage) ? savedLanguage : "ko";
  } catch {
    return "ko";
  }
}

function applyLanguage(language, persist = false) {
  const nextLanguage = SUPPORTED_LANGUAGES.includes(language) ? language : "ko";
  root.lang = nextLanguage;
  root.dataset.language = nextLanguage;
  document.title = translate("meta.title", nextLanguage);

  document.querySelectorAll("[data-i18n]").forEach((element) => {
    element.textContent = translate(element.dataset.i18n, nextLanguage);
  });

  document.querySelectorAll("[data-i18n-aria-label]").forEach((element) => {
    element.setAttribute("aria-label", translate(element.dataset.i18nAriaLabel, nextLanguage));
  });

  document.querySelectorAll("[data-i18n-alt]").forEach((element) => {
    element.setAttribute("alt", translate(element.dataset.i18nAlt, nextLanguage));
  });

  document.querySelectorAll("[data-i18n-content]").forEach((element) => {
    element.setAttribute("content", translate(element.dataset.i18nContent, nextLanguage));
  });

  document.querySelectorAll("[data-i18n-aria-template]").forEach((element) => {
    const label = translate(element.dataset.i18nAriaTemplate, nextLanguage).replace(
      "{service}",
      element.dataset.i18nService || ""
    );
    element.setAttribute("aria-label", label);
  });

  if (languageCurrent) languageCurrent.textContent = languageNames[nextLanguage];
  languageOptions.forEach((option) => {
    option.setAttribute("aria-selected", String(option.dataset.languageOption === nextLanguage));
  });
  languageShortcuts.forEach((button) => {
    button.setAttribute("aria-pressed", String(button.dataset.languageShortcut === nextLanguage));
  });

  if (!persist) return;
  try {
    localStorage.setItem("sentory-site-language", nextLanguage);
  } catch {
    // 저장소를 사용할 수 없어도 현재 페이지의 언어 전환은 유지한다.
  }
}

function currentTheme() {
  return root.dataset.theme || "light";
}

function syncThemeLabel() {
  const isDark = currentTheme() === "dark";
  themeToggle.textContent = translate(isDark ? "theme.toLight" : "theme.toDark");
  themeToggle.setAttribute("aria-label", translate(isDark ? "theme.lightAria" : "theme.darkAria"));
}

applyLanguage(initialLanguage());
if (root.dataset.theme !== "light" && root.dataset.theme !== "dark") root.dataset.theme = "light";
syncThemeLabel();

function closeLanguagePicker(restoreFocus = false) {
  if (!languageOptionsPanel || !languageTrigger) return;
  languageOptionsPanel.hidden = true;
  languageTrigger.setAttribute("aria-expanded", "false");
  if (restoreFocus) languageTrigger.focus();
}

function openLanguagePicker() {
  if (!languageOptionsPanel || !languageTrigger) return;
  languageOptionsPanel.hidden = false;
  languageTrigger.setAttribute("aria-expanded", "true");
  const selectedOption = languageOptions.find((option) => option.getAttribute("aria-selected") === "true");
  (selectedOption || languageOptions[0])?.focus();
}

languageTrigger?.addEventListener("click", () => {
  if (languageTrigger.getAttribute("aria-expanded") === "true") closeLanguagePicker();
  else openLanguagePicker();
});

languageTrigger?.addEventListener("keydown", (event) => {
  if (event.key !== "ArrowDown" && event.key !== "ArrowUp") return;
  event.preventDefault();
  openLanguagePicker();
});

languageOptions.forEach((option, index) => {
  option.addEventListener("click", () => {
    applyLanguage(option.dataset.languageOption, true);
    syncThemeLabel();
    closeLanguagePicker(true);
  });

  option.addEventListener("keydown", (event) => {
    if (event.key === "Escape") {
      event.preventDefault();
      closeLanguagePicker(true);
      return;
    }

    const keyOffsets = { ArrowDown: 1, ArrowUp: -1 };
    if (!(event.key in keyOffsets) && event.key !== "Home" && event.key !== "End") return;
    event.preventDefault();
    const nextIndex = event.key === "Home"
      ? 0
      : event.key === "End"
        ? languageOptions.length - 1
        : (index + keyOffsets[event.key] + languageOptions.length) % languageOptions.length;
    languageOptions[nextIndex]?.focus();
  });
});

languageShortcuts.forEach((button) => {
  button.addEventListener("click", () => {
    applyLanguage(button.dataset.languageShortcut, true);
    syncThemeLabel();
    closeLanguagePicker();
  });
});

document.addEventListener("click", (event) => {
  if (!languagePicker?.contains(event.target)) closeLanguagePicker();
});

themeToggle.addEventListener("click", () => {
  const nextTheme = currentTheme() === "dark" ? "light" : "dark";
  root.dataset.theme = nextTheme;
  try {
    localStorage.setItem("sentory-site-theme", nextTheme);
  } catch {
    // 저장소를 사용할 수 없어도 현재 페이지의 테마 전환은 유지한다.
  }
  syncThemeLabel();
});

menuToggle.addEventListener("click", () => {
  const expanded = menuToggle.getAttribute("aria-expanded") === "true";
  menuToggle.setAttribute("aria-expanded", String(!expanded));
  navLinks.classList.toggle("is-open", !expanded);
  if (expanded) closeLanguagePicker();
});

navLinks.querySelectorAll("a").forEach((link) => {
  link.addEventListener("click", () => {
    menuToggle.setAttribute("aria-expanded", "false");
    navLinks.classList.remove("is-open");
    closeLanguagePicker();
  });
});

const revealElements = document.querySelectorAll(".reveal");
if ("IntersectionObserver" in window) {
  const observer = new IntersectionObserver(
    (entries) => {
      entries.forEach((entry) => {
        if (!entry.isIntersecting) return;
        entry.target.classList.add("is-visible");
        observer.unobserve(entry.target);
      });
    },
    { threshold: 0.14 }
  );
  revealElements.forEach((element) => observer.observe(element));
} else {
  revealElements.forEach((element) => element.classList.add("is-visible"));
}

const storyDemo = document.querySelector("[data-scroll-autoplay]");
const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)");

async function toggleStoryDemoPlayback() {
  if (!storyDemo) return;

  if (storyDemo.paused) {
    try {
      await storyDemo.play();
      storyDemo.dataset.autoplayState = "playing";
    } catch {
      storyDemo.dataset.autoplayState = "manual";
    }
    return;
  }

  storyDemo.pause();
  storyDemo.dataset.autoplayState = "paused";
}

if (storyDemo) {
  storyDemo.addEventListener("click", toggleStoryDemoPlayback);
  storyDemo.addEventListener("keydown", (event) => {
    if (event.key !== "Enter" && event.key !== " ") return;
    event.preventDefault();
    toggleStoryDemoPlayback();
  });
}

if (storyDemo && "IntersectionObserver" in window) {
  let playTimer = null;

  const cancelScheduledPlay = () => {
    if (playTimer === null) return;
    window.clearTimeout(playTimer);
    playTimer = null;
    storyDemo.dataset.autoplayState = "idle";
  };

  const demoObserver = new IntersectionObserver(
    ([entry]) => {
      if (!entry.isIntersecting || entry.intersectionRatio < 0.12 || reducedMotion.matches) {
        cancelScheduledPlay();
        return;
      }

      if (playTimer !== null || storyDemo.dataset.autoplayState === "playing") return;
      storyDemo.dataset.autoplayState = "waiting";
      playTimer = window.setTimeout(async () => {
        playTimer = null;

        try {
          await storyDemo.play();
          storyDemo.dataset.autoplayState = "playing";
          demoObserver.unobserve(storyDemo);
        } catch {
          storyDemo.dataset.autoplayState = "manual";
        }
      }, 300);
    },
    { threshold: [0, 0.12], rootMargin: "0px 0px -20% 0px" }
  );

  reducedMotion.addEventListener("change", () => {
    if (!reducedMotion.matches) return;
    cancelScheduledPlay();
    storyDemo.pause();
    storyDemo.dataset.autoplayState = "manual";
  });

  demoObserver.observe(storyDemo);
} else if (storyDemo) {
  storyDemo.dataset.autoplayState = "manual";
}
