(() => {
  const storedTheme = localStorage.getItem("turnos-theme");
  if (storedTheme === "dark" || (!storedTheme && window.matchMedia("(prefers-color-scheme: dark)").matches)) {
    document.documentElement.dataset.theme = "dark";
  }

  document.querySelector("[data-theme-toggle]")?.addEventListener("click", () => {
    const next = document.documentElement.dataset.theme === "dark" ? "light" : "dark";
    document.documentElement.dataset.theme = next;
    localStorage.setItem("turnos-theme", next);
  });

  const currentPath = location.pathname.replace(/\/$/, "").toLowerCase();
  document.querySelectorAll(".app-nav a[href], .mobile-tabbar a[href]").forEach(link => {
    const linkPath = new URL(link.getAttribute("href"), location.origin).pathname.replace(/\/$/, "").toLowerCase();
    if (linkPath && (currentPath === linkPath || (linkPath !== "/" && currentPath.startsWith(`${linkPath}/`)))) {
      link.classList.add("active");
      link.setAttribute("aria-current", "page");
    }
  });

  const isSecure = window.isSecureContext || location.hostname === "localhost";

  if ("serviceWorker" in navigator && isSecure) {
    window.addEventListener("load", () => {
      navigator.serviceWorker.register("/service-worker.js").catch(() => {
        // La app sigue funcionando aunque el navegador bloquee el SW.
      });
    });
  }

  let deferredInstallPrompt = null;
  const installButton = document.querySelector("[data-pwa-install]");

  window.addEventListener("beforeinstallprompt", event => {
    event.preventDefault();
    deferredInstallPrompt = event;
    if (installButton) installButton.hidden = false;
  });

  installButton?.addEventListener("click", async () => {
    if (!deferredInstallPrompt) return;
    deferredInstallPrompt.prompt();
    await deferredInstallPrompt.userChoice;
    deferredInstallPrompt = null;
    installButton.hidden = true;
  });

  window.addEventListener("appinstalled", () => {
    if (installButton) installButton.hidden = true;
  });

  const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  if (!reduceMotion) {
    const revealItems = document.querySelectorAll(".service-tile, .quick-path-item, .gallery-item, .stat, .turno-item");
    const observer = new IntersectionObserver(entries => {
      entries.forEach(entry => {
        if (entry.isIntersecting) {
          entry.target.classList.add("is-visible");
          observer.unobserve(entry.target);
        }
      });
    }, { threshold: 0.08 });

    revealItems.forEach(item => {
      item.classList.add("reveal-on-scroll");
      observer.observe(item);
    });
  }
})();
