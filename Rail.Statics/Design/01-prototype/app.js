"use strict";

(() => {
  const {lines, stations, rides, board, alerts, screens} = window.RailFixtures;
  const $ = selector => document.querySelector(selector);
  const screenById = new Map(screens.map(screen => [screen[0],screen]));
  const params = new URLSearchParams(location.search);
  const fa = new Intl.NumberFormat("fa-IR");
  const demoDate = "2026-11-09";
  const defaults = () => ({
    from:"tajrish", to:"farhangsara", station:"imam-khomeini", field:"from", query:"",
    after:"08:00", date:demoDate, directoryLine:0, mapLine:0, boardLine:0, boardDirection:"",
    calendarReturn:"home", planError:false,
    boardAfter:"08:00", trip:board[0], alertTab:"active", alertId:alerts[0].id, auth:params.get("account")==="sample",
    favorites:["tajrish","imam-khomeini"], names:true,
    theme:params.get("theme")==="dark"?"dark":"light",
    layout:params.get("layout")==="desktop"?"desktop":"mobile", signInReturn:"favorites",
  });
  let state = defaults();
  let current = "home";

  const shapes = {
    arrow:'<path d="M19 12H5m6-6-6 6 6 6"/>',
    back:'<path d="M5 12h14m-6-6 6 6-6 6"/>',
    swap:'<path d="M8 4v15m-4-4 4 4 4-4M16 20V5m-4 4 4-4 4 4"/>',
    home:'<path d="m3 10 9-7 9 7v10H3z"/><path d="M9 20v-6h6v6"/>',
    map:'<path d="m3 5 6-2 6 2 6-2v16l-6 2-6-2-6 2zM9 3v16m6-14v16"/>',
    pin:'<path d="M20 10c0 5-8 11-8 11S4 15 4 10a8 8 0 1 1 16 0Z"/><circle cx="12" cy="10" r="2.3"/>',
    train:'<rect x="5" y="3" width="14" height="15" rx="4"/><path d="M5 11h14M9 18l-3 3m9-3 3 3m-9-6h.01m6 0h.01"/>',
    clock:'<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>',
    bell:'<path d="M5 17h14l-2-3V9a5 5 0 0 0-10 0v5zM10 21h4"/>',
    heart:'<path d="M20 6c-3-3-6-1-8 1-2-2-5-4-8-1-5 5 2 10 8 15 6-5 13-10 8-15Z"/>',
    person:'<circle cx="12" cy="8" r="4"/><path d="M4 21v-2a5 5 0 0 1 5-5h6a5 5 0 0 1 5 5v2"/>',
    search:'<circle cx="10.5" cy="10.5" r="6"/><path d="m15 15 5 5"/>',
    info:'<circle cx="12" cy="12" r="9"/><path d="M12 11v5m0-8h.01"/>',
    alert:'<path d="m12 3 10 17H2zM12 9v4m0 3h.01"/>',
    wallet:'<rect x="3" y="6" width="18" height="13" rx="2"/><path d="M3 8 17 3m0 10h4"/>',
    calendar:'<rect x="3" y="5" width="18" height="16" rx="2"/><path d="M7 3v4m10-4v4M3 10h18m-13 5h3m2 0h3"/>',
    wifi:'<path d="M2 8a16 16 0 0 1 20 0M5 12a11 11 0 0 1 14 0m-11 4a6 6 0 0 1 8 0m-4 4h.01"/>',
    moon:'<path d="M21 14a9 9 0 1 1-11-11 8 8 0 0 0 11 11Z"/>',
    link:'<path d="M8 16 16 8m-5-2 2-2a4 4 0 0 1 6 6l-2 2m-4 6-2 2a4 4 0 0 1-6-6l2-2"/>',
    check:'<path d="m5 12 4 4L19 6"/>'
  };

  const icon = name => `<svg class="i" viewBox="0 0 24 24" aria-hidden="true">${shapes[name] || shapes.info}</svg>`;
  const name = id => stations[id][0];
  const latin = id => state.names ? `<small lang="en" dir="ltr">${stations[id][1]}</small>` : "";
  const digits = value => String(value).replace(/[0-9]/g, d => fa.format(Number(d)));
  const time = (value, large=false) => `<bdi class="time${large?" large":""}" dir="ltr">${digits(value)}</bdi>`;
  const line = number => `<span class="line-pill" style="--line-color:${lines[number]}"><i aria-hidden="true"></i>خط ${fa.format(number)}</span>`;
  const linesFor = id => stations[id][2].map(line).join("");
  const badge = (text, style="") => `<span class="badge ${style}">${text}</span>`;
  const notice = (text, kind="info") => `<div class="notice ${kind}">${icon(kind==="info"?"info":"alert")}<span>${text}</span></div>`;
  const btn = (text, action, cls="", value="") => `<button type="button" class="button ${cls}" data-action="${action}" data-value="${value}">${text}</button>`;
  const link = (text, id, cls="") => `<a class="button ${cls}" href="#${id}" data-go="${id}">${text}</a>`;
  const head = (tag,title,description) => `<header class="screen-head"><span class="eyebrow">${tag}</span><h1 tabindex="-1">${title}</h1><p class="muted">${description}</p></header>`;
  const sect = (title, right="") => `<div class="section-heading"><h2>${title}</h2>${right}</div>`;
  const metric = (n, label) => `<div class="metric"><strong>${n}</strong><span>${label}</span></div>`;
  const opt = (value, title, selected) => `<option value="${value}"${String(value)===String(selected)?" selected":""}>${title}</option>`;
  const tab = (label, action, val, selected) => `<button type="button" class="chip" data-action="${action}" data-value="${val}" aria-pressed="${selected}">${label}</button>`;
  const row = (title, desc, glyph, action, value, end=icon("arrow")) => `<button type="button" class="list-row" data-action="${action}" data-value="${value}"><span class="row-icon">${icon(glyph)}</span><span class="row-main"><strong>${title}</strong>${desc}</span>${end}</button>`;
  const stationRow = (id, action="station") => row(name(id),latin(id),"pin",action,id,`<span class="badges">${linesFor(id)}</span>`);
  const empty = (glyph,title,description,action) => `<div class="card empty"><span class="row-icon">${icon(glyph)}</span><h2>${title}</h2><p class="muted">${description}</p><div class="actions">${action}</div></div>`;
  const timeline = (stops,n) => `<ol class="timeline" style="--track:${lines[n] || "var(--border)"}">${stops.map(([id,t,sub])=>`<li><i class="station-dot" aria-hidden="true"></i><div><strong>${name(id)}</strong>${sub?`<small>${sub}</small>`:""}</div>${time(t)}</li>`).join("")}</ol>`;
  const heroArt = '<svg class="feature-art" viewBox="0 0 200 170" aria-hidden="true"><g fill="none" stroke="currentColor" stroke-width="3"><path d="M5 35h75l43 43h75M7 60h64l45 45h83M10 85h55l50 50h85"/><circle cx="79" cy="35" r="5"/><circle cx="116" cy="105" r="5"/></g></svg>';

  function schematic(active=0, trip=false) {
    const tracks = trip
      ? [[1,"M190 30V146"], ...(demoItinerary().length>1?[[2,"M190 146H207L253 100H334"]]:[])]
      : [[1,"M190 30V222"],[2,"M33 146H207L253 100H334"],[4,"M28 96H226L230 100H253L270 83H309L338 54"],[5,"M50 96V129L33 146"]];
    const dots = [
      ["tajrish",190,30,202,23],["darvazeh-dolat",190,96,201,83],
      ["imam-khomeini",190,146,196,168],["darvazeh-shemiran",253,100,273,126],
      ["farhangsara",334,100,323,91],["eram-e-sabz",50,96,66,82],
      ["meydan-e-azadi",115,96,115,114],["tehran-sadeghiyeh",33,146,56,167],
      ["kahrizak",190,222,177,232]
    ];
    const visible = n => trip ? n===1 || n===2 : !active || n===active;
    const routeStations = demoItinerary().flatMap(leg=>leg.stops.map(([id])=>id));
    const reduced = trip ? dots.filter(([id])=>routeStations.includes(id)) : dots;
    return `<figure class="map"><svg viewBox="0 0 370 252" role="group" aria-label="نمودار نمونهٔ خطوط و ایستگاه‌های مترو" direction="ltr">
      <defs><pattern id="rail-grid" width="18" height="18" patternUnits="userSpaceOnUse"><path d="M18 0H0V18" fill="none" class="gridlines"/></pattern></defs>
      <rect width="370" height="252" fill="url(#rail-grid)"/>
      ${tracks.map(([n,d])=>`<path class="${visible(n)?"":"line-faint"}" d="${d}" fill="none" stroke="${lines[n]}" stroke-width="${visible(n)?7:5}" stroke-linecap="round" stroke-linejoin="round"/>`).join("")}
      ${reduced.map(([id,x,y,tx,ty])=>`<a href="#station" data-action="station" data-value="${id}" aria-label="${name(id)}" class="${active&&!stations[id][2].includes(active)?"line-faint":""}"><circle class="map-hit" cx="${x}" cy="${y}" r="14"/><circle class="map-dot" cx="${x}" cy="${y}" r="${stations[id][2].length>1?6.5:4.8}"/><text x="${tx}" y="${ty}" text-anchor="middle">${name(id)}</text></a>`).join("")}
    </svg><figcaption>نمودار بخشی از شبکه · جایگاه‌ها و فاصله‌ها شماتیک‌اند.</figcaption></figure>`;
  }

  function planner() {
    const field = (key, label, filled) => `<button class="field" type="button" data-action="pick" data-value="${key}"><i class="station-dot ${filled?"filled":""}" aria-hidden="true"></i><span><small>${label}</small><strong>${name(state[key])}</strong></span></button>`;
    return `${head("متروی تهران","کجا می‌روید؟","مسیر، تعویض خط و حرکت قطارها را یکجا ببینید.")}
      <div class="layout wide">
        <div class="stack"><section class="card">
          ${sect("مسیر سفر",badge("زمان‌بندی نمونه"))}
          <div class="form-grid"><div class="station-fields">${field("from","مبدأ",false)}${field("to","مقصد",true)}</div>
          <button type="button" class="icon-button" data-action="swap" aria-label="جابه‌جایی مبدأ و مقصد">${icon("swap")}</button></div>
          <div class="actions">${btn("تاریخ و زمان سفر","calendar","quiet","home")}${badge(digits(state.after)+" · تهران")}</div>
          ${state.planError?`<div class="section" role="alert">${notice("مبدأ و مقصد باید متفاوت باشند.","danger")}</div>`:""}
          <div class="actions">${btn("پیدا کردن مسیر "+icon("arrow"),"plan","full")}</div>
        </section>
        <div class="list">${row("تابلوی حرکت","<small>حرکت‌های برنامه‌ریزی‌شده از ایستگاه</small>","clock","departures","imam-khomeini")}
        ${row("اطلاعیه‌های سرویس","<small>نمونهٔ اطلاعیه‌های خطوط</small>","bell","navigate","alerts")}</div></div>
        <section class="card stack">${sect("شبکه در یک نگاه",`<a class="text-link" href="#network" data-go="network">نقشه ${icon("arrow")}</a>`)}${schematic()}<p class="caption">از روی نقشه یک ایستگاه را انتخاب کنید.</p></section>
      </div>`;
  }

  function normalized(query) {
    return query.trim().toLocaleLowerCase().normalize("NFKC").replace(/ي|ى/g,"ی").replace(/ك/g,"ک").replace(/[\u200c\u064b-\u065f]/g,"");
  }
  function stationList(picking) {
    const query=normalized(state.query);
    const result=Object.keys(stations).filter(id =>
      (picking || !state.directoryLine || stations[id][2].includes(state.directoryLine)) &&
      (!query || [id,...stations[id].slice(0,2)].some(label=>normalized(label).includes(query))));
    return result.length ? result.map(id=>stationRow(id,picking?"choose":"station")).join("") :
      notice("ایستگاهی با این جست‌وجو پیدا نشد.");
  }
  function directory(picking) {
    const filters = picking?"":`<div class="chip-row">${tab("همه","directory-line",0,state.directoryLine===0)}${Object.keys(lines).map(n=>tab("خط "+fa.format(n),"directory-line",n,state.directoryLine===Number(n))).join("")}</div>`;
    const back = picking?link("بازگشت به برنامه‌ریز","home","quiet"):"";
    return `${picking ? head("برنامه‌ریز سفر",state.field==="from"?"انتخاب مبدأ":"انتخاب مقصد","نام فارسی یا انگلیسی ایستگاه را جست‌وجو کنید.") : head("ایستگاه‌ها","ایستگاه خود را پیدا کنید","با نام یا شمارهٔ خط جست‌وجو کنید.")}
      <section class="card stack">
        <label class="search">${icon("search")}<span class="visually-hidden">جست‌وجوی ایستگاه</span><input id="station-query" type="search" autocomplete="off" placeholder="مثلاً تجریش یا Tajrish"></label>
        ${filters}<div id="station-list" class="list" aria-live="polite">${stationList(picking)}</div>${back}
      </section>`;
  }

  function demoItinerary() {
    if (state.after>"08:05" || state.date!==demoDate || state.from!=="tajrish") return [];
    if (state.to==="imam-khomeini") return [rides[0]];
    if (state.to==="farhangsara") return rides;
    return [];
  }
  function journeys(detail) {
    const route=demoItinerary();
    if (!route.length) return noJourney();
    const start=route[0], end=route[route.length-1];
    const stats=`<div class="metrics">${metric(time(start.start),"سوار شدن")}${metric(time(end.end),"رسیدن")}${metric(fa.format(route.length-1),"تعویض خط")}</div>`;
    const transfer=route.length>1?notice("۶ دقیقه بین دو قطار در این نمونه. زمان تعویض خط برآوردی است.","warn"):"";
    if (!detail) return `${head("پیشنهاد مسیر",name(state.from)+" تا "+name(state.to),"نمونهٔ زودترین رسیدن بر اساس برنامهٔ حرکت")}
      <div class="layout wide"><div class="stack"><section class="card stack">${sect("مسیر پیشنهادی",badge("زمان‌بندی نمونه"))}${stats}
      <div class="badges">${route.map(r=>line(r.line)).join("")}${badge(route.length>1?"۴۷ دقیقه":"۲۲ دقیقه")}</div>
      ${transfer}${link("دیدن مراحل سفر "+icon("arrow"),"journey","full")}${link("تغییر جست‌وجو","home","quiet")}</section></div>
      <section class="card stack">${sect("مسیر در شبکه")}${schematic(0,true)}</section></div>`;
    return `${head("راهنمای سفر","مراحل مسیر",name(state.from)+" تا "+name(state.to))}
      <div class="layout wide"><div class="stack">${stats}
      ${route.map((r,i)=>`${i===1?transfer:""}<section class="card stack">${sect("مرحلهٔ "+fa.format(i+1),line(r.line))}
        <p class="caption">جهت حرکت به سوی <strong>${name(r.direction)}</strong></p>
        ${timeline([[r.from,r.start,"سوار شوید"],...r.stops.slice(1,-1),[r.to,r.end,"پیاده شوید"]],r.line)}</section>`).join("")}
      </div><section class="card stack">${sect("نمودار سفر")}${schematic(0,true)}${link("بازگشت به نتیجه","journeys","quiet")}</section></div>`;
  }
  function noJourney() {
    return `${head("پیشنهاد مسیر","مسیر در نمونه پیدا نشد",name(state.from)+" تا "+name(state.to))}
      ${empty("train","جست‌وجو را تغییر دهید","پیش‌نمایش فقط سفر نمونهٔ تجریش به امام خمینی و فرهنگسرا را برای تاریخ و زمان اولیه دارد.",link("ویرایش مسیر","home"))}`;
  }
  function network() {
    const supported=[1,2,4,5];
    const options=Object.keys(stations).filter(id=>!state.mapLine||stations[id][2].includes(state.mapLine)).slice(0,7);
    return `${head("متروی تهران","نقشهٔ شبکه","نمای شماتیک بخشی از شبکه برای بررسی ایستگاه و تعویض خط.")}
      <div class="layout wide"><section class="card stack">
        <div class="chip-row">${tab("همه","map-line",0,state.mapLine===0)}${supported.map(n=>tab("خط "+fa.format(n),"map-line",n,state.mapLine===n)).join("")}</div>
        ${schematic(state.mapLine)}
        <div class="badges">${supported.map(line).join("")}</div>
      </section><section class="card stack">${sect("ایستگاه‌ها",`<a class="text-link" href="#stations" data-go="stations">فهرست کامل ${icon("arrow")}</a>`)}
        <div class="list">${options.map(id=>stationRow(id)).join("")}</div></section></div>`;
  }

  function station() {
    const id=state.station, saved=state.auth&&state.favorites.includes(id);
    const facilities=[["آسانسور","وضعیت تأییدنشده"],["سرویس بهداشتی","وضعیت تأییدنشده"],["ورودی بدون پله","اطلاعات نمونه موجود نیست"]];
    return `${head("ایستگاه مترو",name(id),state.names?stations[id][1]:"شبکهٔ متروی تهران")}
      <div class="layout wide">
      <section class="card stack">
        ${sect("اتصال به شبکه",stations[id][2].length>1?badge("ایستگاه تقاطعی"):badge("ایستگاه"))}
        <div class="badges">${linesFor(id)}</div>
        ${btn(icon("clock")+" تابلوی حرکت","departures","full",id)}
        <button type="button" class="button quiet" data-action="favorite" data-value="${id}" aria-pressed="${saved}">${icon("heart")}${saved?"حذف از نشان‌شده‌ها":"نشان کردن ایستگاه"}</button>
      </section>
      <section class="card stack">${sect("امکانات و دسترسی")}
        ${facilities.map(([label,status])=>`<div class="setting"><span>${label}</span>${badge(status)}</div>`).join("")}
        ${notice("برای دسترسی بدون پله، وضعیت کل مسیر تا سکو باید بررسی شود.")}
      </section></div>`;
  }
  const minutes = text => Number(text.slice(0,2))*60+Number(text.slice(3));
  const clock = value => [Math.floor(value/60),value%60].map(n=>String(n).padStart(2,"0")).join(":");
  function departures() {
    const options=Object.keys(stations);
    const directions=[...new Set(board.filter(r=>r.station===state.station&&(!state.boardLine||r.line===state.boardLine)).map(r=>r.direction))];
    const rows=state.date===demoDate ? board.filter(r=>r.station===state.station&&(!state.boardLine||r.line===state.boardLine)&&
      (!state.boardDirection||r.direction===state.boardDirection)&&r.time>=state.boardAfter) : [];
    return `${head("برنامهٔ حرکت","تابلوی حرکت",name(state.station)+" · زمان محلی تهران")}
      <div class="layout wide"><section class="card stack">${sect("فیلتر حرکت",badge("برنامه‌ریزی‌شده"))}
        <label class="label-input">ایستگاه<select data-change="board-station">${options.map(id=>opt(id,name(id),state.station)).join("")}</select></label>
        <label class="label-input">خط<select data-change="board-line">${opt(0,"همهٔ خطوط",state.boardLine)}${stations[state.station][2].map(n=>opt(n,"خط "+fa.format(n),state.boardLine)).join("")}</select></label>
        <label class="label-input">جهت<select data-change="board-direction">${opt("","همهٔ جهت‌ها",state.boardDirection)}${directions.map(id=>opt(id,name(id),state.boardDirection)).join("")}</select></label>
        <label class="label-input">از ساعت<input type="time" value="${state.boardAfter}" data-change="board-after"></label>
        ${btn("تاریخ سرویس","calendar","quiet","departures")}
      </section><section class="card stack">${sect("حرکت‌های نمونه",badge(digits(state.date)))}
        ${notice("این زمان‌ها برای نمایش طراحی‌اند. زمان زندهٔ قطار در دسترس این پیش‌نمایش نیست.")}
        <div class="list">${rows.length?rows.map(r=>row("به سوی "+name(r.direction),`<small>خط ${fa.format(r.line)} · برنامهٔ حرکت</small>`,"train","trip",r.id,time(r.time))).join(""):notice("حرکتی در این نمونه با فیلتر فعلی پیدا نشد.")}</div>
      </section></div>`;
  }
  function trip() {
    const selected=state.trip, ride=rides[selected.ride];
    const base=ride.stops.find(([id])=>id===selected.station)?.[1]||ride.start;
    const offset=minutes(selected.time)-minutes(base);
    const stops=ride.stops.map(([id,t])=>[id,clock(minutes(t)+offset),id===selected.station?"ایستگاه انتخابی":""]);
    if (ride.to!==ride.direction) stops.push([ride.direction,clock(minutes(ride.end)+offset+19),"پایانه"]);
    return `${head("برنامهٔ قطار","توقف‌های قطار","به سوی "+name(selected.direction))}
      <section class="card stack">${sect("ترتیب توقف‌ها",line(selected.line))}<div class="badges">${badge("سرویس نمونه")}${badge(digits(state.date))}</div>
        ${timeline(stops,selected.line)}<p class="caption">این فهرست کوتاه‌شده است و همهٔ توقف‌ها را نمایش نمی‌دهد.</p>${link("بازگشت به تابلو","departures","quiet")}
      </section>`;
  }
  function alertsScreen(detail) {
    if (detail) {
      const item=alerts.find(a=>a.id===state.alertId);
      return `${head("اطلاعیهٔ نمونه",item.title,"شبکهٔ متروی تهران")}
        <section class="card stack"><div class="badges">${line(item.line)}${badge(item.state==="active"?"در جریان":"آینده","warn")}</div>
        <p>${item.body}</p>${notice("اطلاعیهٔ نمایشی است و وضعیت فعلی سرویس را گزارش نمی‌کند.","warn")}
        ${link("بازگشت به اطلاعیه‌ها","alerts","quiet")}</section>`;
    }
    return `${head("وضعیت سرویس","اطلاعیه‌ها","اطلاعیه‌های فعال و تغییرات برنامه‌ریزی‌شده.")}
      <section class="card stack"><div class="chip-row">${tab("در جریان","alert-tab","active",state.alertTab==="active")}
        ${tab("آینده","alert-tab","upcoming",state.alertTab==="upcoming")}</div>
        ${notice("اطلاعیه‌های این صفحه ساختگی‌اند.","warn")}
        <div class="list">${alerts.filter(a=>a.state===state.alertTab).map(a=>row(a.title,`<small>خط ${fa.format(a.line)} · اطلاعیهٔ نمونه</small>`,"bell","alert",a.id)).join("")}</div>
      </section>`;
  }
  function calendar() {
    const date = new Date(state.date+"T12:00:00Z");
    const weekday=date.getUTCDay();
    const type=weekday===5?"جمعه یا تعطیل رسمی":weekday===4?"پنجشنبه":"شنبه تا چهارشنبه";
    const localized=new Intl.DateTimeFormat("fa-IR-u-ca-persian",{dateStyle:"full",timeZone:"Asia/Tehran"}).format(date);
    return `${head("برنامهٔ سفر","تاریخ و ساعت سرویس","زمان‌ها بر اساس ساعت محلی تهران نمایش داده می‌شوند.")}
      <section class="card stack"><label class="label-input">تاریخ سرویس (میلادی)<input type="date" data-change="date" value="${state.date}"></label>
      <label class="label-input">حرکت پس از ساعت<input type="time" data-change="${state.calendarReturn==="departures"?"board-after":"after"}" value="${state.calendarReturn==="departures"?state.boardAfter:state.after}"></label>
      <div class="setting"><span>تاریخ انتخابی</span><strong>${localized}</strong></div>
      <div class="setting"><span>نوع برنامهٔ پیش‌فرض</span>${badge(type)}</div>
      ${notice("وضعیت تعطیلی رسمی این تاریخ تأیید نشده است.")}
      ${link("اعمال و بازگشت",state.calendarReturn)}</section>`;
  }
  function fares() {
    return `${head("اطلاعات سفر","کرایه‌ها","قیمت محصولات بر اساس تاریخ اعتبار و به ریال نمایش داده می‌شود.")}
      <section class="card stack"><div class="badges">${badge("ریال ایران")}${badge(digits(state.date))}</div>
      ${empty("wallet","قیمت ثبت‌شده‌ای نیست","این پیش‌نمایش قیمت رسمی یا مبلغ قابل پرداخت نمایش نمی‌دهد.",link("بازگشت به تنظیمات","settings","quiet"))}
      </section>`;
  }
  function favorites() {
    if (!state.auth) return `${head("ایستگاه‌های من","نشان‌شده‌ها","ایستگاه‌های پررفت‌وآمد را نزدیک نگه دارید.")}
      ${empty("heart","برای ذخیره وارد شوید","جست‌وجو و برنامه‌ریزی سفر بدون حساب در دسترس‌اند.",btn("ورود آزمایشی","require-sign-in","","favorites"))}`;
    if (!state.favorites.length) return emptyFavorites();
    return `${head("ایستگاه‌های من","نشان‌شده‌ها","به تابلوی حرکت ایستگاه‌های ذخیره‌شده سریع‌تر برسید.")}
      <section class="card stack">${sect("ایستگاه‌های ذخیره‌شده",badge(fa.format(state.favorites.length)))}
      <div class="list">${state.favorites.map(id=>`<div class="form-grid">${stationRow(id)}<button class="icon-button" type="button" data-action="favorite" data-value="${id}" aria-label="حذف ${name(id)}">${icon("heart")}</button></div>`).join("")}</div>
      ${link("جست‌وجوی ایستگاه","stations","quiet")}</section>`;
  }
  function emptyFavorites() {
    return `${head("ایستگاه‌های من","نشان‌شده‌ها","برای سفرهای تکراری، ایستگاه‌های دلخواه را ذخیره کنید.")}
      ${empty("heart","هنوز ایستگاهی نشان نشده","ایستگاهی را باز کنید و دکمهٔ نشان کردن را بزنید.",link("دیدن ایستگاه‌ها","stations"))}`;
  }
  function signIn() {
    return `${head("حساب کاربری","ورود به Rail","ذخیرهٔ ایستگاه‌ها به حساب کاربری نیاز دارد.")}
      <section class="card feature"><div class="feature-content"><img class="feature-mark" src="../00-assets/symbol-ivory.svg" alt=""><h2>ایستگاه‌های خودتان</h2>
      <p class="caption">این صفحه فقط ورود آزمایشی را شبیه‌سازی می‌کند.</p></div>${heroArt}</section>
      <section class="card stack section">${btn(icon("person")+" استفاده از حساب نمونه","sign-in","full")}
      ${link("ادامه بدون حساب","home","quiet")}${notice("پیش‌نمایش اطلاعات ورود یا گذرواژه جمع‌آوری نمی‌کند.")}</section>`;
  }
  function settings() {
    return `${head("ترجیحات","تنظیمات","نمایش و ابزارهای سفر را مرور کنید.")}
      <div class="layout wide"><section class="card stack">${sect("نمایش")}
      <div class="setting"><span>پوستهٔ تیره</span>${tab(state.theme==="dark"?"روشن کردن":"تیره کردن","theme","",state.theme==="dark")}</div>
      <div class="setting"><span>نام انگلیسی ایستگاه‌ها</span>${tab(state.names?"نمایش داده می‌شود":"پنهان است","names","",state.names)}</div>
      <div class="setting"><span>حساب نمونه</span>${state.auth?btn("خروج آزمایشی","sign-out","quiet"):btn("ورود آزمایشی","require-sign-in","quiet","settings")}</div></section>
      <section class="card stack">${sect("ابزار سفر")}
      ${row("کرایه‌ها","<small>محصولات و قیمت معتبر</small>","wallet","navigate","fares")}
      ${row("تاریخ سرویس","<small>برنامهٔ روز و وضعیت تعطیلی</small>","calendar","calendar","settings")}
      ${row("اطلاعیه‌ها","<small>وضعیت و تغییر برنامه</small>","bell","navigate","alerts")}</section></div>`;
  }
  function offline() {
    return `${head("وضعیت اتصال","اتصال در دسترس نیست","نمونهٔ رفتار برنامه هنگام قطع ارتباط.")}
      <div class="layout wide">${empty("wifi","نقشه همچنان قابل مرور است","در پیاده‌سازی نهایی، دادهٔ ذخیره‌شده باید تاریخ آخرین به‌روزرسانی داشته باشد.",btn("تلاش دوباره","navigate","","home")+link("مرور نقشه","network","quiet"))}
      <section class="card stack">${sect("نمودار نمونه")}${schematic()}</section></div>`;
  }
  function error() {
    return `${head("برنامه‌ریز سفر","خطا در دریافت نتیجه","مبدأ، مقصد و زمان سفر حفظ شده‌اند.")}
      ${empty("alert","دوباره تلاش کنید","این خطا نمایشی است. می‌توانید جست‌وجو را تکرار کنید یا انتخاب‌ها را تغییر دهید.",btn("تلاش دوباره","plan")+link("ویرایش مسیر","home","quiet"))}`;
  }

  const views = {
    home:planner,picker:()=>directory(true),journeys:()=>journeys(false),journey:()=>journeys(true),
    "no-journey":noJourney,network,stations:()=>directory(false),station,departures,trip,
    alerts:()=>alertsScreen(false),alert:()=>alertsScreen(true),fares,calendar,favorites,
    "sign-in":signIn,settings,offline,error,empty:emptyFavorites
  };
  const nav = [
    ["home","مسیر","home",["home","picker","journeys","journey","no-journey","calendar","error"]],
    ["network","شبکه","map",["network","stations","station","departures","trip","offline"]],
    ["alerts","اطلاعیه‌ها","bell",["alerts","alert","fares"]],
    ["favorites","نشان‌شده‌ها","heart",["favorites","empty","settings","sign-in"]]
  ];
  const indexFor = id => screens.findIndex(s=>s[0]===id)+1;
  function buildStudio() {
    let group="";
    $("#screen-list").innerHTML=screens.map(([id,title,category])=>{
      const groupHeading=category!==group?`<h2>${category}</h2>`:"";
      group=category;
      return `${groupHeading}<a href="#${id}" data-go="${id}">${String(indexFor(id)).padStart(2,"0")} · ${title}</a>`;
    }).join("");
  }
  function render(focus=false) {
    const [,title,category,why,tip]=screenById.get(current);
    $("#studio").classList.toggle("desktop",state.layout==="desktop");
    $("#app").dataset.theme=state.theme;
    $("#studio-category").textContent=category+" / "+String(indexFor(current)).padStart(2,"0");
    $("#studio-title").textContent=title;
    $("#studio-notes").innerHTML=`<div><small>Purpose</small><h2>${why}</h2><p>${tip}</p></div><div><small>Preview data</small><p>Offline sample UI. Times and notices are illustrative.</p></div>`;
    $("#theme-control").setAttribute("aria-pressed",state.theme==="dark");
    $("#layout-control").setAttribute("aria-pressed",state.layout==="desktop");
    for (const anchor of document.querySelectorAll("#screen-list a")) {
      if (anchor.dataset.go===current) anchor.setAttribute("aria-current","page");
      else anchor.removeAttribute("aria-current");
    }
    $("#app").innerHTML=`<header class="app-header"><a href="#home" data-go="home" class="brand" aria-label="Rail، برنامه‌ریز"><img src="../00-assets/app-icon.svg" alt=""><span>Rail</span></a>
      <div class="header-meta"><span class="demo">دادهٔ نمونه</span><a href="#settings" data-go="settings" class="icon-button" aria-label="تنظیمات">${icon("person")}</a></div></header>
      <div class="app-frame"><main class="app-content">${views[current]()}</main><nav class="tabbar" aria-label="پیمایش اصلی">
      ${nav.map(([id,label,glyph,group])=>`<a href="#${id}" data-go="${id}" ${group.includes(current)?'aria-current="page"':""}>${icon(glyph)}<span>${label}</span></a>`).join("")}</nav></div>`;
    if ($("#station-query")) $("#station-query").value=state.query;
    if (focus) $("#app h1")?.focus({preventScroll:true});
  }
  function go(id,{historyEntry=true,focus=false}={}) {
    if (!screenById.has(id)) id="home";
    if (current!==id && ["picker","stations"].includes(id)) state.query="";
    current=id;
    if (historyEntry && location.hash!=="#"+id) history.pushState(null,"","#"+id);
    render(focus);
    if (focus) window.scrollTo(0,0);
  }
  function adjustBoard(id) {
    state.station=id;
    state.boardLine=0;
    state.boardDirection="";
  }
  function action(key,value) {
    switch (key) {
      case "navigate": return go(value,{focus:true});
      case "pick": state.field=value; return go("picker",{focus:true});
      case "choose": state[state.field]=value; state.planError=false; return go("home",{focus:true});
      case "swap": [state.from,state.to]=[state.to,state.from]; state.planError=false; break;
      case "plan":
        if (state.from===state.to) { state.planError=true; break; }
        return go(demoItinerary().length?"journeys":"no-journey",{focus:true});
      case "calendar": state.calendarReturn=value; return go("calendar",{focus:true});
      case "station": state.station=value; return go("station",{focus:true});
      case "departures": adjustBoard(value); return go("departures",{focus:true});
      case "trip": state.trip=board.find(r=>r.id===value); return go("trip",{focus:true});
      case "favorite":
        if (!state.auth) { state.signInReturn=current; return go("sign-in",{focus:true}); }
        state.favorites=state.favorites.includes(value)?state.favorites.filter(id=>id!==value):[...state.favorites,value];
        break;
      case "directory-line": state.directoryLine=Number(value); break;
      case "map-line": state.mapLine=Number(value); break;
      case "alert-tab": state.alertTab=value; break;
      case "alert": state.alertId=value; return go("alert",{focus:true});
      case "theme": state.theme=state.theme==="light"?"dark":"light"; break;
      case "names": state.names=!state.names; break;
      case "require-sign-in": state.signInReturn=value; return go("sign-in",{focus:true});
      case "sign-in": state.auth=true; return go(state.signInReturn,{focus:true});
      case "sign-out": state.auth=false; break;
    }
    render();
    const target=[...$("#app").querySelectorAll("[data-action]")].find(el=>el.dataset.action===key&&el.dataset.value===value);
    target?.focus({preventScroll:true});
  }

  document.addEventListener("click",event=>{
    const target=event.target.closest("[data-action],[data-go]");
    if (!target || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
    event.preventDefault();
    if (target.dataset.action) action(target.dataset.action,target.dataset.value);
    else go(target.dataset.go,{focus:true});
  });
  $("#app").addEventListener("input",event=>{
    if (event.target.id!=="station-query") return;
    state.query=event.target.value;
    $("#station-list").innerHTML=stationList(current==="picker");
  });
  $("#app").addEventListener("change",event=>{
    const field=event.target.dataset.change, value=event.target.value;
    if (!field) return;
    switch(field) {
      case "board-station": adjustBoard(value); break;
      case "board-line": state.boardLine=Number(value); state.boardDirection=""; break;
      case "board-direction": state.boardDirection=value; break;
      case "board-after": state.boardAfter=value||"00:00"; break;
      case "date": state.date=value||demoDate; break;
      case "after": state.after=value||"08:00"; break;
    }
    render();
    $("#app [data-change='"+field+"']")?.focus({preventScroll:true});
  });
  $("#theme-control").addEventListener("click",()=>{state.theme=state.theme==="light"?"dark":"light";render();});
  $("#layout-control").addEventListener("click",()=>{state.layout=state.layout==="mobile"?"desktop":"mobile";render();});
  $("#reset-control").addEventListener("click",()=>{state=defaults();go("home");});
  window.addEventListener("popstate",()=>go(location.hash.slice(1),{historyEntry:false}));
  window.addEventListener("hashchange",()=>{if(location.hash.slice(1)!==current) go(location.hash.slice(1),{historyEntry:false});});
  document.body.classList.toggle("capture",params.get("capture")==="1");
  buildStudio();
  go(location.hash.slice(1)||"home",{historyEntry:false});
  window.RailDesign = {screens, show:go};
})();

