import React, { useEffect, useRef, useState } from 'react';
import Link from '@docusaurus/Link';
import Layout from '@theme/Layout';
import CodeBlock from '@theme/CodeBlock';
import styles from './index.module.css';
import getPageData from '../data';

// ─── Hero code sample (not translated — it's code) ────────────────────────────
const HERO_CODE = `public partial class EnemyAI : MonoBehaviour
{
    async VlkTask ChasePlayer()
    {
        while (true)
        {
            MoveTowards(player.position);
            await VlkTask.Yield(); // auto-cancels on Destroy
        }
    }
}

var (tex, sfx, data) = await VlkTask.WhenAll(
    LoadTextureAsync(),
    LoadAudioAsync(),
    FetchDataAsync());`;

// ─── Locale hook ──────────────────────────────────────────────────────────────
function usePageData() {
  const [data, setData] = useState(getPageData('en'));
  useEffect(() => {
    const locale = document.documentElement.lang || 'en';
    setData(getPageData(locale));
  }, []);
  return data;
}

// ─── Effects ──────────────────────────────────────────────────────────────────

function CursorGlow() {
  const ref = useRef(null);
  useEffect(() => {
    const el = ref.current;
    if (!el || window.matchMedia('(pointer:coarse)').matches) return;
    let rx = -9999, ry = -9999, cx = rx, cy = ry, raf;
    const lerp = (a, b, t) => a + (b - a) * t;
    const tick = () => { cx = lerp(cx, rx, 0.08); cy = lerp(cy, ry, 0.08); el.style.transform = `translate(${cx - 300}px,${cy - 300}px)`; raf = requestAnimationFrame(tick); };
    const onMove = (e) => { rx = e.clientX; ry = e.clientY; };
    window.addEventListener('mousemove', onMove, { passive: true });
    raf = requestAnimationFrame(tick);
    return () => { cancelAnimationFrame(raf); window.removeEventListener('mousemove', onMove); };
  }, []);
  return <div ref={ref} style={{ position:'fixed',top:0,left:0,width:600,height:600,borderRadius:'50%', background:'radial-gradient(circle,rgba(124,58,237,0.055) 0%,transparent 70%)', pointerEvents:'none',zIndex:9998,willChange:'transform' }} />;
}

function StarCanvas() {
  const canvasRef = useRef(null);
  const mouseRef  = useRef({ x: 0.5, y: 0.5 });
  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    const resize = () => { canvas.width = canvas.offsetWidth; canvas.height = canvas.offsetHeight; };
    resize();
    const ro = new ResizeObserver(resize);
    ro.observe(canvas);
    const stars = Array.from({ length: 90 }, () => ({ x: Math.random(), y: Math.random(), r: Math.random() * 1.3 + 0.3, vx: (Math.random() - 0.5) * 0.00012, vy: (Math.random() - 0.5) * 0.00012, phase: Math.random() * Math.PI * 2 }));
    let raf;
    const draw = () => {
      const { width: w, height: h } = canvas;
      ctx.clearRect(0, 0, w, h);
      const t = Date.now() * 0.001;
      const { x: mx, y: my } = mouseRef.current;
      stars.forEach(s => {
        s.x += s.vx + (mx - s.x) * 0.000035; s.y += s.vy + (my - s.y) * 0.000035;
        if (s.x < 0) s.x = 1; if (s.x > 1) s.x = 0; if (s.y < 0) s.y = 1; if (s.y > 1) s.y = 0;
        const opacity = 0.12 + 0.25 * Math.abs(Math.sin(t * 0.5 + s.phase));
        ctx.beginPath(); ctx.arc(s.x * w, s.y * h, s.r, 0, Math.PI * 2);
        ctx.fillStyle = `rgba(196,181,253,${opacity})`; ctx.fill();
      });
      raf = requestAnimationFrame(draw);
    };
    draw();
    const onMove = (e) => { const r = canvas.getBoundingClientRect(); mouseRef.current = { x: (e.clientX - r.left) / r.width, y: (e.clientY - r.top) / r.height }; };
    canvas.closest('section')?.addEventListener('mousemove', onMove, { passive: true });
    return () => { cancelAnimationFrame(raf); ro.disconnect(); canvas.closest('section')?.removeEventListener('mousemove', onMove); };
  }, []);
  return <canvas ref={canvasRef} style={{ position:'absolute',inset:0,width:'100%',height:'100%',pointerEvents:'none',zIndex:0 }} />;
}

