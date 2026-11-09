"use strict";

const mobile = RailFixtures.screens.map(([id,title,group],i)=>[title,group,"../02-screens/mobile/"+String(i+1).padStart(2,"0")+"-"+id+".png"]);
mobile.push(["Journey planner · dark","Dark","../02-screens/mobile/home-dark.png"],["Journey steps · dark","Dark","../02-screens/mobile/journey-dark.png"]);
const desktop = ["home","journeys","network","station","departures"].map(id=>[RailFixtures.screens.find(row=>row[0]===id)[1],"Desktop","../02-screens/desktop/"+id+"-1440.png"]);
const card=([name,group,file])=>'<figure><a href="'+file+'"><img src="'+file+'" alt="'+name+'" loading="lazy"></a><figcaption>'+name+'<small>'+group+'</small></figcaption></figure>';
document.getElementById("gallery").innerHTML='<h2>Mobile</h2><div class="gallery-grid">'+mobile.map(card).join("")+'</div><h2>Desktop</h2><div class="gallery-grid wide">'+desktop.map(card).join("")+'</div>';
