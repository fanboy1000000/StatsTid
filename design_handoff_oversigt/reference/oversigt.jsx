/* StatsTid — Oversigt (Overblik) redesign · v2 (planlægning).
   Not just the current month: each balance shows what is accrued (optjent)
   vs spent (brugt), a 3-month forecast so the employee can plan, and how
   much can be transferred to next year vs. what lapses at year-end.
   Three directions on a design canvas. Danish, da-DK numbers. */

// ---- helpers ----
const da = (n, dec = 1) => n.toFixed(dec).replace(".", ",");
const daSigned = (n, dec = 1) => (n >= 0 ? "+" : "") + da(n, dec);
const daDay = (n) => (n === 1 ? "dag" : "dage");

// ---- seed data (shared across directions) ----
const EMP = { name: "Anna Berg", uid: "emp001", org: "Udvikling", ok: "AC · OK24" };
const MONTH = { name: "marts 2026", status: "Kladde", deadline: "5. april 2026", daysLeft: 30 };
const HORIZON = { to: "juni 2026", yearEnd: "31. december 2026" };
const NORM = { actual: 96.5, expected: 147.0, pct: 66 };

// Flex: a real saldo that is climbing toward the cap → planning matters.
const FLEX = {
  saldo: 22.5, earned: 31.0, spent: 8.5, loft: 37, band: 37, proj3: 31.5,
  series: [
    { m: "marts", v: 22.5, now: true },
    { m: "april", v: 25.0 },
    { m: "maj", v: 28.5 },
    { m: "juni", v: 31.5 },
  ],
};
const MER = 2.0;

// Ferie planning: 25 dage ret (heraf 3 overført), 8 afholdt, 6 planlagt.
const FERIE = { ret: 25, held: 8, planned: 6, rest: 11, carry: 3, transferMax: 5, mustHold: 6 };

// Absence quotas with transfer / lapse rules.
const QUOTAS = [
  { name: "Ferie", ret: 25, used: 8, planned: 6, rest: 11, carry: 3, unit: "dage",
    transfer: { type: "keep", text: "Op til 5 dage overføres" }, note: "3 overført fra 2025" },
  { name: "Omsorgsdage", ret: 2, used: 1, planned: 0, rest: 1, unit: "dage",
    transfer: { type: "lapse", text: "Bortfalder 31. dec" }, note: "nulstilles 1. januar" },
  { name: "Barns 1. & 2. sygedag", ret: 2, used: 1, planned: 0, rest: 1, unit: "dage",
    transfer: { type: "none", text: "Kan ikke overføres" }, note: "pr. barn pr. år" },
  { name: "Seniordage", ret: 3, used: 0, planned: 0, rest: 3, unit: "dage",
    transfer: { type: "lapse", text: "Bortfalder 31. dec" }, note: "aftalt i lokalaftale" },
];

// ---- atoms ----
function Bar({ pct, className = "" }) {
  return (
    <div className={`ov-bar ${className}`}>
      <div className="ov-bar__fill" style={{ width: `${Math.max(0, Math.min(100, pct))}%` }} />
    </div>
  );
}
function SegBar({ used, total, planned = 0, carry }) {
  const u = total > 0 ? (used / total) * 100 : 0;
  const p = total > 0 ? (planned / total) * 100 : 0;
  const carryPct = carry ? (carry / total) * 100 : null;
  return (
    <div className="ov-seg">
      <div className="ov-seg__used" style={{ width: `${u}%` }} />
      <div className="ov-seg__planned" style={{ width: `${p}%` }} />
      {carryPct != null && <div className="ov-seg__carry" style={{ left: `${100 - carryPct}%` }} />}
    </div>
  );
}
function TransferTag({ t }) {
  const cls = t.type === "keep" ? "ov-tag--keep" : t.type === "lapse" ? "ov-tag--lapse" : "ov-tag--none";
  return <span className={`ov-tag ${cls}`}>{t.text}</span>;
}