function ScrambleText({ text }) {
  const CHARS = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz#$%@&';
  const [display, setDisplay] = useState(() => text.replace(/[^\s]/g, CHARS[0]));
  useEffect(() => {
    let frame = 0; const total = 28;
    const id = setInterval(() => {
      setDisplay(text.split('').map((ch, i) => { if (ch === ' ') return ' '; if (frame >= Math.ceil((i / text.length) * total)) return ch; return CHARS[Math.floor(Math.random() * CHARS.length)]; }).join(''));
      frame++; if (frame > total) clearInterval(id);
    }, 35);
    return () => clearInterval(id);
  }, [text]);
  return <>{display}</>;
}

function useMagnetic() {
  const ref = useRef(null);
  useEffect(() => {
    const el = ref.current;
    if (!el || window.matchMedia('(pointer:coarse)').matches) return;
    const onMove  = (e) => { const r = el.getBoundingClientRect(); el.style.transform = `translate(${(e.clientX - r.left - r.width/2) * 0.28}px,${(e.clientY - r.top - r.height/2) * 0.28}px)`; };
    const onLeave = () => { el.style.transform = ''; el.style.transition = 'transform 0.4s cubic-bezier(.25,.46,.45,.94)'; };
    const onEnter = () => { el.style.transition = 'transform 0.1s ease'; };
    el.addEventListener('mousemove', onMove); el.addEventListener('mouseleave', onLeave); el.addEventListener('mouseenter', onEnter);
    return () => { el.removeEventListener('mousemove', onMove); el.removeEventListener('mouseleave', onLeave); el.removeEventListener('mouseenter', onEnter); };
  }, []);
  return ref;
}

// ─── Hooks ────────────────────────────────────────────────────────────────────

function useInView(threshold = 0.12) {
  const ref = useRef(null);
  const [inView, setInView] = useState(false);
  useEffect(() => {
    const el = ref.current;
    if (!el) return;
    const obs = new IntersectionObserver(([e]) => { if (e.isIntersecting) { setInView(true); obs.disconnect(); } }, { threshold });
    obs.observe(el);
    return () => obs.disconnect();
  }, []);
  return [ref, inView];
}

function useCountUp(target, active) {
  const [val, setVal] = useState(0);
  useEffect(() => {
    if (!active) return;
    if (target === 0) { setVal(0); return; }
    const dur = 1200; const start = performance.now();
    const tick = (now) => { const t = Math.min((now - start) / dur, 1); setVal(Math.round((1 - Math.pow(1 - t, 3)) * target)); if (t < 1) requestAnimationFrame(tick); };
    requestAnimationFrame(tick);
  }, [active, target]);
  return val;
}

// ─── Primitives ───────────────────────────────────────────────────────────────

function FadeIn({ children, delay = 0, className, style }) {
  const [ref, inView] = useInView(0.08);
  return (
    <div ref={ref} className={className} style={{ opacity: inView ? 1 : 0, transform: inView ? 'translateY(0)' : 'translateY(20px)', transition: `opacity 0.5s ease ${delay}s, transform 0.5s ease ${delay}s`, ...style }}>
      {children}
    </div>
  );
}

function Tooltip({ text, children }) {
  return (
    <span className={styles.tipWrap}>
      {children}<span className={styles.tipIcon}>ⓘ</span><span className={styles.tip}>{text}</span>
    </span>
  );
}

// ─── Sections ─────────────────────────────────────────────────────────────────

