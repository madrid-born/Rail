"use strict";

const fs = require("node:fs");
const path = require("node:path");
const {pathToFileURL} = require("node:url");
const {chromium} = require("playwright");

const root = path.resolve(__dirname, "..");
const edge = "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe";
const executablePath = process.env.RAIL_BROWSER || (fs.existsSync(edge) ? edge : undefined);
const prototype = pathToFileURL(path.join(root, "01-prototype/index.html")).href;
const pad = n => String(n).padStart(2, "0");
const need = (ok, message) => { if (!ok) throw new Error(message); };

async function main() {
  const browser = await chromium.launch({executablePath, headless:true});
  try {
    const page = await browser.newPage({viewport:{width:390,height:844},deviceScaleFactor:2, reducedMotion:"reduce"});
    page.setDefaultTimeout(6500);
    const errors = [];
    page.on("pageerror", error => errors.push(error.message));
    page.on("requestfailed", req => errors.push(req.url()+" "+req.failure()?.errorText));
    page.on("request", req => { if (/^https?:/.test(req.url())) errors.push("Unexpected remote request "+req.url()); });
    const capture = async (id, options="") => {
      await page.goto("about:blank");
      await page.goto(prototype+"?capture=1"+options+"#"+id);
      await page.waitForFunction(()=>window.RailDesign && document.querySelector("#app h1"));
    };
    const screenshot = async file => {
      const target = path.join(root,file);
      fs.mkdirSync(path.dirname(target),{recursive:true});
      await page.screenshot({path:target, fullPage:true, animations:"disabled"});
    };
    const layoutCheck = async message => need(
      await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),message
    );
    await capture("home");
    const screens = await page.evaluate(()=>window.RailDesign.screens.map(([id,title,group])=>({id,title,group})));
    const mobile = [];
    for (const [index,screen] of screens.entries()) {
      await capture(screen.id,screen.id==="favorites"?"&account=sample":"");
      await layoutCheck("390px overflow on "+screen.id);
      const file = "02-screens/mobile/"+pad(index+1)+"-"+screen.id+".png";
      await screenshot(file);
      mobile.push({...screen,file});
    }
    for (const id of ["home","journey"]) {
      await capture(id,"&theme=dark");
      await layoutCheck("Dark layout overflow on "+id);
      const file="02-screens/mobile/"+id+"-dark.png";
      await screenshot(file);
      mobile.push({id:id+"-dark",title:id==="home"?"Journey planner · dark":"Journey steps · dark",group:"Dark",file});
    }
    await page.setViewportSize({width:320,height:740});
    for (const screen of screens) {
      await capture(screen.id,screen.id==="favorites"?"&account=sample":"");
      await layoutCheck("320px overflow on "+screen.id);
    }
    await page.setViewportSize({width:1440,height:960});
    const desktop = [];
    for (const id of ["home","journeys","network","station","departures"]) {
      await capture(id,"&layout=desktop");
      await layoutCheck("Desktop overflow on "+id);
      const file="02-screens/desktop/"+id+"-1440.png";
      await screenshot(file);
      desktop.push({...screens.find(s=>s.id===id),file});
    }
    await page.goto(prototype+"#home");
    await page.waitForFunction(()=>window.RailDesign);
    await screenshot("05-overview/prototype-studio.png");
    await page.goto(pathToFileURL(path.join(root,"03-design-system/components.html")).href);
    await screenshot("03-design-system/components.png");

    await page.setViewportSize({width:390,height:844});
    const click = selector=>page.locator("#app "+selector).first().click();
    const on = async (id)=>need(new URL(page.url()).hash==="#"+id,"Expected screen "+id);
    await capture("home");
    await click('[data-action="plan"]');
    await on("journeys");
    await click('[data-go="journey"]');
    await on("journey");
    need(await page.locator("#app .timeline").count()===2,"Transfer itinerary should have two legs");
    await capture("home");
    await click('[data-action="pick"][data-value="to"]');
    await on("picker");
    await page.locator("#station-query").fill("Imam");
    need(await page.locator("#station-list button").count()===1,"English search did not filter station");
    await page.locator("#station-query").fill("امام");
    need(await page.locator("#station-list button").count()===1,"Persian search did not filter station");
    await click('[data-action="choose"][data-value="imam-khomeini"]');
    await click('[data-action="plan"]');
    await click('[data-go="journey"]');
    need(await page.locator("#app .timeline").count()===1,"Direct itinerary should have one leg");
    await capture("home");
    await click('[data-action="swap"]');
    await click('[data-action="plan"]');
    await on("no-journey");
    await capture("home");
    await click('[data-action="pick"][data-value="to"]');
    await click('[data-action="choose"][data-value="tajrish"]');
    await click('[data-action="plan"]');
    await on("home");
    need(await page.locator('#app [role="alert"]').count()===1,"Identical stations should keep the planner open");

    await capture("network");
    await click('[data-action="map-line"][data-value="2"]');
    need(await page.locator('#app [data-action="map-line"][data-value="2"]').getAttribute("aria-pressed")==="true","Map filter did not persist");
    await click('svg [data-action="station"][data-value="imam-khomeini"] .map-hit');
    await on("station");
    await click('[data-action="departures"]');
    await on("departures");
    await page.locator('[data-change="board-line"]').selectOption("2");
    need(await page.locator('#app [data-action="trip"]').count()===2,"Departure line filter did not narrow the board");
    await click('[data-action="trip"]');
    await on("trip");
    need(await page.locator("#app .timeline li").count()>=3,"Missing stops on the trip");
    await click('[data-go="departures"]');
    await click('[data-action="calendar"]');
    await on("calendar");
    await click('[data-go="departures"]');
    await on("departures");

    await capture("station");
    await click('[data-action="favorite"]');
    await on("sign-in");
    await click('[data-action="sign-in"]');
    await on("station");
    await click('[data-action="favorite"]');
    need(await page.locator('#app [data-action="favorite"]').getAttribute("aria-pressed")==="false","Favorite removal failed");
    await click('[data-action="favorite"]');
    need(await page.locator('#app [data-action="favorite"]').getAttribute("aria-pressed")==="true","Favorite addition failed");
    await click('.tabbar [data-go="favorites"]');
    await click('[data-action="favorite"][data-value="imam-khomeini"]');
    await click('[data-action="favorite"][data-value="tajrish"]');
    need(await page.locator("#app .empty h2").textContent()==="هنوز ایستگاهی نشان نشده","Empty favorites state did not appear");

    await capture("alerts");
    await click('[data-action="alert-tab"][data-value="upcoming"]');
    await click('[data-action="alert"][data-value="sample-maintenance"]');
    await on("alert");
    await capture("calendar");
    await page.locator('[data-change="date"]').fill("2026-11-13");
    need((await page.locator("#app .setting").last().textContent()).includes("جمعه"),"Calendar did not update for Friday");
    await capture("settings");
    await click('[data-action="theme"]');
    need(await page.locator("#app").getAttribute("data-theme")==="dark","Theme toggle failed");
    await capture("error");
    await click('[data-action="plan"]');
    await on("journeys");
    await capture("offline");
    await click('[data-action="navigate"][data-value="home"]');
    await on("home");

    need(errors.length===0,errors.join("\n"));
    const manifest={project:"Rail",status:"design proposal",preview:"01-prototype/index.html",mobile,desktop};
    fs.writeFileSync(path.join(root,"MANIFEST.json"),JSON.stringify(manifest,null,2)+"\n");
    fs.mkdirSync(path.join(root,"04-handoff"),{recursive:true});
    fs.writeFileSync(path.join(root,"04-handoff/verification.json"),JSON.stringify({
      browser:browser.version(), mobileScreens:mobile.length, desktopScreens:desktop.length,
      passed:["Offline file:// loading","No remote requests or JavaScript errors","No horizontal overflow at 320 or 390 pixels",
        "Direct and transfer journeys","Persian and English station search","Distinct-station validation and no-result state","Map filter and station links",
        "Departure line filter and trip drill-in","Account gate and favorite add/remove","Empty favorites",
        "Upcoming alert drill-in","Service date, weekday and return navigation","Dark theme","Offline and error retry"],
      scope:"Browser prototype only. No native app, API call, real account or real schedule was exercised."
    },null,2)+"\n");
    console.log("Rendered "+mobile.length+" mobile and "+desktop.length+" desktop frames. Interaction checks passed.");
  } finally {
    await browser.close();
  }
}

main().catch(error=>{ console.error(error); process.exitCode=1; });
