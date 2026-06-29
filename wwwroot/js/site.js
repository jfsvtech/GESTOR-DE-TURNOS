(() => {
  const storedTheme = localStorage.getItem("turnos-theme");
  if (storedTheme === "dark" || (!storedTheme && window.matchMedia("(prefers-color-scheme: dark)").matches)) {
    document.documentElement.dataset.theme = "dark";
  }

  const syncThemeButton = () => {
    const isDark = document.documentElement.dataset.theme === "dark";
    document.querySelectorAll("[data-theme-toggle]").forEach(button => {
      button.setAttribute("aria-label", isDark ? "Activar modo claro" : "Activar modo oscuro");
      button.setAttribute("title", isDark ? "Modo claro" : "Modo oscuro");
      const icon = button.querySelector("i");
      if (icon) icon.className = isDark ? "bi bi-sun" : "bi bi-moon-stars";
    });
    document.querySelector('meta[name="theme-color"]')?.setAttribute("content", isDark ? "#0B1120" : "#059669");
  };
  syncThemeButton();

  document.querySelector("[data-theme-toggle]")?.addEventListener("click", () => {
    const next = document.documentElement.dataset.theme === "dark" ? "light" : "dark";
    document.documentElement.dataset.theme = next;
    localStorage.setItem("turnos-theme", next);
    syncThemeButton();
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

  const loader = document.querySelector("[data-app-loader]");
  const showLoader = () => loader?.classList.add("is-active");
  const hideLoader = () => loader?.classList.remove("is-active");
  window.addEventListener("pageshow", hideLoader);
  document.addEventListener("click", event => {
    const link = event.target.closest("a[href]");
    if (!link) return;
    const url = new URL(link.href, location.href);
    const isSameApp = url.origin === location.origin;
    const isDownload = link.hasAttribute("download");
    const opensNewTab = link.target && link.target !== "_self";
    if (isSameApp && !isDownload && !opensNewTab && `${url.pathname}${url.search}` !== `${location.pathname}${location.search}`) {
      showLoader();
    }
  });

  document.querySelectorAll("form").forEach(form => {
    form.addEventListener("submit", () => {
      const submitter = form.querySelector("button[type='submit'], .btn-primary");
      if (!submitter || submitter.dataset.noLoading === "true") return;
      submitter.classList.add("is-loading");
      submitter.setAttribute("aria-busy", "true");
    });
  });

  const toastRoot = document.createElement("div");
  toastRoot.className = "toast-stack";
  document.body.appendChild(toastRoot);
  document.querySelectorAll(".alert").forEach(alert => {
    const isError = alert.classList.contains("alert-danger");
    const toast = document.createElement("div");
    toast.className = `app-toast ${isError ? "error" : "success"}`;
    toast.innerHTML = `<i class="bi ${isError ? "bi-exclamation-triangle" : "bi-check-circle"}"></i><span>${alert.textContent.trim()}</span>`;
    toastRoot.appendChild(toast);
    setTimeout(() => toast.classList.add("show"), 80);
    setTimeout(() => toast.classList.remove("show"), 4200);
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
