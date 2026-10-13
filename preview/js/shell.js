window.Shell = (function () {
  const pages = [
    ["overview", "index.html", "nav.overview"],
    ["games", "games.html", "nav.games"],
    ["tops", "tops.html", "nav.tops"],
    ["tags", "tags.html", "nav.tags"],
    null,
    ["developer", "developer.html", "nav.developer"],
    ["developers", "developers.html", "nav.developers"],
    ["jams", "jams.html", "nav.jams"],
    ["mygame", "mygame.html", "nav.mygame"],
    null,
    ["data", "data.html", "nav.data"],
  ];

  function icon(id) {
    const paths = {
      overview: "M4 4h7v7H4zm9 0h7v7h-7zM4 13h7v7H4zm9 0h7v7h-7z",
      games: "M6 8h12v8H6zM8 6h2M14 6h2",
      tops: "M8 18V10m4 8V6m4 12v-5",
      tags: "M4 12l8-8h6v6l-8 8z",
      developer: "M4 18l6-10 4 6 2-3 4 7z",
      developers: "M8 11a3 3 0 1 0 0-6 3 3 0 0 0 0 6zm8 1a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5zM3 19c1-3 3-4 5-4s4 1 5 4m2-3c2 0 3 1 4 3",
      jams: "M5 10c2-4 12-4 14 0-4 1-10 1-14 0zm2 3h10v5H7z",
      mygame: "M12 4v4m0 8v4M4 12h4m8 0h4M7 7l2.5 2.5M14.5 14.5L17 17M17 7l-2.5 2.5M9.5 14.5L7 17",
      data: "M12 4v10m0 0l-4-4m4 4l4-4M5 19h14",
    };
    return `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7">${(paths[id] || "").split("M").filter(Boolean).map((d) => `<path d="M${d}"/>`).join("")}</svg>`;
  }

  function nav(active) {
    return pages.map((item) => {
      if (!item) return `<div class="gap"></div>`;
      const [id, href, key] = item;
      const on = active === id || (active === "game" && id === "games") || (active === "tag" && id === "tags") || (active === "author" && id === "developers") || (active === "jam" && id === "jams");
      return `<a class="${on ? "on" : ""}" href="${href}">${icon(id)}<span>${I18n.t(key)}</span></a>`;
    }).join("");
  }

  function mount() {
    const page = document.body.dataset.page || "overview";
    document.documentElement.lang = I18n.lang();
    document.title = `${I18n.t("page." + page)} — ItchDB`;
    const lang = I18n.lang();
    const app = document.getElementById("app");
    app.innerHTML = `
      <header class="appbar">
        <button class="icon-btn menu-btn" type="button" id="menu" aria-label="Menu">☰</button>
        <div class="logo">Itch<span>DB</span></div>
        <div class="sub">${I18n.t("brand.sub")}</div>
        <div class="spacer"></div>
        <div class="filters" title="${I18n.t("filters.off")}">
          <label class="filter"><input type="checkbox" disabled /> ${I18n.t("filters.nsfw")}</label>
          <label class="filter"><input type="checkbox" checked disabled /> ${I18n.t("filters.unrated")}</label>
          <label class="filter">${I18n.t("filters.min")}
            <select disabled><option>≥ 4</option></select>
          </label>
        </div>
        <div class="langs" role="group">
          <button type="button" data-lang="en" class="${lang === "en" ? "on" : ""}">${I18n.t("lang.en")}</button>
          <button type="button" data-lang="ru" class="${lang === "ru" ? "on" : ""}">${I18n.t("lang.ru")}</button>
        </div>
      </header>
      <div class="backdrop" id="backdrop"></div>
      <div class="layout">
        <nav class="nav" id="nav">${nav(page)}</nav>
        <main class="content" id="slot"></main>
      </div>`;
    document.getElementById("slot").innerHTML = window.Pages.render(page);
    document.querySelectorAll("[data-lang]").forEach((btn) => {
      btn.addEventListener("click", () => I18n.setLang(btn.dataset.lang));
    });
    const drawer = document.getElementById("nav");
    const backdrop = document.getElementById("backdrop");
    const close = () => { drawer.classList.remove("open"); backdrop.classList.remove("show"); };
    document.getElementById("menu").addEventListener("click", () => {
      drawer.classList.toggle("open");
      backdrop.classList.toggle("show");
    });
    backdrop.addEventListener("click", close);
    document.querySelectorAll("[data-tabs]").forEach((box) => {
      box.querySelectorAll(".tab").forEach((tab) => {
        tab.addEventListener("click", () => {
          box.querySelectorAll(".tab").forEach((t) => t.classList.remove("on"));
          box.querySelectorAll(".panel").forEach((p) => p.classList.remove("on"));
          tab.classList.add("on");
          const panel = box.querySelector(`[data-panel="${tab.dataset.tab}"]`);
          if (panel) panel.classList.add("on");
        });
      });
    });
  }

  return { mount };
})();
