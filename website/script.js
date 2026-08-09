const root = document.documentElement;
root.classList.add("js");
const themeToggle = document.querySelector(".theme-toggle");
const menuToggle = document.querySelector(".menu-toggle");
const navLinks = document.querySelector(".nav-links");

function currentTheme() {
  return root.dataset.theme || "light";
}

function syncThemeLabel() {
  const isDark = currentTheme() === "dark";
  themeToggle.textContent = isDark ? "밝게" : "어둡게";
  themeToggle.setAttribute("aria-label", isDark ? "밝은 테마로 전환" : "어두운 테마로 전환");
}

if (root.dataset.theme !== "light" && root.dataset.theme !== "dark") root.dataset.theme = "light";
syncThemeLabel();

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
});

navLinks.querySelectorAll("a").forEach((link) => {
  link.addEventListener("click", () => {
    menuToggle.setAttribute("aria-expanded", "false");
    navLinks.classList.remove("is-open");
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