function Hero({ data }) {
  const magPrimary = useMagnetic();
  const magGhost   = useMagnetic();
  const cardRef    = useRef(null);
  useEffect(() => {
    const el = cardRef.current;
    if (!el) return;
    const onMove = (e) => { const r = el.getBoundingClientRect(); el.style.setProperty('--mx', `${e.clientX - r.left}px`); el.style.setProperty('--my', `${e.clientY - r.top}px`); };
    el.addEventListener('mousemove', onMove, { passive: true });
    return () => el.removeEventListener('mousemove', onMove);
  }, []);
  return (
    <section className={styles.hero}>
      <StarCanvas />
      <div className={styles.aura1} /><div className={styles.aura2} /><div className={styles.aura3} />
      <div className={styles.dots} />
      <div className={`container ${styles.heroGrid}`}>
        <div className={styles.heroLeft}>
          <div className={styles.pill}><span className={styles.pillDot} />{data.hero.pill}</div>
          <h1 className={styles.h1}>{data.hero.h1}<br /><span className={styles.grad}><ScrambleText text={data.hero.h1Grad} /></span></h1>
          <p className={styles.heroP}>
            {data.hero.p1} {data.hero.p2} <em>{data.hero.em}</em>
          </p>
          <div className={styles.ctas}>
            <Link ref={magPrimary} className={styles.btnPrimary} to="/docs/installation">{data.hero.cta}</Link>
            <Link ref={magGhost}   className={styles.btnGhost}   href="https://github.com/unapartidamas/valkarn-tasks"><GHIcon />GitHub</Link>
          </div>
        </div>
        <div className={styles.heroRight}>
          <div ref={cardRef} className={styles.codeCard}>
            <div className={styles.chrome}>
              <span style={{background:'#ff5f57'}} /><span style={{background:'#febc2e'}} /><span style={{background:'#28c840'}} />
              <span className={styles.chromeLabel}>EnemyAI.cs</span>
            </div>
            <CodeBlock language="csharp" showLineNumbers>{HERO_CODE}</CodeBlock>
          </div>
        </div>
      </div>
    </section>
  );
}

function StatItem({ s, inView }) {
  const val = useCountUp(s.end, inView);
  return (
    <div className={styles.stat}>
      <span className={styles.statN}>{val}{s.suffix}</span>
      <span className={styles.statL}>{s.label}</span>
    </div>
  );
}

function Stats({ data }) {
  const [ref, inView] = useInView(0.3);
  return (
    <div ref={ref} className={styles.statsRow}>
      {data.stats.map((s) => <StatItem key={s.label} s={s} inView={inView} />)}
    </div>
  );
}

function Features({ data }) {
  return (
    <section className={styles.sec}>
      <div className="container">
        <FadeIn>
          <h2 className={styles.h2}>{data.featuresSection.heading}</h2>
          <p className={styles.secSub}>{data.featuresSection.sub}</p>
        </FadeIn>
        <div className={styles.featGrid}>
          {data.features.map((f, i) => (
            <FadeIn key={f.title} delay={i * 0.04}>
              <div className={styles.featCard}>
                <span className={styles.featIcon}>{f.icon}</span>
                <strong className={styles.featTitle}>{f.title}</strong>
                <p className={styles.featDesc}>{f.desc}</p>
              </div>
            </FadeIn>
          ))}
        </div>
      </div>
    </section>
  );
}

