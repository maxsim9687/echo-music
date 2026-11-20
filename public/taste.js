// Song facts for the wave's filters: genre (from Apple Music's catalog, looked up once by the server: /api/meta),
// language and year. The taste model itself lives on the server (recsys.py), one per listener.

export const GENRES = ["Pop", "Hip-Hop/Rap", "Rock", "Alternative", "Electronic", "R&B/Soul", "Latin", "K-Pop", "Country",
  "Metal", "Phonk", "Folk", "Jazz", "Classical", "Soundtrack", "Other"];
export const GENRE_RU = {
  Pop: "Поп", "Hip-Hop/Rap": "Хип-хоп и рэп", Rock: "Рок", Alternative: "Альтернатива", Electronic: "Электроника",
  "R&B/Soul": "R&B и соул", Latin: "Латино", "K-Pop": "K-pop", Country: "Кантри", Metal: "Метал", Phonk: "Фонк",
  Folk: "Фолк и авторская", Jazz: "Джаз", Classical: "Классика", Soundtrack: "Саундтреки", Other: "Другое",
};

const MAP = [
  [/phonk/i, "Phonk"],
  [/k-?pop|j-?pop|korean|japan|anime/i, "K-Pop"],
  [/latin|latino|reggaet|urbano|mexic|sertanejo|brazil|mpb|bachata|salsa|cumbia|corrido|banda|pagode|forró|samba|regional/i, "Latin"],
  [/hip.?hop|rap|trap|drill|grime|хип|рэп/i, "Hip-Hop/Rap"],
  [/metal|метал/i, "Metal"],
  [/alternative|indie|альтерн|инди|emo|punk/i, "Alternative"],
  [/rock|рок|grunge/i, "Rock"],
  [/r&b|soul|funk|disco|gospel|соул/i, "R&B/Soul"],
  [/dance|electr|house|techno|trance|edm|dubstep|drum|bass|lo-?fi|ambient|synth|downtempo|hardstyle|танц|электр/i, "Electronic"],
  [/country|americana|кантри/i, "Country"],
  [/folk|singer|songwriter|acoustic|bard|шансон|авторск|фолк/i, "Folk"],
  [/jazz|blues|джаз|блюз/i, "Jazz"],
  [/classical|orchestr|opera|piano|классик/i, "Classical"],
  [/soundtrack|film|tv|game|score|саундтр/i, "Soundtrack"],
  [/pop|поп|estrada|эстрад|world|reggae/i, "Pop"],
];
export function normGenre(g) {
  if (!g) return "";
  for (const [re, out] of MAP) if (re.test(g)) return out;
  return "Other";
}

// ---- song metadata from the server ----
let META = {};
let metaLoaded = false;
export const setMeta = (m) => { META = m || {}; metaLoaded = true; };
const nk = (s) => (s || "").toLowerCase().replace(/\(.*?\)|\[.*?\]/g, " ").replace(/feat\.?.*$|ft\..*$/i, " ").replace(/[^a-z0-9а-яё]+/gi, " ").trim();
export const metaKey = (t) => nk((t.artist || "").split(",")[0]) + "|" + nk(t.title);

export function langOf(t) {
  const s = `${t.title} ${t.artist}`;
  if (/[әғқңөұүһ]/i.test(s)) return "other"; // Kazakh, not Russian
  if (/[а-яё]/i.test(s)) return "ru";
  if (/[가-힣ㄱ-ㅎ]|[ぁ-んァ-ン一-龯]/.test(s)) return "ko";
  if (/[ãõ]|\b(você|não|saudade|coração)\b/i.test(s)) return "pt";
  if (/[ñ¿¡]|\b(el|la|los|las|mi|tu|que|amor|corazón|te quiero|baila|noche|vida|quiero|ella|señorita)\b/i.test(s) || (t.tags || []).includes("latin")) return "es";
  if (/^[\x00-\x7F’'“”…–—]*$/.test(s)) return "en";
  return "other";
}

// genre, year and explicit for one track: Apple metadata first, then the source's own genre, then tags
export function infoOf(t) {
  if (t._info) return t._info;
  const m = META[metaKey(t)];
  let genre = m ? normGenre(m[0]) : normGenre(t.genre);
  const text = `${t.title} ${(t.tags || []).join(" ")}`;
  if (/phonk/i.test(text)) genre = "Phonk";
  const year = m?.[1] || +(t.releaseDate || "").slice(0, 4) || 0;
  const info = { genre: genre || "", year, explicit: !!m?.[2] || /explicit/i.test(t.title || ""), lang: langOf(t), known: !!m };
  // remember the answer only once the genre list has arrived, so early guesses are not kept forever
  if (metaLoaded) Object.defineProperty(t, "_info", { value: info, enumerable: false, configurable: true });
  return info;
}

// era bucket for the «Эпоха» filter
export const eraOf = (y) => (!y ? "" : y < 2000 ? "classic" : y < 2010 ? "2000s" : y < 2023 ? "2010s" : "new");
