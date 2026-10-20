const Fmt = (() => {
    const ru = new Intl.NumberFormat("ru-RU", { maximumFractionDigits: 0 });
    function group(n, digits, lang) {
        const v = Number(n);
        if (!Number.isFinite(v)) return "—";
        return new Intl.NumberFormat(lang === "en" ? "en-US" : "ru-RU", {
            minimumFractionDigits: digits,
            maximumFractionDigits: digits,
        }).format(v);
    }
    function n(v, digits, lang) {
        if (v === null || v === undefined || v === "") return "—";
        if (typeof v === "number" && !Number.isFinite(v)) return "—";
        return group(v, digits ?? 0, lang);
    }
    function short(v, lang) {
        if (v === null || v === undefined || !Number.isFinite(Number(v))) return "—";
        const x = Number(v);
        const loc = lang === "en" ? "en-US" : "ru-RU";
        const abs = Math.abs(x);
        if (lang === "en") {
            if (abs >= 1_000_000) return new Intl.NumberFormat(loc, { maximumFractionDigits: 1 }).format(x / 1_000_000) + "M";
            if (abs >= 10_000) return new Intl.NumberFormat(loc, { maximumFractionDigits: 0 }).format(x / 1_000) + "k";
            if (abs >= 1_000) return new Intl.NumberFormat(loc, { maximumFractionDigits: 1 }).format(x / 1_000) + "k";
        } else {
            if (abs >= 1_000_000) return new Intl.NumberFormat(loc, { maximumFractionDigits: 1 }).format(x / 1_000_000) + " млн";
            if (abs >= 10_000) return new Intl.NumberFormat(loc, { maximumFractionDigits: 0 }).format(x / 1_000) + " тыс.";
            if (abs >= 1_000) return new Intl.NumberFormat(loc, { maximumFractionDigits: 1 }).format(x / 1_000) + " тыс.";
        }
        return new Intl.NumberFormat(loc, { maximumFractionDigits: x % 1 === 0 ? 0 : 1 }).format(x);
    }
    function pct(share, digits, lang) {
        if (share === null || share === undefined || !Number.isFinite(Number(share))) return "—";
        return n(Number(share) * 100, digits ?? 0, lang) + "%";
    }
    function signed(v, digits, suffix, lang) {
        if (v === null || v === undefined || !Number.isFinite(Number(v))) return "—";
        const x = Number(v);
        return (x > 0 ? "+" : "") + n(x, digits ?? 0, lang) + (suffix || "");
    }
    function times(v, lang) {
        if (v === null || v === undefined || !Number.isFinite(Number(v))) return "—";
        const x = Number(v);
        const digits = x >= 10 ? 0 : 1;
        let s = n(x, digits, lang);
        if (x < 10) {
            const raw = x.toFixed(2);
            if (raw.endsWith("0")) s = n(x, 1, lang);
        }
        return "×" + s;
    }
    function stars(v, lang) {
        if (v === null || v === undefined || !Number.isFinite(Number(v))) return "—";
        return n(v, 2, lang) + "★";
    }
    function plural(n0, one, few, many) {
        const n = Math.abs(n0) % 100;
        if (n >= 11 && n <= 19) return many;
        const d = n % 10;
        if (d === 1) return one;
        if (d >= 2 && d <= 4) return few;
        return many;
    }
    return { n, short, pct, signed, times, stars, plural, ru };
})();