// ---- 3-month forecast (vertical bars toward a loft line) ----
function Forecast({ title = "Prognose for flexsaldo", caption, bare = false }) {
  const body = (
    <React.Fragment>
      <div className="ov-forecast__head">
        <div>
          <p className="ov-eyebrow" style={{ margin: 0 }}>{title}</p>
        </div>
        <div className="ov-forecast__legend">
          <span><i className="ov-swatch ov-swatch--actual" /> Faktisk</span>
          <span><i className="ov-swatch ov-swatch--proj" /> Prognose</span>
        </div>
      </div>
      <p className="ov-forecast__cap">{caption || "Ved nuværende tempo og uden afspadsering."}</p>
      <div className="ov-fc">
        <div className="ov-fc__loft" style={{ top: 0 }} />
        <div className="ov-fc__loftlabel">Loft {da(FLEX.loft, 0)} t</div>
        {FLEX.series.map((s) => (
          <div className="ov-fc__col" key={s.m}>
            <span className="ov-fc__val">{da(s.v)}</span>
            <div className={`ov-fc__bar ${s.now ? "" : "ov-fc__bar--proj"}`} style={{ height: `${(s.v / FLEX.loft) * 100}%` }} />
          </div>
        ))}
      </div>
      <div className="ov-fc__months">
        {FLEX.series.map((s) => (
          <div className={`ov-fc__month ${s.now ? "ov-fc__month--now" : ""}`} key={s.m}>{s.m}{s.now ? " · nu" : ""}</div>
        ))}
      </div>
      <div className="ov-fc__note">
        <span className="ov-fc__note-dot" />
        <p>Din flexsaldo rammer ca. <b>{da(FLEX.proj3)} t</b> i {HORIZON.to.split(" ")[0]} — tæt på loftet på {da(FLEX.loft, 0)} t. Flex over loftet bortfalder ved årets udgang, så <b>planlæg afspadsering</b>.</p>
      </div>
    </React.Fragment>
  );
  return bare ? body : <div className="ov-forecast">{body}</div>;
}

// ---- app-shell wrapper (non-scrolling; fills the artboard) ----
function Shell({ children }) {
  return (
    <div className="ov-shell">
      <header className="st-header">
        <h1 className="st-header__title">StatsTid</h1>
        <div className="st-header__user">
          <span className="st-header__org">{EMP.org}</span>
          <span className="st-header__uid">{EMP.uid}</span>
          <span className="st-badge st-badge--info">Employee</span>
          <button type="button" className="st-btn st-btn--ghost st-btn--sm">Log ud</button>
        </div>
      </header>
      <nav className="st-topnav" aria-label="Hovednavigation">
        <div className="st-topnav__list">
          <button type="button" className="st-tab st-tab--active" aria-current="page">Min tid</button>
        </div>
      </nav>
      <div className="ov-body">
        <aside className="st-sidebar">
          <nav className="st-sidebar__nav">
            <button type="button" className="st-navlink">Registrering</button>
            <button type="button" className="st-navlink st-navlink--active">Oversigt</button>
          </nav>
        </aside>
        <main className="ov-main">{children}</main>
      </div>
    </div>
  );
}

function MonthSwitch() {
  return (
    <div className="ov-monthswitch">
      <button type="button" className="st-btn st-btn--ghost st-btn--sm">←</button>
      <span className="ov-monthswitch__title">{MONTH.name}</span>
      <button type="button" className="st-btn st-btn--ghost st-btn--sm">→</button>
    </div>
  );
}

/* ============================================================
   Direction A — Saldo & planlægning (calm: ledger KPIs +
   forecast + ferie-transfer + quota planning table)
   ============================================================ */
