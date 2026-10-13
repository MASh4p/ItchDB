window.Pages = (function () {
  const t = (key) => I18n.t(key);
  const sample = () => window.ITCH_SAMPLE || { empty: true };

  function esc(value) {
    return String(value ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
  }

  function loc() {
    return I18n.lang() === "en" ? "en-US" : "ru-RU";
  }

  function num(value, digits) {
    if (value === null || value === undefined || Number.isNaN(Number(value))) return "—";
    return Number(value).toLocaleString(loc(), { maximumFractionDigits: digits || 0, minimumFractionDigits: digits || 0 });
  }

  function pct(value, digits) {
    if (value === null || value === undefined || Number.isNaN(Number(value))) return "—";
    return (Number(value) * 100).toLocaleString(loc(), { maximumFractionDigits: digits || 0, minimumFractionDigits: digits || 0 }) + "%";
  }

  function nm(row, ru, en) {
    if (!row) return "";
    return I18n.lang() === "en" ? (row[en] || row[ru] || "") : (row[ru] || row[en] || "");
  }

  function mail() {
    const email = (window.ITCH_CONFIG && window.ITCH_CONFIG.contactEmail) || "";
    const subject = encodeURIComponent(I18n.lang() === "en" ? "ItchDB full version" : "Полная версия ItchDB");
    return email ? `mailto:${email}?subject=${subject}` : "#";
  }

  function cta() {
    return `<aside class="cta"><p>${esc(t("cta.text"))}</p><a class="btn" href="${mail()}">${esc(t("cta.button"))}</a></aside>`;
  }

  function emptyBanner() {
    return sample().empty ? `<div class="banner">${esc(t("empty"))}</div>` : "";
  }

  function kpi(label, value, hint, color) {
    return `<article class="card"><div class="section-title">${esc(label)}</div><div class="kpi-value" style="${color ? `color:${color}` : ""}">${esc(value)}</div>${hint ? `<div class="kpi-hint">${esc(hint)}</div>` : ""}</article>`;
  }

  function bars(rows, label, value, color) {
    const max = Math.max(1, ...rows.map((r) => Number(r[value]) || 0));
    if (!rows.length) return `<p class="lead">${esc(t("empty"))}</p>`;
    return `<div class="bars">${rows.map((r) => {
      const width = Math.max(2, Math.round((Number(r[value]) || 0) / max * 100));
      return `<div class="bar-row"><span>${esc(r[label])}</span><div class="bar-track"><div class="bar-fill" style="width:${width}%;background:${color || "var(--cyan)"}"></div></div><span class="num">${esc(num(r[value], r.digits || 0))}</span></div>`;
    }).join("")}</div>`;
  }

  function gameRows(rows) {
    if (!rows || !rows.length) return `<p class="lead">—</p>`;
    return `<table><thead><tr><th>${esc(t("col.game"))}</th><th class="r">${esc(t("col.ratings"))}</th><th class="r">${esc(t("col.stars"))}</th><th>${esc(t("col.genre"))}</th><th>${esc(t("col.year"))}</th><th>${esc(t("col.price"))}</th></tr></thead><tbody>
      ${rows.map((g) => `<tr><td><a href="game.html?id=${encodeURIComponent(g.id)}">${esc(g.title)}</a></td><td class="r num">${esc(num(g.ratings))}</td><td class="r num">${g.stars == null ? "—" : esc(num(g.stars, 2))}</td><td>${esc(nm(g, "genreRu", "genreEn"))}</td><td class="num">${esc(g.year || "—")}</td><td>${esc(nm(g, "priceRu", "priceEn"))}</td></tr>`).join("")}
    </tbody></table>`;
  }

  function segmentRows(rows) {
    if (!rows || !rows.length) return "";
    return `<table><thead><tr><th>${esc(t("col.segment"))}</th><th class="r">${esc(t("col.median"))}</th><th class="r">${esc(t("col.chance"))}</th><th class="r">${esc(t("col.games"))}</th></tr></thead><tbody>
      ${rows.map((s) => `<tr><td><a href="tag.html?key=${encodeURIComponent(s.key)}">${esc(nm(s, "nameRu", "nameEn"))}</a></td><td class="r num">${esc(num(s.median))}</td><td class="r num">${esc(pct(s.chance, 1))}</td><td class="r num">${esc(num(s.games))}</td></tr>`).join("")}
    </tbody></table>`;
  }

  function insights(list) {
    const items = (list || []).map((i) => `<div class="insight ${esc(i.tone || "info")}"><b>${esc(nm(i, "titleRu", "titleEn"))}</b>${cta()}</div>`).join("");
    return items || cta();
  }

  function tabs(defs, panels) {
    const buttons = defs.map((d, i) => `<button type="button" class="tab${i === 0 ? " on" : ""}" data-tab="${d.id}">${esc(t(d.key))}</button>`).join("");
    const body = defs.map((d, i) => `<div class="panel${i === 0 ? " on" : ""}" data-panel="${d.id}">${panels[d.id] || ""}</div>`).join("");
    return `<div data-tabs><div class="tabs">${buttons}</div>${body}</div>`;
  }

  function findGame(id) {
    const s = sample();
    const all = [...(s.games || [])];
    Object.values(s.tops || {}).forEach((list) => (list || []).forEach((g) => all.push(g)));
    return all.find((g) => String(g.id) === String(id)) || all[0] || null;
  }

  function findSegment(key) {
    const s = sample();
    const bags = Object.values(s.segments || {});
    const all = bags.flat().concat(s.developer?.niches || []);
    return all.find((row) => row.key === key) || null;
  }

  function overview() {
    const o = sample().overview;
    const kpis = o ? `<div class="grid kpis">
      ${kpi(t("kpi.catalog"), num(o.catalogTotal), `${t("kpi.known")} ${num(o.known)}`)}
      ${kpi(t("kpi.zero"), pct(o.zeroShare), t("kpi.zeroHint"), "var(--amber)")}
      ${kpi(t("kpi.median"), num(o.median), t("kpi.medianHint"))}
      ${kpi(t("kpi.p90"), `${num(o.p90)}+`, t("kpi.p90Hint"))}
      ${kpi(t("kpi.success").replace("{0}", o.successThreshold || 10), pct(o.successShare, 1), o.successLow == null ? "" : `${pct(o.successLow, 1)}–${pct(o.successHigh, 1)}`, "var(--green)")}
      ${kpi(t("kpi.stars"), o.stars == null ? "—" : num(o.stars, 2) + "★", t("kpi.starsHint"))}
    </div>` : "";
    const dist = (o && o.distribution || []).map((d) => ({ label: d.label, value: d.value }));
    const years = (o && o.years || []).map((d) => ({ label: String(d.year), value: d.avg, digits: 1 }));
    const genres = (o && o.genres || []).map((d) => ({ label: nm(d, "nameRu", "nameEn"), value: d.median, digits: 0 }));
    return `${emptyBanner()}<h1>${esc(t("page.overview"))}</h1><p class="lead">${esc(t("lead.overview"))}</p>${kpis}
      <div class="grid split">
        <section class="card"><h2>${esc(t("section.insights"))}</h2>${insights(o && o.insights)}<a href="developer.html">${esc(t("more.dev"))} →</a></section>
        <div>
          <section class="card" style="margin-bottom:12px"><h2>${esc(t("section.distribution"))}</h2>${bars(dist, "label", "value", "var(--accent)")}</section>
          <section class="card"><h2>${esc(t("section.years"))}</h2>${bars(years, "label", "value")}</section>
        </div>
      </div>
      <div class="grid half" style="margin-top:12px">
        <section class="card"><h2>${esc(t("section.genres"))}</h2>${cta()}${bars(genres, "label", "value")}</section>
        <section class="card"><h2>${esc(t("section.niches"))}</h2>${cta()}${(o && o.niches || []).map((s) => `<a class="row-link" href="tag.html?key=${encodeURIComponent(s.key)}"><span>${esc(nm(s, "nameRu", "nameEn"))}</span><span class="num">${esc(t("col.median"))} ${esc(num(s.median))}</span></a>`).join("") || ""}<p><a href="tops.html">${esc(t("more.tops"))} →</a></p><h2>${esc(t("section.noted"))}</h2>${(o && o.games || []).map((g) => `<a class="row-link" href="game.html?id=${encodeURIComponent(g.id)}"><span>${esc(g.title)}</span><span class="num">${esc(num(g.ratings))}</span></a>`).join("")}</section>
      </div>`;
  }

  function games() {
    return `${emptyBanner()}<h1>${esc(t("page.games"))}</h1><p class="lead">${esc(t("lead.games"))}</p><section class="card">${gameRows(sample().games)}</section>`;
  }

  function game() {
    const id = new URLSearchParams(location.search).get("id");
    const g = findGame(id);
    if (!g) return `${emptyBanner()}<h1>${esc(t("page.game"))}</h1>${cta()}`;
    return `${emptyBanner()}<h1>${esc(g.title)}</h1><p class="lead">${esc(nm(g, "genreRu", "genreEn"))} · ${esc(g.year || "—")} · ${esc(nm(g, "priceRu", "priceEn"))}</p>
      <div class="grid half">
        <section class="card"><table class="kv">
          <tr><td>${esc(t("col.ratings"))}</td><td class="num">${esc(num(g.ratings))}</td></tr>
          <tr><td>${esc(t("col.stars"))}</td><td>${g.stars == null ? "—" : esc(num(g.stars, 2))}</td></tr>
          <tr><td>${esc(t("col.platforms"))}</td><td>${esc(g.platforms || "—")}</td></tr>
          <tr><td>${esc(t("row.author"))}</td><td>${g.authorId ? `<a href="author.html?id=${encodeURIComponent(g.authorId)}">${esc(g.author || "—")}</a>` : esc(g.author || "—")}</td></tr>
        </table><p><a class="btn" href="${esc(g.url || "https://itch.io")}" target="_blank" rel="noreferrer">${esc(t("open.itch"))}</a></p></section>
        <section class="card"><h2>${esc(t("detail.history"))}</h2>${cta()}<h2>${esc(t("detail.similar"))}</h2>${cta()}</section>
      </div>`;
  }

  function tops() {
    const tops = sample().tops || {};
    const defs = ["demand", "reviews", "hot", "rising", "featured", "comments"].map((id) => ({ id, key: "tops." + id }));
    const panels = {};
    defs.forEach((d) => { panels[d.id] = `${cta()}<section class="card">${gameRows(tops[d.id])}</section>`; });
    return `${emptyBanner()}<h1>${esc(t("page.tops"))}</h1><p class="lead">${esc(t("lead.tops"))}</p>${tabs(defs, panels)}`;
  }

  function tags() {
    const seg = sample().segments || {};
    const defs = ["genres", "tags", "engines", "formats", "languages"].map((id) => ({ id, key: "tags." + id }));
    const panels = {};
    defs.forEach((d) => { panels[d.id] = `${cta()}<section class="card">${segmentRows(seg[d.id])}</section>`; });
    return `${emptyBanner()}<h1>${esc(t("page.tags"))}</h1><p class="lead">${esc(t("lead.tags"))}</p>${tabs(defs, panels)}`;
  }

  function tag() {
    const key = new URLSearchParams(location.search).get("key");
    const s = findSegment(key);
    if (!s) return `${emptyBanner()}<h1>${esc(t("page.tag"))}</h1>${cta()}`;
    return `${emptyBanner()}<h1>${esc(nm(s, "nameRu", "nameEn"))}</h1>
      <div class="grid kpis">${kpi(t("col.median"), num(s.median))}${kpi(t("col.chance"), pct(s.chance, 1))}${kpi(t("col.games"), num(s.games))}</div>
      ${cta()}`;
  }

  function map(points) {
    const rows = points || [];
    const w = 640, h = 320, left = 36, right = 12, top = 12, bottom = 24;
    const xs = rows.map((p) => Math.log(1 + (p.games || 1)));
    const ys = rows.map((p) => Math.log(1 + (p.median || 0)));
    const minX = Math.min(...xs, 0), maxX = Math.max(...xs, 1);
    const minY = Math.min(...ys, 0), maxY = Math.max(...ys, 1);
    const sx = (v) => left + (v - minX) / (maxX - minX || 1) * (w - left - right);
    const sy = (v) => top + (1 - (v - minY) / (maxY - minY || 1)) * (h - top - bottom);
    const dots = rows.map((p) => `<circle cx="${sx(Math.log(1 + (p.games || 1))).toFixed(1)}" cy="${sy(Math.log(1 + (p.median || 0))).toFixed(1)}" r="5" fill="#5cc8fa"><title>${esc(nm(p, "nameRu", "nameEn"))}</title></circle>`).join("");
    return `<div class="map-wrap"><svg viewBox="0 0 ${w} ${h}" role="img">
      <rect x="${left}" y="${top}" width="${(w - left - right) / 2}" height="${(h - top - bottom) / 2}" fill="rgba(92,200,250,0.08)"/>
      <rect x="${left + (w - left - right) / 2}" y="${top}" width="${(w - left - right) / 2}" height="${(h - top - bottom) / 2}" fill="rgba(255,107,107,0.06)"/>
      <rect x="${left}" y="${top + (h - top - bottom) / 2}" width="${(w - left - right) / 2}" height="${(h - top - bottom) / 2}" fill="rgba(76,195,138,0.06)"/>
      <rect x="${left + (w - left - right) / 2}" y="${top + (h - top - bottom) / 2}" width="${(w - left - right) / 2}" height="${(h - top - bottom) / 2}" fill="rgba(245,184,74,0.06)"/>
      ${dots}
    </svg><div class="map-cta">${cta()}</div></div>`;
  }

  function developer() {
    const d = sample().developer || {};
    const defs = [
      ["insights", "dev.insights"],
      ["map", "dev.map"],
      ["niches", "dev.niches"],
      ["formats", "dev.formats"],
      ["combos", "dev.combos"],
      ["drivers", "dev.drivers"],
      ["concept", "dev.concept"],
    ].map(([id, key]) => ({ id, key }));
    const how = `<table class="kv">
      <tr><td>${esc(t("how.median"))}</td><td>${esc(t("how.medianText"))}</td></tr>
      <tr><td>${esc(t("how.chance"))}</td><td>${esc(t("how.chanceText"))}</td></tr>
      <tr><td>${esc(t("how.competition"))}</td><td>${esc(t("how.competitionText"))}</td></tr>
      <tr><td>${esc(t("how.opportunity"))}</td><td>${esc(t("how.opportunityText"))}</td></tr>
    </table>`;
    const panels = {
      insights: `<div class="grid split"><section class="card"><h2>${esc(t("section.insights"))}</h2>${insights(d.insights)}</section><div><section class="card" style="margin-bottom:12px"><h2>${esc(t("section.drivers"))}</h2>${cta()}</section><section class="card"><h2>${esc(t("section.how"))}</h2>${how}</section></div></div>`,
      map: `<section class="card"><h2>${esc(t("map.caption"))}</h2>${map(d.map)}<div class="grid quads" style="margin-top:12px">
        ${["blue", "red", "quiet", "trap"].map((q) => `<article class="card"><h2>${esc(t("quad." + q))}</h2><p class="lead">${esc(t("quad." + q + "Hint"))}</p>${q === "blue" ? cta() : (d.map || []).filter((p) => p.quadrant === q).slice(0, 4).map((p) => `<div class="row-link"><span>${esc(nm(p, "nameRu", "nameEn"))}</span><span class="num">${esc(num(p.median))}</span></div>`).join("") || "—"}</article>`).join("")}
      </div></section>`,
      niches: `${cta()}<section class="card">${segmentRows(d.niches)}</section>`,
      formats: `${cta()}${(d.formatGroups || []).map((g) => `<section class="card" style="margin-bottom:12px"><h2>${esc(nm(g, "titleRu", "titleEn"))}</h2>${segmentRows(g.items)}</section>`).join("")}`,
      combos: `${cta()}<section class="card"><table><thead><tr><th>${esc(t("col.segment"))}</th><th class="r">${esc(t("col.median"))}</th></tr></thead><tbody>${(d.combos || []).map((c) => `<tr><td>${esc(nm(c, "aRu", "aEn"))} + ${esc(nm(c, "bRu", "bEn"))}</td><td class="r num">${esc(num(c.median))}</td></tr>`).join("")}</tbody></table></section>`,
      drivers: `${cta()}<section class="card"><table><thead><tr><th>${esc(t("col.segment"))}</th><th>${esc(t("col.group"))}</th></tr></thead><tbody>${(d.drivers || []).map((r) => `<tr><td>${esc(nm(r, "nameRu", "nameEn"))}</td><td>${esc(nm(r, "groupRu", "groupEn"))}</td></tr>`).join("")}</tbody></table></section>`,
      concept: `<section class="card"><div class="form-row"><label>${esc(t("concept.genre"))}<br/><input class="fake-input" placeholder="${esc(t("concept.placeholder"))}" /></label><label>${esc(t("concept.tags"))}<br/><input class="fake-input" placeholder="${esc(t("concept.placeholder"))}" /></label></div><p class="lead">${esc(t("disabled"))}</p>${cta()}</section>`,
    };
    return `${emptyBanner()}<h1>${esc(t("page.developer"))}</h1><p class="lead">${esc(t("lead.developer"))}</p>${tabs(defs, panels)}`;
  }

  function developers() {
    const d = sample().developers || {};
    const k = d.kpis;
    const cards = k ? `<div class="grid kpis">${kpi(t("kpi.devCount"), num(k.count), `${num(k.withFollowers)}`)}${kpi(t("kpi.single"), pct(k.singleShare))}${kpi(t("kpi.top1"), pct(k.top1Share), t("kpi.top1Hint"))}${kpi(t("kpi.followers"), num(k.medianFollowers))}</div>` : "";
    const rows = (d.rows || []).map((r) => `<tr><td><a href="author.html?id=${encodeURIComponent(r.id)}">${esc(r.name)}</a></td><td class="r num">${esc(num(r.followers))}</td><td class="r num">${esc(num(r.games))}</td><td class="r num">${esc(num(r.median))}</td><td class="r num">${esc(num(r.sum))}</td></tr>`).join("");
    return `${emptyBanner()}<h1>${esc(t("page.developers"))}</h1><p class="lead">${esc(t("lead.developers"))}</p>${cards}${cta()}<section class="card"><table><thead><tr><th>${esc(t("row.author"))}</th><th class="r">${esc(t("col.followers"))}</th><th class="r">${esc(t("col.authorGames"))}</th><th class="r">${esc(t("col.median"))}</th><th class="r">${esc(t("col.sum"))}</th></tr></thead><tbody>${rows}</tbody></table></section>`;
  }

  function author() {
    const id = new URLSearchParams(location.search).get("id");
    const rows = (sample().developers && sample().developers.rows) || [];
    const row = rows.find((r) => String(r.id) === String(id)) || rows[0];
    if (!row) return `${emptyBanner()}<h1>${esc(t("page.author"))}</h1>${cta()}`;
    return `${emptyBanner()}<h1>${esc(row.name)}</h1><div class="grid kpis">${kpi(t("col.followers"), num(row.followers))}${kpi(t("col.authorGames"), num(row.games))}${kpi(t("col.median"), num(row.median))}</div>${cta()}`;
  }

  function jams() {
    const d = sample().jams || {};
    const k = d.kpis;
    const cards = k ? `<div class="grid kpis">${kpi(t("kpi.jamCount"), num(k.count))}${kpi(t("kpi.jamActive"), num(k.active), "", "var(--green)")}${kpi(t("kpi.jamMedian"), num(k.medianSubmissions))}${kpi(t("kpi.jamFinish"), pct(k.conversion))}</div>` : "";
    const rows = (d.rows || []).map((r) => `<tr><td><a href="jam.html?id=${encodeURIComponent(r.id)}">${esc(r.title)}</a></td><td class="r num">${esc(num(r.submissions))}</td><td class="r num">${esc(num(r.joined))}</td><td class="r num">${esc(pct(r.conversion))}</td></tr>`).join("");
    return `${emptyBanner()}<h1>${esc(t("page.jams"))}</h1><p class="lead">${esc(t("lead.jams"))}</p>${cards}${cta()}<section class="card"><table><thead><tr><th>${esc(t("page.jam"))}</th><th class="r">${esc(t("col.submissions"))}</th><th class="r">${esc(t("col.joined"))}</th><th class="r">${esc(t("col.finish"))}</th></tr></thead><tbody>${rows}</tbody></table></section>`;
  }

  function jam() {
    const id = new URLSearchParams(location.search).get("id");
    const rows = (sample().jams && sample().jams.rows) || [];
    const row = rows.find((r) => String(r.id) === String(id)) || rows[0];
    if (!row) return `${emptyBanner()}<h1>${esc(t("page.jam"))}</h1>${cta()}`;
    return `${emptyBanner()}<h1>${esc(row.title)}</h1><div class="grid kpis">${kpi(t("col.submissions"), num(row.submissions))}${kpi(t("col.joined"), num(row.joined))}${kpi(t("col.finish"), pct(row.conversion))}</div>${cta()}`;
  }

  function mygame() {
    return `${emptyBanner()}<h1>${esc(t("page.mygame"))}</h1><p class="lead">${esc(t("lead.mygame"))}</p>
      <div class="grid half"><section class="card"><h2>${esc(t("mygame.key"))}</h2><p class="lead">${esc(t("mygame.keyHint"))}</p><input class="fake-input" disabled placeholder="••••" /><p><button class="btn ghost" type="button" disabled>${esc(t("data.start"))}</button></p>${cta()}</section>
      <section class="card"><h2>${esc(t("mygame.track"))}</h2>${cta()}</section></div>`;
  }

  function data() {
    const cards = ["quick", "daily", "deep"].map((id) => `<section class="card"><h2>${esc(t("data." + id))}</h2><p class="lead">${esc(t("data." + id + "Text"))}</p><button class="btn ghost" type="button" disabled>${esc(t("disabled"))}</button></section>`).join("");
    return `${emptyBanner()}<h1>${esc(t("page.data"))}</h1><p class="lead">${esc(t("lead.data"))}</p><div class="grid cards3">${cards}</div>${cta()}`;
  }

  const views = { overview, games, game, tops, tags, tag, developer, developers, author, jams, jam, mygame, data };

  function render(page) {
    return (views[page] || overview)();
  }

  document.addEventListener("DOMContentLoaded", () => window.Shell.mount());
  return { render };
})();
