from pathlib import Path
import json
import os

from PIL import Image, ImageDraw, ImageFont

root = Path(__file__).resolve().parent.parent
manifest = json.loads((root / "MANIFEST.json").read_text(encoding="utf-8"))
overview = root / "05-overview"
jpg_folder = root / "02-screens" / "jpg"
jpg_folder.mkdir(parents=True, exist_ok=True)
overview.mkdir(exist_ok=True)

font_root = Path(os.environ.get("WINDIR", "C:/Windows")) / "Fonts"
palette = {"paper": "#e9efeb", "ink": "#102c34", "teal": "#00636a", "muted": "#465f62", "white": "#fffefa"}

def font(size, bold=False):
    try:
        return ImageFont.truetype(str(font_root / ("segoeuib.ttf" if bold else "segoeui.ttf")), size)
    except OSError:
        return ImageFont.load_default(size)

def contact_sheet(filename, title, subtitle, screens, *, desktop=False):
    columns = 2 if desktop else 3
    width = 1600 if desktop else 1500
    cell_height = 470 if desktop else 526
    rows = (len(screens) + columns - 1) // columns
    height = 158 + rows * cell_height + 32
    sheet = Image.new("RGB", (width, height), palette["paper"])
    draw = ImageDraw.Draw(sheet)
    draw.text((47, 27), "RAIL  /  TEHRAN METRO", font=font(17, True), fill=palette["teal"])
    draw.text((46, 53), title, font=font(43, True), fill=palette["ink"])
    draw.text((48, 111), subtitle, font=font(18), fill=palette["muted"])
    gap = 16
    card_width = (width - 96 - (columns - 1) * gap) // columns

    for i, screen in enumerate(screens):
        col, row = i % columns, i // columns
        x, y = 48 + col * (card_width + gap), 151 + row * cell_height
        draw.rounded_rectangle((x, y, x + card_width, y + cell_height - 14), radius=18, fill=palette["white"])
        draw.text((x + 16, y + 13), screen["title"], font=font(21, True), fill=palette["ink"])
        draw.text((x + 17, y + 42), screen["group"], font=font(14), fill=palette["muted"])
        with Image.open(root / screen["file"]) as img:
            frame = img.convert("RGB")
            frame.thumbnail((card_width - 35, cell_height - 88), Image.Resampling.LANCZOS)
            px = x + (card_width - frame.width) // 2
            py = y + 67 + (cell_height - 88 - frame.height) // 2
            draw.rectangle((px - 1, py - 1, px + frame.width, py + frame.height), fill="#c9d9d3")
            sheet.paste(frame, (px, py))

    sheet.save(overview / f"{filename}.png", optimize=True)
    sheet.save(overview / f"{filename}.jpg", quality=90, subsampling=0)

mobile = manifest["mobile"]
for screen in mobile:
    with Image.open(root / screen["file"]) as img:
        img.convert("RGB").save(jpg_folder / (Path(screen["file"]).stem + ".jpg"), quality=94, subsampling=0)

contact_sheet("01-journey-planning", "Plan a Tehran Metro trip", "Station selection, trip result, train changes and an empty result.", mobile[:5])
contact_sheet("02-network-and-departures", "Find the station and the train", "Schematic network, station details and scheduled departures.", mobile[5:10])
contact_sheet("03-service-and-account", "Service information and saved stations", "Illustrative alerts, fare availability, service date and the account gate.", mobile[10:16])
contact_sheet("04-preferences-and-states", "Preferences, edge states and dark mode", "Prototype states for interruption, empty content and alternate appearance.", mobile[16:])
contact_sheet("05-windows-layouts", "Rail on a wider screen", "Right-hand navigation with paired work surfaces for the Windows design.", manifest["desktop"], desktop=True)
print(f"Created {len(mobile)} mobile JPGs and five overview sheets in PNG and JPG.")