function Comparison({ data }) {
  const [ref, inView] = useInView(0.04);
  const COLS  = ['task', 'unitask', 'awaitable', 'valkarn'];
  const HEADS = { task: 'System.Task', unitask: 'UniTask', awaitable: 'Awaitable', valkarn: 'Valkarn Tasks' };
  return (
    <section className={styles.sec2}>
      <div className="container">
        <FadeIn>
          <h2 className={styles.h2}>{data.comparisonSection.heading}</h2>
          <p className={styles.secSub}>{data.comparisonSection.sub}</p>
        </FadeIn>
        <div ref={ref} style={{ overflowX:'auto', opacity: inView ? 1 : 0, transform: inView ? 'none' : 'translateY(24px)', transition:'opacity .6s ease .1s, transform .6s ease .1s' }}>
          <table className={styles.cmpTable}>
            <colgroup>
              <col style={{width:'30%'}} /><col style={{width:'17.5%'}} /><col style={{width:'17.5%'}} /><col style={{width:'17.5%'}} /><col style={{width:'17.5%'}} />
            </colgroup>
            <thead>
              <tr>
                <th className={styles.thF}>{data.comparisonSection.featureCol}</th>
                {COLS.map(c => <th key={c} className={c === 'valkarn' ? styles.thV : styles.thC}>{HEADS[c]}</th>)}
              </tr>
            </thead>
            <tbody>
              {data.rows.map((row) => (
                <tr key={row.feature} className={styles.cmpRow}>
                  <td className={styles.tdF}>
                    <span className={styles.tdFMain}>{row.feature}</span>
                    {row.sub && <span className={styles.tdFSub}>{row.sub}</span>}
                  </td>
                  {COLS.map(c => {
                    const isWin = row.win?.includes(c);
                    const note  = row.note?.[c];
                    const isV   = c === 'valkarn';
                    return (
                      <td key={c} className={[styles.tdC, isWin ? styles.tdWin : '', isV ? styles.tdV : ''].join(' ')}>
                        {note ? <Tooltip text={note}>{row.cols[c]}</Tooltip> : row.cols[c]}
                      </td>
                    );
                  })}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </section>
  );
}

function Versus({ data }) {
  return (
    <section className={styles.sec}>
      <div className="container">
        <FadeIn>
          <h2 className={styles.h2}>{data.versusSection.heading}</h2>
          <p className={styles.secSub}>{data.versusSection.sub}</p>
        </FadeIn>
        <div className={styles.versusGrid}>
          {data.versus.map((v, i) => (
            <FadeIn key={v.vs} delay={i * 0.08}>
              <div className={styles.versusCard}>
                <div className={styles.versusHead}>
                  <span className={styles.versusVs}>vs</span>
                  <span className={styles.versusName} style={{ color: v.color }}>{v.vs}</span>
                </div>
                <ul className={styles.versusList}>
                  {v.problems.map(p => (
                    <li key={p.title} className={styles.versusItem}>
                      <span className={styles.versusItemTitle}>{p.title}</span>
                      <span className={styles.versusItemBody}>{p.body}</span>
                    </li>
                  ))}
                </ul>
                <div className={styles.versusVerdict}>{v.verdict}</div>
              </div>
            </FadeIn>
          ))}
        </div>
      </div>
    </section>
  );
}

function Cta({ data }) {
  const magPrimary = useMagnetic();
  const magGhost   = useMagnetic();
  return (
    <section className={styles.ctaSec}>
      <div className={styles.ctaBlob} />
      <div className="container" style={{ position:'relative', textAlign:'center' }}>
        <FadeIn>
          <h2 className={styles.ctaH}>{data.cta.heading}</h2>
          <p className={styles.ctaP}>{data.cta.p1}<br />{data.cta.p2}</p>
          <div className={styles.ctaBtns}>
            <Link ref={magPrimary} className={styles.btnPrimary} to="/docs/installation">{data.cta.btnPrimary}</Link>
            <Link ref={magGhost}   className={styles.btnGhost}   to="/docs/license">{data.cta.btnGhost}</Link>
          </div>
        </FadeIn>
      </div>
    </section>
  );
}

function GHIcon() {
  return <svg width="15" height="15" viewBox="0 0 24 24" fill="currentColor" style={{marginRight:7,verticalAlign:'text-bottom',flexShrink:0}}><path d="M12 0C5.37 0 0 5.37 0 12c0 5.31 3.435 9.795 8.205 11.385.6.105.825-.255.825-.57 0-.285-.015-1.23-.015-2.235-3.015.555-3.795-.735-4.035-1.41-.135-.345-.72-1.41-1.23-1.695-.42-.225-1.02-.78-.015-.795.945-.015 1.62.87 1.845 1.23 1.08 1.815 2.805 1.305 3.495.99.105-.78.42-1.305.765-1.605-2.67-.3-5.46-1.335-5.46-5.925 0-1.305.465-2.385 1.23-3.225-.12-.3-.54-1.53.12-3.18 0 0 1.005-.315 3.3 1.23.96-.27 1.98-.405 3-.405s2.04.135 3 .405c2.295-1.56 3.3-1.23 3.3-1.23.66 1.65.24 2.88.12 3.18.765.84 1.23 1.905 1.23 3.225 0 4.605-2.805 5.625-5.475 5.925.435.375.81 1.095.81 2.22 0 1.605-.015 2.895-.015 3.3 0 .315.225.69.825.57A12.02 12.02 0 0 0 24 12c0-6.63-5.37-12-12-12z"/></svg>;
}

// ─── Page ─────────────────────────────────────────────────────────────────────

export default function Home() {
  const data = usePageData();
  return (
    <Layout title="Zero-allocation async/await for Unity" description="Struct-based async/await for Unity. Zero allocation, source-generated lifecycle cancellation, Burst & ECS ready.">
      <CursorGlow />
      <Hero       data={data} />
      <Stats      data={data} />
      <Features   data={data} />
      <Comparison data={data} />
      <Versus     data={data} />
      <Cta        data={data} />
    </Layout>
  );
}
