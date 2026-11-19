// Downloads in the best quality the source gives: original file where possible, WAV decoded locally.
export function downloadOptions(t) {
  if (!t?.downloadable) return [];
  if (t.src === "audius") return [
    { label: "MP3", note: "320 kbps, как у автора", fmt: "mp3", url: t.stream },
    { label: "WAV", note: "без потерь из MP3 320", fmt: "wav", url: t.stream, convert: true },
  ];
  if (t.src === "jamendo") return [
    { label: "FLAC", note: "оригинал без потерь", fmt: "flac", url: t.dlOrig },
    { label: "WAV", note: "из FLAC, 24 бит", fmt: "wav", url: t.dlOrig, convert: true, bits: 24 },
    { label: "MP3", note: "для телефона", fmt: "mp3", url: t.dlMp3 },
  ];
  const ext = (t.ext || "mp3").toLowerCase();
  const out = [{ label: ext.toUpperCase(), note: "оригинал, как загружен", fmt: ext, url: t.dlOrig, direct: true }];
  if (ext !== "wav") out.push({ label: "WAV", note: `из ${ext.toUpperCase()}`, fmt: "wav", url: t.stream, convert: true, bits: ext === "mp3" ? 16 : 24 });
  return out;
}

const safe = (s) => s.replace(/[\\/:*?"<>|]+/g, "").trim().slice(0, 150) || "track";

function save(blob, name) {
  const a = document.createElement("a");
  a.href = URL.createObjectURL(blob); a.download = name;
  document.body.appendChild(a); a.click(); a.remove();
  setTimeout(() => URL.revokeObjectURL(a.href), 60e3);
}

async function fetchWithProgress(url, onProgress) {
  const r = await fetch(url);
  if (!r.ok) throw new Error(`HTTP ${r.status}`);
  const total = +r.headers.get("Content-Length") || 0;
  if (!r.body || !total) return new Uint8Array(await r.arrayBuffer());
  const reader = r.body.getReader(); const parts = []; let got = 0;
  for (;;) {
    const { done, value } = await reader.read();
    if (done) break;
    parts.push(value); got += value.length; onProgress(got / total);
  }
  const out = new Uint8Array(got); let o = 0;
  for (const p of parts) { out.set(p, o); o += p.length; }
  return out;
}

// Sample rate from the file header: FLAC STREAMINFO, WAV fmt chunk, or the first MP3 frame.
export function sampleRateOf(b) {
  const str = (o, n) => String.fromCharCode(...b.subarray(o, o + n));
  if (str(0, 4) === "fLaC") return (b[18] << 12) | (b[19] << 4) | (b[20] >> 4);
  if (str(0, 4) === "RIFF" && str(8, 4) === "WAVE") return b[24] | (b[25] << 8) | (b[26] << 16) | (b[27] << 24);
  let i = 0;
  if (str(0, 3) === "ID3") i = 10 + ((b[6] << 21) | (b[7] << 14) | (b[8] << 7) | b[9]);
  for (const end = Math.min(b.length - 4, i + 65536); i < end; i++) {
    if (b[i] !== 0xff || (b[i + 1] & 0xe0) !== 0xe0) continue;
    const ver = (b[i + 1] >> 3) & 3, idx = (b[i + 2] >> 2) & 3;
    if (ver === 1 || idx === 3) continue;
    const base = [44100, 48000, 32000][idx];
    return ver === 3 ? base : ver === 2 ? base / 2 : base / 4;
  }
  return 0;
}

export function encodeWav(buf, bits = 16) {
  const ch = buf.numberOfChannels, sr = buf.sampleRate, len = buf.length, bps = bits / 8;
  const data = len * ch * bps, ab = new ArrayBuffer(44 + data), v = new DataView(ab);
  const w = (o, s) => { for (let i = 0; i < s.length; i++) v.setUint8(o + i, s.charCodeAt(i)); };
  w(0, "RIFF"); v.setUint32(4, 36 + data, true); w(8, "WAVE"); w(12, "fmt "); v.setUint32(16, 16, true);
  v.setUint16(20, 1, true); v.setUint16(22, ch, true); v.setUint32(24, sr, true); v.setUint32(28, sr * ch * bps, true);
  v.setUint16(32, ch * bps, true); v.setUint16(34, bits, true); w(36, "data"); v.setUint32(40, data, true);
  const chans = Array.from({ length: ch }, (_, i) => buf.getChannelData(i));
  let o = 44;
  for (let i = 0; i < len; i++) for (let c = 0; c < ch; c++) {
    const s = Math.max(-1, Math.min(1, chans[c][i]));
    if (bits === 24) { const n = Math.round(s < 0 ? s * 0x800000 : s * 0x7fffff); v.setUint8(o, n & 255); v.setUint8(o + 1, (n >> 8) & 255); v.setUint8(o + 2, (n >> 16) & 255); o += 3; }
    else { v.setInt16(o, s < 0 ? s * 0x8000 : s * 0x7fff, true); o += 2; }
  }
  return new Blob([ab], { type: "audio/wav" });
}

export async function download(t, opt, onProgress = () => {}) {
  const name = `${safe(t.artist)} - ${safe(t.title)}.${opt.fmt}`;
  if (opt.direct) { const a = document.createElement("a"); a.href = opt.url; a.download = name; document.body.appendChild(a); a.click(); a.remove(); return; }
  let bytes;
  try { bytes = await fetchWithProgress(opt.url, (p) => onProgress(opt.convert ? p * 0.8 : p)); }
  catch (e) {
    if (!opt.convert) { window.open(opt.url, "_blank", "noopener"); return "opened"; }
    throw e;
  }
  if (!opt.convert) { save(new Blob([bytes]), name); return; }
  if (bytes.length > 220 * 1024 * 1024) throw new Error("too_big");
  // decode at the file's own sample rate, otherwise the browser resamples (usually to 48 kHz)
  const rate = sampleRateOf(bytes);
  let ctx;
  try { ctx = new (window.AudioContext || window.webkitAudioContext)(rate ? { sampleRate: rate } : undefined); }
  catch { ctx = new (window.AudioContext || window.webkitAudioContext)(); }
  try {
    const audio = await ctx.decodeAudioData(bytes.buffer);
    onProgress(0.9);
    save(encodeWav(audio, opt.bits || 16), name);
    onProgress(1);
  } finally { ctx.close(); }
}
