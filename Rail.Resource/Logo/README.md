# Rail logo kit

Rail is the Tehran Metro rider app. This kit uses the selected #03 Azadi Tower and R symbol in Persian Teal. The symbol was redrawn with straight track edges and smooth vector curves. The horizontal and stacked versions pair it with an outlined Segoe UI Bold wordmark.

## Start here

- Use [Rail-Horizontal-Persian-Teal.svg](02-SVG/Rail-Horizontal-Persian-Teal.svg) for headers and wide placements.
- Use [Rail-Stacked-Persian-Teal.svg](02-SVG/Rail-Stacked-Persian-Teal.svg) when the logo has more vertical space.
- Use [Rail-App-Icon-1024.png](03-PNG/Rail-App-Icon-1024.png) as the general-purpose rounded icon.
- Use the [preview sheet](Rail-Logo-Preview.png) to compare the logo layouts and monochrome marks.

| Folder | Contents |
| --- | --- |
| `01-Masters` | Layered Photoshop files for the app icon, horizontal logo, and stacked logo. The background, tile, symbol, and wordmark are separate raster layers where applicable. |
| `02-SVG` | Scalable outlines for both logo layouts, the standalone symbol, and rounded and full-bleed icons. Text is outlined and travels with the file. |
| `03-PNG` | Transparent PNGs for logos and symbols, plus rounded app icons from 64 to 1024 px. |
| `04-JPG` | Flattened sRGB JPGs with white or teal backgrounds. |
| `05-Monochrome` | Solid black and solid white symbols and logos in SVG, PNG, and JPG, plus reversed black and white icon tiles. |
| `06-App-and-Web` | Full-square Apple and Play Store PNGs, Android adaptive foreground and background layers, standard and maskable PWA PNGs, and browser favicons. The ICO contains 16, 32, and 48 px sizes. |
| `07-Social` | PNG and JPG artwork at 1080 × 1080 (1:1), 1080 × 1350 (4:5), 1080 × 1920 (9:16), 1920 × 1080 (16:9), and 1200 × 630 (Open Graph). |
| [`Maui`](Maui/README.md) | .NET MAUI app icon and splash SVGs, optional in-app wordmarks, and project-file settings. |

## Colors

| Color | sRGB hex | Use |
| --- | --- | --- |
| Persian Teal | `#00636A` | Primary tile and dark brand background |
| Warm Ivory | `#FFF4DE` | Primary symbol and reversed wordmark |
| Ink | `#102C34` | Wordmark on light backgrounds |
| Black | `#000000` | Single-color mark on light backgrounds |
| White | `#FFFFFF` | Single-color mark on dark backgrounds |

Use the ivory or white artwork on dark surfaces. For iOS, Android, or a maskable PWA icon, start with the corresponding full-square asset so the platform can apply its own mask. Use the SVG artwork when resizing for large-format or print layouts. The supplied colors are sRGB.
