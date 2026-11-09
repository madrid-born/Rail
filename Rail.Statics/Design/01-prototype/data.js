"use strict";

window.RailFixtures = {
  lines: {
    1:"#E0001F", 2:"#2F4389", 3:"#67C5F5", 4:"#F8E100",
    5:"#007E46", 6:"#EF639F", 7:"#7F0B74"
  },
  // Names, line memberships and IDs are from the imported Rail catalog.
  stations: {
    "tajrish":["تجریش","Tajrish",[1]],
    "imam-khomeini":["امام خمینی","Imam Khomeini",[1,2]],
    "farhangsara":["فرهنگسرا","Farhangsara",[2]],
    "darvazeh-dolat":["دروازه دولت","Darvazeh Dolat",[1,4]],
    "darvazeh-shemiran":["دروازه شمیران","Darvazeh Shemiran",[2,4]],
    "eram-e-sabz":["ارم سبز","Eram-e Sabz",[4,5]],
    "meydan-e-azadi":["میدان آزادی","Meydan-e Azadi",[4]],
    "tehran-sadeghiyeh":["تهران (صادقیه)","Tehran (Sadeghiyeh)",[2,5]],
    "shahid-kolahdooz":["شهید کلاهدوز","Shahid Kolahdooz",[4]],
    "golshahr":["گلشهر","Golshahr",[5]],
    "bimeh":["بیمه","Bimeh",[4]],
    "mehrabad-airport-terminal-4-6":["پایانه ۴ و ۶ فرودگاه مهرآباد","Mehrabad Airport Terminal 4 & 6",[4]],
    "shahr-e-parand":["شهر پرند","Shahr-e Parand",[1]],
    "kahrizak":["کهریزک","Kahrizak",[1]]
  },
  // Fictional sample times, including the transfer assumption. No operating advice.
  rides: [
    {from:"tajrish",to:"imam-khomeini",line:1,direction:"kahrizak",start:"08:05",end:"08:27",stops:[
      ["tajrish","08:05"],["darvazeh-dolat","08:22"],["imam-khomeini","08:27"]
    ]},
    {from:"imam-khomeini",to:"farhangsara",line:2,direction:"farhangsara",start:"08:33",end:"08:52",stops:[
      ["imam-khomeini","08:33"],["darvazeh-shemiran","08:40"],["farhangsara","08:52"]
    ]}
  ],
  board: [
    {id:"l1-0805",station:"tajrish",line:1,direction:"kahrizak",time:"08:05",ride:0},
    {id:"l1-0813",station:"tajrish",line:1,direction:"kahrizak",time:"08:13",ride:0},
    {id:"l1-0821",station:"tajrish",line:1,direction:"kahrizak",time:"08:21",ride:0},
    {id:"l1-0827",station:"imam-khomeini",line:1,direction:"kahrizak",time:"08:28",ride:0},
    {id:"l2-0833",station:"imam-khomeini",line:2,direction:"farhangsara",time:"08:33",ride:1},
    {id:"l2-0840",station:"imam-khomeini",line:2,direction:"farhangsara",time:"08:41",ride:1}
  ],
  alerts: [
    {id:"sample-delay",line:4,state:"active",title:"افزایش فاصلهٔ حرکت قطارها",body:"این پیام فرضی برای نمایش اطلاعیهٔ تأخیر است. در نسخهٔ عملیاتی، بازهٔ زمانی و مرجع اطلاعیه در این بخش نمایش داده می‌شود."},
    {id:"sample-maintenance",line:1,state:"upcoming",title:"تغییر برنامهٔ سرویس",body:"این پیام فرضی نمونهٔ اطلاعیه‌ای است که پیش از آغاز یک تغییر برنامه منتشر شده است."}
  ],
  screens: [
    ["home","Journey planner","Travel","Start with a station pair","Search and plan as a guest. Sample stations are prefilled to make the flow easy to try."],
    ["picker","Station picker","Travel","Make picking a station quick","Search in Persian or English. Select a station, then return to the planner."],
    ["journeys","Journey result","Travel","Compare the times that matter","Show first boarding, arrival, and transfer count. The prototype has one itinerary for the sample request."],
    ["journey","Journey steps","Travel","Make each change of train clear","Read the line, direction, boarding station, exit station, and estimated transfer time."],
    ["no-journey","No journey","Travel","Offer a useful next step","An empty itinerary is a valid result. Keep the selections and let riders change their search."],
    ["network","Network map","Explore","A schematic with useful touch points","Select a line or a station. This is a partial diagram; line and station names remain available in a list."],
    ["stations","Station directory","Explore","Find stations by name or line","Filter the local catalog sample in Persian or English. Preserve stable station IDs in the app."],
    ["station","Station detail","Explore","Lead with the interchange and boarding options","Show line membership, departures, and the status of station information. Favorite is an account action."],
    ["departures","Departure board","Explore","Keep scheduled and live data distinct","Filter the sample board by line, direction, and local time. Tapping a departure opens its trip."],
    ["trip","Scheduled trip","Explore","Read the train’s stopping order","Show stop times and the advertised direction; keep the service date visible."],
    ["alerts","Service alerts","Service","Separate current from upcoming","Switch between two illustrative alert windows. The production catalog may be empty."],
    ["alert","Alert detail","Service","Give the affected line and the source","A live record should include the publication window and source when available."],
    ["fares","Fare information","Service","Handle missing published prices","Show the currency and effective date in production. This sample contains no tariff amounts."],
    ["calendar","Service calendar","Service","Expose the day type and its verification","Use a Tehran service date. Unknown holiday verification is shown explicitly."],
    ["favorites","Favorite stations","Account","Make repeat trips easier","Open a saved station, remove it, or try the account and empty states."],
    ["sign-in","Sign-in","Account","Defer account creation until it adds value","Use the sample-account button to exercise favorites. The preview sends no credentials."],
    ["settings","Preferences","Account","Put presentation choices in reach","Change theme, toggle English station names, or inspect fare and calendar screens."],
    ["offline","Offline","States","Keep the schematic available","Demonstrate a restricted offline view and a simulated retry. Cache behavior needs implementation."],
    ["error","Request error","States","Recover without losing the trip form","Retry the sample journey or return to the planner."],
    ["empty","Empty favorites","States","Explain where favorites come from","Send riders to the station list to save their first station."]
  ]
};
