// Soft spots that drift, push away from the cursor and swell with the beat. One look per theme.
export function createFx(canvas, getCenterEl) {
  const x = canvas.getContext("2d");
  const root = document.documentElement;
  let W, H, cfg, P = [], t = 0, en = 0.25, target = 0.25, col = "255,255,255", mx = -9999, my = -9999, fx = -9999, fy = -9999;
  const still = matchMedia("(prefers-reduced-motion: reduce)").matches;
  const CFG = {
    1: { n: 8, min: 160, max: 360, a: 0.17 }, 2: { n: 7, min: 180, max: 400, a: 0.11 }, 3: { n: 11, min: 50, max: 150, a: 0.95, hard: 1 },
    4: { n: 9, min: 140, max: 320, a: 0.15 }, 5: { n: 26, min: 24, max: 90, a: 0.24 }, 6: { n: 9, min: 120, max: 280, a: 0.18, stretch: 1 },
    7: { n: 42, min: 10, max: 46, a: 0.38 }, 8: { n: 7, min: 120, max: 300, a: 0.5, ring: 1 }, 9: { n: 9, min: 140, max: 300, a: 0.15, orbit: 1 },
    10: { n: 9, min: 200, max: 420, a: 0.24 },
  };
  cfg = CFG[+root.dataset.v] || CFG[10];
  const rnd = (a, b) => a + Math.random() * (b - a);
  function size() {
    const dpr = Math.min(2, devicePixelRatio || 1);
    W = innerWidth; H = innerHeight; canvas.width = W * dpr; canvas.height = H * dpr; x.setTransform(dpr, 0, 0, dpr, 0, 0);
    if (cfg) seed();
  }
  function seed() {
    const k = Math.max(0.55, Math.min(1.2, W / 1300)); P = [];
    for (let i = 0; i < cfg.n; i++) {
      const hx = rnd(0, W), hy = rnd(0, H);
      P.push({ hx, hy, x: hx, y: hy, vx: 0, vy: 0, r: rnd(cfg.min, cfg.max) * k, ph: rnd(0, 6.283), sp: rnd(0.15, 0.45), amp: rnd(30, 120), ang: rnd(0, 6.283), rad: rnd(120, Math.max(W, H) * 0.55) });
    }
  }
  function setTheme(v) { cfg = CFG[v] || CFG[10]; col = getComputedStyle(root).getPropertyValue("--p").trim() || "255,255,255"; seed(); if (still) draw(0); }
  addEventListener("pointermove", (e) => { mx = e.clientX; my = e.clientY; });
  document.addEventListener("pointerleave", () => { mx = my = -9999; });
  addEventListener("resize", size);
  const rgba = (a) => `rgba(${col},${a})`;
  function blob(px, py, r, a, sx = 1) {
    if (cfg.hard) { x.fillStyle = rgba(a); x.beginPath(); x.ellipse(px, py, r * 0.5 * sx, r * 0.5, 0, 0, 6.283); x.fill(); return; }
    if (cfg.ring) { x.strokeStyle = rgba(a * 0.35); x.lineWidth = 1.5; x.beginPath(); x.ellipse(px, py, r * 0.5 * sx, r * 0.5, 0, 0, 6.283); x.stroke(); x.fillStyle = rgba(a * 0.08); x.fill(); return; }
    x.save(); x.translate(px, py); x.scale(sx, 1);
    const g = x.createRadialGradient(0, 0, 0, 0, 0, r);
    g.addColorStop(0, rgba(a)); g.addColorStop(0.45, rgba(a * 0.45)); g.addColorStop(1, rgba(0));
    x.fillStyle = g; x.beginPath(); x.arc(0, 0, r, 0, 6.283); x.fill(); x.restore();
  }
  function draw(dt) {
    t += dt; en += (target - en) * 0.04;
    const beat = en * (0.6 + 0.4 * Math.abs(Math.sin(t * 2.2)));
    x.clearRect(0, 0, W, H);
    let ocx = W / 2, ocy = H * 0.4;
    const el = cfg.orbit && getCenterEl();
    if (el) { const w = el.getBoundingClientRect(); ocx = w.left + w.width / 2; ocy = w.top + w.height / 2; }
    for (const p of P) {
      let tx, ty;
      if (cfg.orbit) { p.ang += dt * 0.08 * (1 + beat * 2); tx = ocx + Math.cos(p.ang) * p.rad; ty = ocy + Math.sin(p.ang) * p.rad * 0.45; }
      else { tx = p.hx + Math.sin(t * p.sp + p.ph) * p.amp; ty = p.hy + Math.cos(t * p.sp * 0.8 + p.ph) * p.amp; }
      const dx = p.x - mx, dy = p.y - my, d = Math.hypot(dx, dy), R = p.r * 1.1 + 140;
      if (d < R && d > 0) { const f = (1 - d / R) * 9; p.vx += (dx / d) * f; p.vy += (dy / d) * f; }
      p.vx += (tx - p.x) * 0.012; p.vy += (ty - p.y) * 0.012; p.vx *= 0.88; p.vy *= 0.88; p.x += p.vx; p.y += p.vy;
      const sw = 1 + beat * 0.28 * Math.sin(t * 3 + p.ph);
      blob(p.x, p.y, p.r * sw, cfg.a * (0.75 + beat * 0.5), cfg.stretch ? 1.6 + beat * 0.8 : 1);
    }
    if (mx > -999) { fx += (mx - fx) * 0.08; fy += (my - fy) * 0.08; if (!cfg.hard && !cfg.ring) blob(fx, fy, 180, cfg.a * 0.9); }
  }
  let lt = performance.now();
  function loop(now) { const dt = Math.min(0.05, (now - lt) / 1000); lt = now; draw(dt); requestAnimationFrame(loop); }
  size();
  if (!still) requestAnimationFrame(loop);
  return { setTheme, energy: (v) => { target = v ? 1 : 0.25; if (still) draw(0); } };
}