function DirectionA() {
  return (
    <Shell>
      <div className="ov-pagehead">
        <div>
          <h1 className="ov-pagehead__title">Min oversigt</h1>
          <p className="ov-pagehead__sub">{EMP.name} · {EMP.ok}</p>
        </div>
        <MonthSwitch />
      </div>

      <div className="ov-monthbar">
        <div className="ov-monthbar__group">
          <span className="ov-monthbar__label">Denne måned</span>
          <span className="ov-monthbar__month">
            <span className="ov-monthbar__monthname">{MONTH.name}</span>
            <span className="st-badge st-badge--warning">{MONTH.status}</span>
          </span>
        </div>
        <div className="ov-monthbar__group">
          <div className="ov-monthbar__progresshead">
            <span className="ov-monthbar__label">Normtimer</span>
            <span className="ov-monthbar__val">{da(NORM.actual)} / {da(NORM.expected)} t
              <span className="ov-monthbar__pct">{NORM.pct}%</span></span>
          </div>
          <Bar pct={NORM.pct} />
        </div>
        <div className="ov-monthbar__group ov-monthbar__deadline">
          <span className="ov-monthbar__label">Frist for indsendelse</span>
          <span className="ov-monthbar__deadval">{MONTH.deadline}</span>
        </div>
      </div>

      {/* balances with optjent / brugt ledger */}
      <div className="ov-kpis">
        <div className="ov-kpi">
          <p className="ov-kpi__label">Flex saldo</p>
          <p className="ov-kpi__value">{daSigned(FLEX.saldo)} <small>t</small></p>
          <p className="ov-kpi__help">Inden for loftet på {da(FLEX.loft, 0)} t</p>
          <div className="ov-ledger">
            <div className="ov-ledger__item"><span className="ov-ledger__k">Optjent i år</span><span className="ov-ledger__v ov-ledger__v--earn">{daSigned(FLEX.earned)}</span></div>
            <div className="ov-ledger__item"><span className="ov-ledger__k">Brugt</span><span className="ov-ledger__v">−{da(FLEX.spent)} t</span></div>
          </div>
          <div className="ov-kpi__foot"><span className="ov-fwd">Om 3 mdr <span className="ov-fwd__arrow">→</span> <b>{da(FLEX.proj3)} t</b></span></div>
        </div>
        <div className="ov-kpi">
          <p className="ov-kpi__label">Ferie</p>
          <p className="ov-kpi__value">{FERIE.rest} <small>dage uplanlagt</small></p>
          <p className="ov-kpi__help">{FERIE.held} afholdt · {FERIE.planned} planlagt</p>
          <div className="ov-ledger">
            <div className="ov-ledger__item"><span className="ov-ledger__k">Ret i år</span><span className="ov-ledger__v">{FERIE.ret} dage</span></div>
            <div className="ov-ledger__item"><span className="ov-ledger__k">Heraf overført</span><span className="ov-ledger__v">{FERIE.carry} dage</span></div>
          </div>
          <div className="ov-kpi__foot"><TransferTag t={{ type: "keep", text: "Op til 5 kan overføres" }} /></div>
        </div>
        <div className="ov-kpi">
          <p className="ov-kpi__label">Normtimer</p>
          <p className="ov-kpi__value">{da(NORM.actual)} <small>/ {da(NORM.expected)} t</small></p>
          <p className="ov-kpi__help">På vej mod norm for marts</p>
          <div className="ov-kpi__foot">
            <div className="ov-kpi__bartext"><span>Registreret</span><span>{NORM.pct}%</span></div>
            <Bar pct={NORM.pct} className="ov-bar--thin" />
          </div>
        </div>
        <div className="ov-kpi">
          <p className="ov-kpi__label">Merarbejde</p>
          <p className="ov-kpi__value">{da(MER)} <small>t</small></p>
          <p className="ov-kpi__help">Til afspadsering eller udbetaling</p>
          <div className="ov-kpi__foot"><span className="ov-fwd">Opgøres ved <b>månedsskift</b></span></div>
        </div>
      </div>

      {/* planning row */}
      <div className="ov-planhead">
        <p className="ov-eyebrow">Planlægning</p>
        <span className="ov-horizon">Horisont: <b>{MONTH.name} → {HORIZON.to}</b></span>
      </div>
      <div style={{ display: "grid", gridTemplateColumns: "1.5fr 1fr", gap: 16, marginBottom: 20 }}>
        <Forecast />
        <div className="st-card">
          <div className="st-card__header">Ferie — kan overføres til 2027</div>
          <div className="st-card__body">
            <div className="ov-transfer">
              <div className="ov-transfer__row">
                <span className="ov-transfer__big" style={{ color: "var(--color-info)" }}>5</span>
                <span className="ov-transfer__lbl">dage <b>kan overføres</b> til næste ferieår — op til 1 uge ud over de 4 ugers afholdelsespligt.</span>
              </div>
              <div className="ov-transfer__row">
                <span className="ov-transfer__big" style={{ color: "var(--color-warning)" }}>{FERIE.mustHold}</span>
                <span className="ov-transfer__lbl">dage <b>skal planlægges</b> inden {HORIZON.yearEnd}, ellers udbetales eller bortfalder de.</span>
              </div>
            </div>
          </div>
        </div>
      </div>

      {/* quota planning table */}
      <div className="st-card">
        <div className="st-card__header">Fraværskvoter 2026 — optjent, brugt og overførsel</div>
        <div className="st-card__body st-card__body--flush">
          <table className="st-table ov-ptable">
            <thead>
              <tr>
                <th>Kvote</th>
                <th className="ov-num">Ret i år</th>
                <th className="ov-num">Brugt</th>
                <th className="ov-num">Planlagt</th>
                <th className="ov-num">Rest</th>
                <th>Ved årets udgang</th>
              </tr>
            </thead>
            <tbody>
              {QUOTAS.map((q) => (
                <tr key={q.name}>
                  <td className="ov-ptable__name">{q.name}<div className="ov-ptable__sub">{q.note}</div></td>
                  <td className="ov-num">{q.ret} {q.unit}</td>
                  <td className="ov-num">{q.used}</td>
                  <td className="ov-num">{q.planned}</td>
                  <td className="ov-num ov-rest">{q.rest}</td>
                  <td><TransferTag t={q.transfer} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </Shell>
  );
}

/* ============================================================
   Direction B — Dashboard med prognose
   ============================================================ */
function DirectionB() {
  return (
    <Shell>
      <div className="ov-pagehead">
        <div>
          <h1 className="ov-pagehead__title">Min oversigt</h1>
          <p className="ov-pagehead__sub">{EMP.name} · {EMP.ok} · planlægning {MONTH.name} → {HORIZON.to}</p>
        </div>
        <MonthSwitch />
      </div>

      <div className="ov-grid2">
        {/* left column */}
        <div className="ov-col">
          <div className="ov-statuscard">
            <div className="ov-statuscard__top">
              <span className="ov-statuscard__month">
                <span className="ov-statuscard__monthname">{MONTH.name}</span>
                <span className="st-badge st-badge--warning">{MONTH.status}</span>
              </span>
              <span className="ov-normhead__status">{NORM.pct}% · på vej mod norm</span>
            </div>
            <div className="ov-normhead">
              <span className="ov-normhead__val">{da(NORM.actual)} <small>/ {da(NORM.expected)} t normtimer</small></span>
            </div>
            <Bar pct={NORM.pct} className="ov-bar--tall" />
            <div className="ov-statusmeta">
              <div className="ov-statusmeta__item">
                <div className="ov-statusmeta__k">Flex saldo</div>
                <div className="ov-statusmeta__v">{daSigned(FLEX.saldo)} t</div>
              </div>
              <div className="ov-statusmeta__item">
                <div className="ov-statusmeta__k">Ferie uplanlagt</div>
                <div className="ov-statusmeta__v">{FERIE.rest} dage</div>
              </div>
              <div className="ov-statusmeta__item">
                <div className="ov-statusmeta__k">Frist</div>
                <div className="ov-statusmeta__v">{MONTH.deadline}</div>
              </div>
            </div>
          </div>

          <Forecast />
        </div>

        {/* right rail */}
        <div className="ov-col">
          <div className="ov-attncard">
            <div className="ov-attncard__head">
              Planlæg inden årsskiftet
              <span className="ov-attncard__count">3</span>
            </div>
            <div className="ov-attn">
              <span className="ov-attn__dot ov-attn__dot--warn" />
              <div>
                <p className="ov-attn__title">Afspadsér flex</p>
                <p className="ov-attn__body">Prognose {da(FLEX.proj3)} t i juni — nær loftet på {da(FLEX.loft, 0)} t. Overskydende flex bortfalder 31. dec.</p>
              </div>
            </div>
            <div className="ov-attn">
              <span className="ov-attn__dot ov-attn__dot--act" />
              <div>
                <p className="ov-attn__title">Afhold {FERIE.mustHold} feriedage</p>
                <p className="ov-attn__body">Op til 5 dage kan overføres til 2027 — de øvrige skal afholdes inden 31. dec.</p>
              </div>
            </div>
            <div className="ov-attn">
              <span className="ov-attn__dot ov-attn__dot--warn" />
              <div>
                <p className="ov-attn__title">3 seniordage bortfalder</p>
                <p className="ov-attn__body">Seniordage kan ikke overføres. Book dem inden {HORIZON.yearEnd}.</p>
              </div>
            </div>
          </div>

          <div className="ov-attncard">
            <div className="ov-attncard__head">Fraværskvoter — rest &amp; overførsel</div>
            <div className="ov-qmini">
              {QUOTAS.map((q) => (
                <div className="ov-qmini__row" key={q.name}>
                  <div className="ov-qmini__top">
                    <span className="ov-qmini__name">{q.name}</span>
                    <span className="ov-qmini__nums"><b>{q.rest}</b> <span>/ {q.ret} {q.unit}</span></span>
                  </div>
                  <SegBar used={q.used} total={q.ret} planned={q.planned} carry={q.carry || 0} />
                  <div style={{ marginTop: 7 }}><TransferTag t={q.transfer} /></div>
                </div>
              ))}
            </div>
          </div>
        </div>
      </div>
    </Shell>
  );
}

/* ============================================================
   Direction C — Saldo-bånd + fremskrivning + transfer-matrix
   ============================================================ */
function FlexMeter() {
  const half = (FLEX.saldo / FLEX.band) * 50;
  const pos = FLEX.saldo >= 0;
  return (
    <div className="ov-meter">
      <div className="ov-meter__track">
        <div className="ov-meter__zero" />
        <div className={`ov-meter__fill ${pos ? "" : "ov-meter__fill--neg"}`}
          style={pos ? { left: "50%", width: `${half}%` } : { right: "50%", width: `${-half}%` }} />
      </div>
      <div className="ov-meter__scale"><span>−{da(FLEX.band, 0)}</span><span>0</span><span>loft +{da(FLEX.loft, 0)}</span></div>
    </div>
  );
}

function DirectionC() {
  return (
    <Shell>
      <div className="ov-pagehead" style={{ marginBottom: 14 }}>
        <div>
          <h1 className="ov-pagehead__title">Min oversigt</h1>
          <p className="ov-pagehead__sub">{EMP.name} · {EMP.ok}</p>
        </div>
        <MonthSwitch />
      </div>

      <div className="ov-ribbon">
        <span className="ov-monthbar__monthname" style={{ fontSize: 15 }}>{MONTH.name}</span>
        <span className="st-badge st-badge--warning">{MONTH.status}</span>
        <span className="ov-ribbon__deadline">Indsend inden <b>{MONTH.deadline}</b> · om {MONTH.daysLeft} dage</span>
      </div>

      {/* balance band with optjent / brugt */}
      <div className="ov-band">
        <div className="ov-band__cell">
          <p className="ov-band__label">Flex saldo</p>
          <div className="ov-band__big">{daSigned(FLEX.saldo)} <small>t</small></div>
          <FlexMeter />
          <div className="ov-ledger" style={{ marginTop: 12 }}>
            <div className="ov-ledger__item"><span className="ov-ledger__k">Optjent</span><span className="ov-ledger__v ov-ledger__v--earn">{daSigned(FLEX.earned)}</span></div>
            <div className="ov-ledger__item"><span className="ov-ledger__k">Brugt</span><span className="ov-ledger__v">−{da(FLEX.spent)} t</span></div>
          </div>
        </div>
        <div className="ov-band__cell">
          <p className="ov-band__label">Normtimer</p>
          <div className="ov-band__big">{da(NORM.actual)} <small>/ {da(NORM.expected)} t</small></div>
          <div className="ov-band__progresshead">
            <span className="ov-band__sub" style={{ margin: 0 }}>Registreret denne måned</span>
            <span className="ov-band__pct">{NORM.pct}%</span>
          </div>
          <Bar pct={NORM.pct} className="ov-bar--tall" />
        </div>
        <div className="ov-band__cell">
          <p className="ov-band__label">Ferie</p>
          <div className="ov-band__big">{FERIE.rest} <small>dage</small></div>
          <p className="ov-band__sub" style={{ marginBottom: 10 }}>{FERIE.held} afholdt · {FERIE.planned} planlagt</p>
          <TransferTag t={{ type: "keep", text: "Op til 5 overføres" }} />
        </div>
        <div className="ov-band__cell">
          <p className="ov-band__label">Merarbejde</p>
          <div className="ov-band__big">{da(MER)} <small>t</small></div>
          <p className="ov-band__sub">Til afspadsering<br />eller udbetaling</p>
        </div>
      </div>

      {/* full-width forecast */}
      <div className="ov-planhead" style={{ marginTop: 0 }}>
        <p className="ov-eyebrow">Fremskrivning — om 3 måneder</p>
        <span className="ov-horizon">{MONTH.name} → <b>{HORIZON.to}</b></span>
      </div>
      <div style={{ marginBottom: 22 }}><Forecast caption="Flexsaldo ved nuværende tempo. Planlæg afspadsering før loftet nås." /></div>

      {/* transfer matrix */}
      <p className="ov-eyebrow">Fraværskvoter 2026 — hvad kan overføres?</p>
      <div className="ov-matrix">
        {QUOTAS.map((q) => (
          <div className="ov-qcard" key={q.name}>
            <div>
              <p className="ov-qcard__name">{q.name}</p>
              <p className="ov-qcard__year">{q.used} brugt · {q.planned} planlagt af {q.ret}</p>
            </div>
            <div className="ov-qcard__frac">{q.rest} <span className="ov-qcard__unit">{daDay(q.rest)} tilbage</span></div>
            <SegBar used={q.used} total={q.ret} planned={q.planned} carry={q.carry || 0} />
            <div className="ov-qcard__foot">
              <TransferTag t={q.transfer} />
              <div className="ov-qcard__note">{q.note}</div>
            </div>
          </div>
        ))}
      </div>
    </Shell>
  );
}

/* ============================================================
   Direction D — Grid / fliser (modular bento grid)
   Every piece of information is a uniform tile in one grid.
   ============================================================ */
function DirectionD() {
  return (
    <Shell>
      <div className="ov-pagehead">
        <div>
          <h1 className="ov-pagehead__title">Min oversigt</h1>
          <p className="ov-pagehead__sub">{EMP.name} · {EMP.ok} · planlægning {MONTH.name} → {HORIZON.to}</p>
        </div>
        <MonthSwitch />
      </div>

      <div className="ov-bento">
        {/* balances row */}
        <div className="ov-tile">
          <p className="ov-tile__label">Flex saldo</p>
          <p className="ov-tile__value">{daSigned(FLEX.saldo)} <small>t</small></p>
          <FlexMeter />
          <div className="ov-ledger" style={{ marginTop: 12 }}>
            <div className="ov-ledger__item"><span className="ov-ledger__k">Optjent</span><span className="ov-ledger__v ov-ledger__v--earn">{daSigned(FLEX.earned)}</span></div>
            <div className="ov-ledger__item"><span className="ov-ledger__k">Brugt</span><span className="ov-ledger__v">−{da(FLEX.spent)} t</span></div>
          </div>
        </div>
        <div className="ov-tile">
          <p className="ov-tile__label">Ferie</p>
          <p className="ov-tile__value">{FERIE.rest} <small>dage uplanlagt</small></p>
          <p className="ov-tile__sub">{FERIE.held} afholdt · {FERIE.planned} planlagt · {FERIE.ret} i alt</p>
          <div className="ov-tile__foot"><TransferTag t={{ type: "keep", text: "Op til 5 kan overføres" }} /></div>
        </div>
        <div className="ov-tile">
          <p className="ov-tile__label">Normtimer</p>
          <p className="ov-tile__value">{da(NORM.actual)} <small>/ {da(NORM.expected)} t</small></p>
          <p className="ov-tile__sub">På vej mod norm for marts</p>
          <div className="ov-tile__foot">
            <div className="ov-kpi__bartext"><span>Registreret</span><span>{NORM.pct}%</span></div>
            <Bar pct={NORM.pct} className="ov-bar--thin" />
          </div>
        </div>
        <div className="ov-tile">
          <p className="ov-tile__label">Merarbejde</p>
          <p className="ov-tile__value">{da(MER)} <small>t</small></p>
          <p className="ov-tile__sub">Til afspadsering eller udbetaling</p>
          <div className="ov-tile__foot"><span className="ov-fwd">Opgøres ved <b>månedsskift</b></span></div>
        </div>

        {/* status + forecast row */}
        <div className="ov-tile ov-tile--2">
          <div className="ov-tile__statustop">
            <span className="ov-tile__label"><span className="ov-monthbar__monthname" style={{ fontSize: 16, textTransform: "none", letterSpacing: 0 }}>{MONTH.name}</span><span className="st-badge st-badge--warning">{MONTH.status}</span></span>
            <span className="ov-normhead__status">{NORM.pct}% · på vej mod norm</span>
          </div>
          <div className="ov-normhead" style={{ marginBottom: 8 }}>
            <span className="ov-normhead__val">{da(NORM.actual)} <small>/ {da(NORM.expected)} t normtimer</small></span>
          </div>
          <Bar pct={NORM.pct} className="ov-bar--tall" />
          <div className="ov-tile__metarow">
            <div><div className="ov-tile__mk">Arbejdsdage</div><div className="ov-tile__mv">13 / 21</div></div>
            <div><div className="ov-tile__mk">Ikke fordelt</div><div className="ov-tile__mv">4,0 t</div></div>
            <div><div className="ov-tile__mk">Frist</div><div className="ov-tile__mv">{MONTH.deadline}</div></div>
          </div>
        </div>
        <div className="ov-tile ov-tile--2"><Forecast bare /></div>

        {/* quotas */}
        <div className="ov-bento__head">
          <p className="ov-eyebrow">Fraværskvoter 2026 — kan overføres?</p>
          <span className="ov-horizon">Ved årets udgang: {HORIZON.yearEnd}</span>
        </div>
        {QUOTAS.map((q) => (
          <div className="ov-tile" key={q.name}>
            <p className="ov-tile__label" style={{ textTransform: "none", letterSpacing: 0, fontSize: 14, fontWeight: "var(--font-weight-semibold)", color: "var(--color-text)" }}>{q.name}</p>
            <p className="ov-tile__sub" style={{ marginTop: 3 }}>{q.used} brugt · {q.planned} planlagt af {q.ret}</p>
            <p className="ov-tile__value" style={{ fontSize: 26, marginTop: 12 }}>{q.rest} <small>{daDay(q.rest)} tilbage</small></p>
            <div style={{ marginTop: 10 }}><SegBar used={q.used} total={q.ret} planned={q.planned} carry={q.carry || 0} /></div>
            <div className="ov-tile__foot"><TransferTag t={q.transfer} /></div>
          </div>
        ))}
      </div>
    </Shell>
  );
}

// Current-balance tiles shown above the year grid.
const BALANCES = [
  { label: "Flex saldo", value: "+22,5", unit: "t", sub: "optjent overtid" },
  { label: "Ferie", value: "22", unit: "dage", sub: "saldo" },
  { label: "Omsorgsdage", value: "1", unit: "dag", sub: "rest" },
  { label: "Seniordage", value: "3", unit: "dage", sub: "rest" },
  { label: "Sygedage", value: "4", unit: "dage", sub: "i år" },
  { label: "Barns sygedag", value: "1", unit: "dag", sub: "i år" },
];

// ---- Year matrix data (Direction E) ----
const YMONTHS = ["Jan", "Feb", "Mar", "Apr", "Maj", "Jun", "Jul", "Aug", "Sep", "Okt", "Nov", "Dec"];
const NOW_I = 2; // marts in progress
const YEAR = {
  diffNorm:   [6.0, 5.0, 2.5, 0.0, 0.0, -8.0, -10.0, 0.0, 0.0, 0.0, 0.0, -5.0],
  ferieAfholdt: [0, 3, 0, 0, 0, 0, 10, 0, 0, 5, 0, 0],
  ferieSaldo:   [25, 22, 22, 22, 22, 22, 12, 12, 12, 7, 7, 7],
  ferieKeep:    [null, null, null, null, null, null, null, null, null, null, null, 5],
  omsAfholdt: [0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0],
  omsSaldo:   [2, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1],
  omsKeep:    [null, null, null, null, null, null, null, null, null, null, null, 0],
  senAfholdt: [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 3],
  senSaldo:   [3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 0],
  senKeep:    [null, null, null, null, null, null, null, null, null, null, null, 0],
};
// Keep the grid internally consistent: arbejdstid = norm + diff fra norm.
YEAR.arbejdstid = YEAR.diffNorm.map((d) => NORM.expected + d);

function YRow({ label, values, sub, fmt, signed, keep }) {
  const cell = (v, i) => {
    const cls = ["ov-ynum"];
    if (i === NOW_I) cls.push("ov-ynow");
    else if (i > NOW_I) cls.push("ov-yproj");
    let content;
    if (v == null) content = <span className="ov-ydash">–</span>;
    else {
      content = fmt ? fmt(v) : signed ? daSigned(v) : da(v);
      if (signed && v > 0) cls.push("ov-ypos");
      else if (signed && v < 0) cls.push("ov-yneg");
      if (keep && v > 0) cls.push("ov-ykeep");
    }
    return <td key={i} className={cls.join(" ")}>{content}</td>;
  };
  return (
    <tr className={`ov-yrow ${sub ? "ov-yrow--sub" : ""}`}>
      <td className="ov-ylabel">{label}</td>
      {values.map(cell)}
    </tr>
  );
}

/* ============================================================
   Direction E — Årsgrid (year matrix)
   Months across the top, metrics grouped by category. Jan–Mar
   faktisk, Apr–Dec planlagt/prognose, current month highlighted.
   ============================================================ */
function DirectionE() {
  const days0 = (n) => (n === 0 ? <span className="ov-ydash">–</span> : da(n, 0));
  const intDays = (n) => (n === 0 ? <span className="ov-ydash">–</span> : String(n));
  return (
    <Shell>
      <div className="ov-pagehead">
        <div>
          <h1 className="ov-pagehead__title">Årsoversigt 2026</h1>
          <p className="ov-pagehead__sub">{EMP.name} · {EMP.ok} · Norm: {da(NORM.expected, 0)} timer</p>
        </div>
        <div className="ov-monthswitch">
          <button type="button" className="st-btn st-btn--ghost st-btn--sm">←</button>
          <span className="ov-monthswitch__title" style={{ minWidth: 64 }}>2026</span>
          <button type="button" className="st-btn st-btn--ghost st-btn--sm">→</button>
        </div>
      </div>

      <div className="ov-statrow">
        {BALANCES.map((b) => (
          <div className="ov-stat" key={b.label}>
            <p className="ov-stat__label">{b.label}</p>
            <p className="ov-stat__value">{b.value} <small>{b.unit}</small></p>
            <p className="ov-stat__sub">{b.sub}</p>
          </div>
        ))}
      </div>

      <div className="st-card" style={{ marginTop: 18 }}>
        <div className="st-card__body st-card__body--flush" style={{ padding: "8px 16px 12px" }}>
          <table className="ov-ytable">
            <colgroup>
              <col className="ov-ycol-label" />
              {YMONTHS.map((m, i) => <col key={i} />)}
            </colgroup>
            <thead>
              <tr>
                <th className="ov-ylabel">2026</th>
                {YMONTHS.map((m, i) => (
                  <th key={m} className={i === NOW_I ? "ov-ynow ov-ynow-head" : ""}>
                    {i === NOW_I ? <span className="ov-ynow-tag">Nu</span> : null}{m}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              <tr className="ov-ygroup ov-ygroup--first"><td colSpan={13}>Arbejdstid</td></tr>
              <YRow label="Arbejdstid" values={YEAR.arbejdstid} fmt={(v) => `${da(v)}`} />
              <YRow label="Diff. fra norm" values={YEAR.diffNorm} sub signed />

              <tr className="ov-ygroup"><td colSpan={13}>Ferie</td></tr>
              <YRow label="Saldo (rest)" values={YEAR.ferieSaldo} sub fmt={intDays} />
              <YRow label="Afholdt" values={YEAR.ferieAfholdt} sub fmt={intDays} />
              <YRow label="Kan overføres" values={YEAR.ferieKeep} sub fmt={intDays} keep />

              <tr className="ov-ygroup"><td colSpan={13}>Omsorgsdage</td></tr>
              <YRow label="Saldo (rest)" values={YEAR.omsSaldo} sub fmt={intDays} />
              <YRow label="Afholdt" values={YEAR.omsAfholdt} sub fmt={intDays} />
              <YRow label="Kan overføres" values={YEAR.omsKeep} sub fmt={intDays} />

              <tr className="ov-ygroup"><td colSpan={13}>Seniordage</td></tr>
              <YRow label="Saldo (rest)" values={YEAR.senSaldo} sub fmt={intDays} />
              <YRow label="Afholdt" values={YEAR.senAfholdt} sub fmt={intDays} />
              <YRow label="Kan overføres" values={YEAR.senKeep} sub fmt={intDays} />
            </tbody>
          </table>
        </div>
      </div>
    </Shell>
  );
}

// ---- mount on the design canvas ----
function OversigtCanvas() {
  return (
    <DesignCanvas>
      <DCSection
        id="oversigt"
        title="Oversigt (Overblik) — Min tid"
        subtitle="Årsgrid (E) viser hele året som matrix. A–D er alternative overblik med samme planlægningsdata."
      >
        <DCArtboard id="e" label="E · Årsgrid — måneder × kategorier" width={1240} height={956}>
          <DirectionE />
        </DCArtboard>
        <DCArtboard id="a" label="A · Saldo & planlægning (rolig)" width={1240} height={1296}>
          <DirectionA />
        </DCArtboard>
        <DCArtboard id="b" label="B · Dashboard med prognose (tæt)" width={1240} height={964}>
          <DirectionB />
        </DCArtboard>
        <DCArtboard id="c" label="C · Saldo-bånd + fremskrivning (markant)" width={1240} height={1084}>
          <DirectionC />
        </DCArtboard>
        <DCArtboard id="d" label="D · Grid / fliser (bento)" width={1240} height={1036}>
          <DirectionD />
        </DCArtboard>
      </DCSection>
    </DesignCanvas>
  );
}

ReactDOM.createRoot(document.getElementById("root")).render(<OversigtCanvas />);
